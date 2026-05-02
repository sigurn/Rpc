using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

public class ServiceHostAsync : IAsyncRunnable, IServiceHost
{
    private static readonly ILogger<ServiceHost> _logger = RpcLogging.CreateLogger<ServiceHost>();

    private record struct ServiceData(ShareWithin Shared, Func<object> Factory);

    private static readonly Dictionary<Type, RefCounter<ICallTarget>> _globalInstances = new();

    private readonly Func<IAsyncChannelAcceptor>[] _factories;

    private readonly Dictionary<Type, ServiceData> _services = new();

    private readonly Dictionary<Type, RefCounter<ICallTarget>> _hostInstances = new();

    private readonly List<Session> _sessions = new();

    private readonly Lazy<IServiceCatalog> _serviceCatalog;

    private volatile bool _isRunning = false;

    public ServiceHostAsync(params Func<IAsyncChannelAcceptor>[] factories)
    {
        ArgumentNullException.ThrowIfNull(factories);

        _serviceCatalog = new Lazy<IServiceCatalog>(CreateServiceCatalog);
        _factories = factories;
    }

    public bool IsRunning
    {
        get
        {
            lock(_sessions)
                return _isRunning;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        lock(_sessions)
        {
            if (_isRunning) throw new InvalidOperationException("The service host is already running");
            _isRunning = true;
        }

        var acceptors = _factories.Select(x => x()).ToArray();

        try
        {
            List<Task<IChannel>> tasks = acceptors
                .Select(x => x.AcceptAsync(cancellationToken))
                .ToList();

            while(!cancellationToken.IsCancellationRequested && tasks.Count > 0)
            {
                var task = await Task.WhenAny(tasks.ToImmutableArray());
                if (task.IsCanceled)
                {
                    tasks.Remove(task);
                    continue;
                }

                try
                {
                    var index = tasks.IndexOf(task);
                    var acceptor = acceptors[index];
                    if (task.IsFaulted)
                    {
                        _logger.LogError("Cannot accept connection. Exception {exception}", task.Exception);
                    }
                    else
                    {
                        var channel = task.Result;
                        var session = new Session(new QueueChannel(channel), this);
                        channel.BoundObject = session;

                        lock (_sessions)
                            _sessions.Add(session);

                        OnConnected(session);

                        channel.Closed += (s, e) => OnDisconnectd(session);
                        channel.Faulted += (s, e) => OnDisconnectd(session);
                    }
    
                    tasks[index] = acceptors[index].AcceptAsync(cancellationToken);
                }
                catch(TaskCanceledException)
                {
                    return;
                }
                catch(Exception ex)
                {
                    _logger.LogError("Connection failed. Exception {exception}", ex);
                }
            }            
        }
        finally
        {
            foreach(var a in acceptors)
                await a.DisposeAsync();

            Session[] sessions;

            lock(_sessions)
            {
                sessions = _sessions.ToArray();
                _sessions.Clear();

                _isRunning = false;
            }

            foreach(var s in sessions)
            {
                await s.DisposeAsync();
            }
        }
    }

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
            _logger.LogInformation("Registered service '{type}', shared within {share}", typeof(T), share);
        }
    }

    private volatile bool _publishServicesCatalog = false;
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

    public event EventHandler<ChannelEventArgs>? Connected;
    public event EventHandler<ChannelEventArgs>? Disconnected;

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

            types = _services.Keys.ToArray();
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
        if (Connected is null) return;
        Connected(this, new ChannelEventArgs(session.Channel));
    }

    private void OnDisconnectd(Session session)
    {
        var channel = session.Channel;

        if (Disconnected is not null)
            Disconnected(this, new ChannelEventArgs(channel));

        channel.BoundObject = null;

        lock(_sessions)
            _sessions.Remove(session);
        
        session.DisposeAsync().AsTask().Wait();

        if (channel is IAsyncDisposable ad)
            ad.DisposeAsync().AsTask().Wait();
        else if (channel is IDisposable d)
            d.Dispose();
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
                    {
                        if (storage.ContainsKey(type))
                            storage.Remove(type);
                    }

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

    private IServiceCatalog CreateServiceCatalog()
    {
        return new ServiceCatalog(_services);
    }
}