using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

/// <summary>
/// Hosts RPC services over asynchronous channel acceptors, managing sessions and service instances.
/// </summary>
public class ServiceHostAsync : IAsyncRunnable, IServiceHost
{
    private static readonly ILogger<ServiceHost> _logger = RpcLogging.CreateLogger<ServiceHost>();

    private record struct ServiceData(ShareWithin Shared, Func<object> Factory);

    private static readonly Dictionary<Type, RefCounter<ICallTarget>> _globalInstances = [];

    private readonly List<Func<IAsyncChannelAcceptor>> _factories = [];

    private readonly Dictionary<Type, ServiceData> _services = [];

    private readonly Dictionary<Type, RefCounter<ICallTarget>> _hostInstances = [];

    private readonly List<Session> _sessions = [];

    private readonly Lazy<IServiceCatalog> _serviceCatalog;

    private TaskCompletionSource? _tcs;
    private readonly List<Func<IAsyncChannelAcceptor>> _newFactories = [];


    /// <summary>
    /// Initializes a new instance of <see cref="ServiceHostAsync"/> with the specified acceptor factories.
    /// </summary>
    public ServiceHostAsync()
    {
        _serviceCatalog = new Lazy<IServiceCatalog>(CreateServiceCatalog);
    }

    /// <summary>
    /// Gets a value indicating whether the service host is currently running.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock(_sessions)
                return _tcs is not null;
        }
    }

    /// <summary>
    /// Starts the service host and runs until the cancellation token is cancelled, accepting and processing connections from all configured acceptors.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token used to stop the host.</param>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        List<Task> tasks = [];
        Task cancellationTask = cancellationToken.WaitForCancellationAsync();
        Task eventTask;

        lock(_sessions)
        {
            if (_tcs is not null)
                throw new InvalidOperationException("The service host is already running");

            _tcs = new TaskCompletionSource();
            eventTask = _tcs.Task;

            tasks.Add(eventTask);
            tasks.Add(cancellationTask);
            tasks.AddRange(_factories.Select(x => HandleConnectionsAsync(x, cancellationToken)));
        }

        try
        {
            while(!cancellationToken.IsCancellationRequested && tasks.Count > 0)
            {
                var task = await Task.WhenAny(tasks.ToImmutableArray()).ConfigureAwait(false);

                if (task == cancellationTask)
                    return;

                tasks.Remove(task);

                if (task == eventTask)
                {
                    Func<IAsyncChannelAcceptor>[] factories;
                    lock(_sessions)
                    {
                        factories = [.. _newFactories];
                        _newFactories.Clear();

                        if (_tcs is null || _tcs.Task.IsCompleted)
                            _tcs = new TaskCompletionSource();

                        eventTask = _tcs.Task;
                        tasks.Add(eventTask);
                    }

                    tasks.AddRange(factories.Select(x => HandleConnectionsAsync(x, cancellationToken)));
                }

                if (task.IsFaulted)
                {
                    if (_logger.IsEnabled(LogLevel.Error))
                        _logger.LogError("Connection handling is faulted. Exception {exception}", task.Exception);                    
                }
            }            
        }
        finally
        {
            Session[] sessions;

            lock(_sessions)
            {
                sessions = [.. _sessions];
                _sessions.Clear();

                _tcs = null;
            }

            foreach(var s in sessions)
            {
                if (Disconnected is not null)
                    Disconnected(this, new SessionEventArgs(s));
                    
                await s.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public IDisposable RegisterAcceptor(Func<IAsyncChannelAcceptor> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock(_sessions)
        {
            if (_factories.Contains(factory))
                throw new InvalidOperationException("The factory is already registered");

            _factories.Add(factory);

            if (_tcs is not null)
            {
                _newFactories.Add(factory);
                _tcs.TrySetResult();            
            }
        }

        return Disposable.Create(() =>
        {
            lock(_sessions)
                _factories.Remove(factory); 
        });
    }

    /// <summary>
    /// Registers a service with the specified interface type, sharing scope, and factory.
    /// </summary>
    /// <typeparam name="T">The interface type of the service. Must be an interface.</typeparam>
    /// <param name="share">The scope within which service instances are shared.</param>
    /// <param name="factory">The factory used to create service instances.</param>
    public void RegisterSerive<T>(ShareWithin share, Func<T> factory) where T : class
    {
        using var _ = _logger.Scope();

        if (!typeof(T).IsInterface)
            throw new ArgumentException("Type must be an interface");

        ArgumentNullException.ThrowIfNull(factory);

        lock (_services)
        {
            if (_services.ContainsKey(typeof(T)))
                throw new ArgumentException($"Service with type {typeof(T)} is already registered.");

            _services.Add(typeof(T), new ServiceData(Shared: share, Factory: () => factory()));

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Registered service '{type}', shared within {share}", typeof(T), share);
        }
    }

    private volatile bool _publishServicesCatalog = false;
    /// <summary>
    /// Gets or sets a value indicating whether the service catalog is published and discoverable by clients.
    /// </summary>
    public bool PublishServicesCatalog
    {
        get
        {
            lock (_services)
                return _publishServicesCatalog;
        }
        set
        {
            lock (_services)
                _publishServicesCatalog = value;
        }
    }

    /// <summary>
    /// Occures when a client connects and a new session is created.
    /// </summary>
    public event EventHandler<SessionEventArgs>? Connected;

    /// <summary>
    /// Occures when a client disconnects and the associated session is disposed.
    /// </summary>
    public event EventHandler<SessionEventArgs>? Disconnected;

    (ShareWithin Shared, Func<object> Factory) IServiceHost.GetServiceInfo(Type type)
    {
        lock (_services)
        {
            if (type == typeof(IServiceCatalog) && _publishServicesCatalog)
                return (ShareWithin.Host, () => _serviceCatalog.Value);

            if (_services.TryGetValue(type, out var serviceData))
                return (serviceData.Shared, serviceData.Factory);

            throw new Exception($"Unknown service {type}");
        }
    }

    Type? IServiceHost.FindTypeById(Guid id)
    {
        Type[] types;
        lock (_services)
        {
            if (typeof(IServiceCatalog).GUID == id && _publishServicesCatalog)
                return typeof(IServiceCatalog);

            types = [.. _services.Keys];
        }

        return types.Where(x => x.GUID == id).FirstOrDefault();
    }

    RefCounter<ICallTarget> IServiceHost.CreateHostInstance(Type type, Func<ICallTarget> factory)
    {
        return CreateInstance(type, factory, _hostInstances);
    }

    RefCounter<ICallTarget> IServiceHost.CreateGlobalInstance(Type type, Func<ICallTarget> factory)
    {
        return CreateInstance(type, factory, _globalInstances);
    }

    private void OnConnected(Session session)
    {
        lock(_sessions)
            _sessions.Add(session);

        if (Connected is not null)
            Connected(this, new SessionEventArgs(session));
    }

    private void OnDisconnected(Session session)
    {
        // A single disconnect can raise both Closed and Faulted, so guard against
        // handling the same session more than once. Remove() returns false when the
        // session was already removed by an earlier notification.
        lock(_sessions)
        {
            if (!_sessions.Remove(session))
                return;
        }

        if (Disconnected is not null)
            Disconnected(this, new SessionEventArgs(session));

        session.Dispose();
    }

    private static RefCounter<ICallTarget> CreateInstance(Type type, Func<ICallTarget> factory, Dictionary<Type, RefCounter<ICallTarget>> storage)
    {
        lock (storage)
        {
            if (!storage.TryGetValue(type, out var refCounter))
            {
                refCounter = new RefCounter<ICallTarget>(factory(), x =>
                {
                    lock (storage)
                        storage.Remove(type);
 
                    if (x is IDisposable d) d.Dispose();
                });
                storage.Add(type, refCounter);
            }

            return refCounter;
        }
    }

    private class ServiceCatalog : IServiceCatalog
    {
        private readonly Dictionary<Type, ServiceData> _services;

        public ServiceCatalog(Dictionary<Type, ServiceData> services)
        {
            _services = services;
        }
        
        public Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken cancellationToken)
        {
            lock (_services)
                return Task.FromResult<IReadOnlyList<ServiceInfo>>(_services.Select(x => ServiceInfo.Create(x.Key, x.Value.Shared)).ToList());
        }
    }

    private ServiceCatalog CreateServiceCatalog()
    {
        return new ServiceCatalog(_services);
    }

    private async Task HandleConnectionsAsync(Func<IAsyncChannelAcceptor> factory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IAsyncChannelAcceptor acceptor;
        try
        {
            acceptor = factory();
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Cannot create acceptor");
            return;
        }

        try
        {
            while(!cancellationToken.IsCancellationRequested)
            {
                var channel = await acceptor.AcceptAsync(cancellationToken).ConfigureAwait(false);
                if (channel is null) return;

                if (channel.State != ChannelState.Opened) continue;

                var session = new Session(new QueueChannel(channel), this);
                channel.BoundObject = session;

                channel.Closed += (s, e) => ThreadPool.QueueUserWorkItem(OnDisconnected, session, true);
                channel.Faulted += (s, e) => ThreadPool.QueueUserWorkItem(OnDisconnected, session, true);

                OnConnected(session);
            }

            cancellationToken.ThrowIfCancellationRequested();            
        }
        finally
        {
            if (acceptor is IAsyncDisposable ad)
                await ad.DisposeAsync().ConfigureAwait(false);
            else if (acceptor is IDisposable d)
                d.Dispose();
        }
    }
}