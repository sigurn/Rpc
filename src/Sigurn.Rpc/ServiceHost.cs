using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

public class ServiceHost : IServiceHost
{
    private static readonly ILogger<ServiceHost> _logger = RpcLogging.CreateLogger<ServiceHost>();

    private record struct ServiceData(ShareWithin Shared, Func<object> Factory);

    private static readonly Dictionary<Type, RefCounter<ICallTarget>> _globalInstances = new();

    private readonly IChannelHost _host;

    private readonly Dictionary<Type, ServiceData> _services = new();

    private readonly Dictionary<Type, RefCounter<ICallTarget>> _hostInstances = new();

    private readonly List<Session> _sessions = new();

    private readonly Lazy<IServiceCatalog> _serviceCatalog;

    public ServiceHost(IChannelHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _serviceCatalog = new Lazy<IServiceCatalog>(CreateServiceCatalog);
        _host = host;
        _host.Connected += OnConnected;
        _host.Disconnected += OnDisconnected;

    }

    public void Start()
    {
        using var _ = _logger.Scope();
        if (_host.IsOpened) return;
        _host.Open();        
    }

    public void Stop()
    {
        using var _ = _logger.Scope();
        if (!_host.IsOpened) return;
        _host.Close();

        Session[] sessions;
        
        lock (_sessions)
            sessions = _sessions.ToArray();

        var tasks = sessions
            .Select(x => x.Channel.CloseAsync(CancellationToken.None))
            .ToArray();

        Task.WaitAll(tasks);
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

    private void OnConnected(object? sender, ChannelEventArgs args)
    {
        if (sender is null ||
            args.Channel is null ||
            args.Channel.State != ChannelState.Opened) return;

        var session = new Session(new QueueChannel(args.Channel), (IChannelHost)sender, this);
        args.Channel.BoundObject = session;

        lock (_sessions)
            _sessions.Add(session);
    }

    private void OnDisconnected(object? sender, ChannelEventArgs args)
    {
        if (args is null || args.Channel is null || args.Channel.BoundObject is not Session session) return;

        lock (_sessions)
            _sessions.Remove(session);

        args.Channel.BoundObject = null;

        session.Dispose();
    }

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