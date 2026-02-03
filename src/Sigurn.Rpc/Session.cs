using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc;

sealed class Session : ISession, IDisposable
{
    private static readonly AsyncLocal<ISession?> _session = new AsyncLocal<ISession?>();

    public static ISession? Current => _session.Value;

    internal static IDisposable SetSessionScope(ISession session)
    {
        _session.Value = session;
        return Disposable.Create(() => _session.Value = null);
    }

    private readonly IChannel _channel;
    private readonly IChannelHost? _host;
    private readonly RpcHandler _handler;

    private readonly Dictionary<Type, RefCounter<ICallTarget>> _sessionInstances = new();
    private readonly Dictionary<object, RefCounter<ICallTarget>> _instances = new();

    private readonly Dictionary<Guid, RefCounter<ICallTarget>> _adapters = new();
    private readonly Dictionary<Guid, RefCounter<ICallTarget>> _proxies = new();

    private readonly IServiceHost? _serviceHost = null;
    private readonly SerializationContext _context;

    private readonly Dictionary<Enum, (object? Value, object? Password)> _properties = new();

    private volatile int _isDisposed = 0;

    internal Session(IChannel channel)
    {
        _channel = channel;
        _host = null;
        _handler = new RpcHandler(channel, OnRequest);
        _context = new RpcSerializationContext(this);

        InterfaceProxy.InstanceDestroyed += OnProxyDestroyed;
    }

    internal Session(IChannel channel, IChannelHost host)
    {
        _channel = channel;
        _host = host;
        _handler = new RpcHandler(channel, OnRequest);
        _context = new RpcSerializationContext(this);
    }

    internal Session(IChannel channel, IChannelHost host, IServiceHost serviceHost)
    {
        _channel = channel;
        _host = host;
        _serviceHost = serviceHost;
        _handler = new RpcHandler(channel, OnRequest);
        _context = new RpcSerializationContext(this);
    }

    public Guid Id { get; } = Guid.NewGuid();

    public IChannel Channel
    {
        get
        {
            CheckDisposed();
            return _channel;
        }
    }

    public IChannelHost? ChannelHost
    {
        get
        {
            CheckDisposed();
            return _host;
        }
    }

    public SerializationContext SerializationContext
    {
        get
        {
            CheckDisposed();
            return _context;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        RefCounter<ICallTarget>[] instances;

        lock (_proxies)
        {
            instances = _proxies.Values.ToArray();
            _proxies.Clear();
        }

        foreach (var instance in instances)
            instance.Dispose();

        lock (_adapters)
        {
            instances = _adapters.Values.ToArray();
            _adapters.Clear();
        }

        foreach (var instance in instances)
            instance.Dispose();

        lock (_sessionInstances)
        {
            instances = _sessionInstances.Values.ToArray();
            _sessionInstances.Clear();
        }

        foreach (var instance in instances)
            instance.Dispose();
    }

    public object? GetProperty(Enum key)
    {
        lock(_properties)
            return _properties[key];
    }

    public bool TryGetProperty(Enum key, out object? value)
    {
        lock (_properties)
        {
            if (_properties.TryGetValue(key, out var valueBucket))
            {
                value = valueBucket.Value;
                return true;
            }

            value = default;
            return false;
        }
    }

    public void SetProperty(Enum key, object? value)
    {
        lock (_properties)
        {
            if (_properties.TryGetValue(key, out var valueBucket) && valueBucket.Password is not null)
                throw new InvalidOperationException("Invalid password. Cannot change value");

            _properties[key] = (Value:value, Password: null);
        }
    }

    public void SetProperty(Enum key, object? value, object password)
    {
        lock (_properties)
        {
            if (_properties.TryGetValue(key, out var valueBucket) && valueBucket.Password is not null && !valueBucket.Password.Equals(password))
                throw new InvalidOperationException("Invalid password. Cannot change value.");

            _properties[key] = (Value:value, Password: password);
        }
    }

    public bool ContainsProperty(Enum key)
    {
        lock(_properties)
            return _properties.ContainsKey(key);
    }

    public bool RemoveProperty(Enum key)
    {
        lock (_properties)
        {
            if (!_properties.TryGetValue(key, out var valueBucket)) return false;

            if (valueBucket.Password is not null)
                throw new InvalidOperationException("Invalid password. Cannot change value.");

            _properties.Remove(key);
            return true;
        }
    }

    public bool RemoveProperty(Enum key, object password)
    {
        lock (_properties)
        {
            if (!_properties.TryGetValue(key, out var valueBucket)) return false;

            if (valueBucket.Password is not null && !valueBucket.Password.Equals(password))
                throw new InvalidOperationException("Invalid password. Cannot change value.");

            _properties.Remove(key);
            return true;
        }
    }

    public bool IsPropertyPasswordProtected(Enum key)
    {
        lock (_properties)
        {
            if (!_properties.TryGetValue(key, out var valueBucket)) return false;
            return valueBucket.Password is not null;
        }
    }

    internal RpcHandler Rpc => _handler;

    internal async Task<T> CreateProxy<T>(CancellationToken cancellationToken)
    {
        var instanceId = await _handler.GetServiceInstanceAsync(typeof(T).GUID, cancellationToken);
        return (T)GetProxy(typeof(T), instanceId);
    }

    private void CheckDisposed()
    {
        if (_isDisposed != 0)
            throw new ObjectDisposedException($"Session {Id}");
    }

    private RefCounter<ICallTarget> CreateSessionInstance(Type type, Func<object> factory)
    {
        lock (_sessionInstances)
        {
            if (!_sessionInstances.TryGetValue(type, out var instanceRef))
            {
                instanceRef = new RefCounter<ICallTarget>
                (
                    CreateAdapter(type, factory()),
                    x =>
                    {
                        lock (_sessionInstances)
                            _sessionInstances.Remove(type);
                        if (x is IDisposable d) d.Dispose();
                    }
                );
                _sessionInstances.Add(type, instanceRef);
            }

            return instanceRef;
        }
    }

    private Guid RegisterInstance(RefCounter<ICallTarget> instance)
    {
        var id = Guid.NewGuid();

        lock (_adapters)
        {
            foreach (var kvp in _adapters)
                if (kvp.Value == instance) return kvp.Key;

            _adapters.Add(id, instance);
            instance.AddRef();
            instance.Value.EventTriggered += (s, e) =>
            {
                var ec = EventContext.Current;

                if (ec is not null)
                {
                    if (ec.Include is not null && !ec.Include.Contains(this)) return;
                    if (ec.Exclude is not null && ec.Exclude.Contains(this)) return;
                }

                var packet = new EventDataPacket(id, e.EventId, e.Args);
                _handler.SendAsync(packet, CancellationToken.None).Wait();
            };
        }

        if (instance.Value is ISessionsAware sas)
            sas.AttachSession(this);

        return id;
    }

    internal Guid RegisterInstance(Type type, object instance)
    {
        RefCounter<ICallTarget>? refCounter;
        lock (_instances)
        {
            if (!_instances.TryGetValue(instance, out refCounter))
            {
                refCounter = new RefCounter<ICallTarget>
                (
                    CreateAdapter(type, instance),
                    x =>
                    {
                        lock (_instances)
                            _instances.Remove(instance);

                        (x as IDisposable)?.Dispose();
                    }
                );
                _instances.Add(instance, refCounter);
            }
        }

        return RegisterInstance(refCounter);
    }

    private void ReleaseInstance(Guid instanceId)
    {
        RefCounter<ICallTarget>? instance = null;
        lock (_adapters)
        {
            if (!_adapters.TryGetValue(instanceId, out instance)) return;
            {
                _adapters.Remove(instanceId);
            }
        }

        if (instance.Value is ISessionsAware sas)
            sas.DetachSession(this);

        instance.Release();
    }

    internal object GetProxy(Type type, Guid instanceId)
    {
        lock (_proxies)
        {
            RefCounter<ICallTarget>? proxyRef;

            if (!_proxies.TryGetValue(instanceId, out proxyRef))
            {
                proxyRef = new RefCounter<ICallTarget>(new ServiceInstance(instanceId, _handler), x =>
                {
                    lock (_proxies)
                        _proxies.Remove(instanceId);
                    if (x is IDisposable d) d.Dispose();
                });
                _proxies.Add(instanceId, proxyRef);
            }

            return InterfaceProxy.CreateProxy(instanceId, type, proxyRef, SerializationContext);
        }
    }

    private ICallTarget? GetAdapter(Guid instanceId)
    {
        lock (_adapters)
        {
            if (_adapters.TryGetValue(instanceId, out var instance))
                return instance.Value;
        }

        return null;
    }

    private ICallTarget CreateAdapter(Type type, object instance)
    {
        return InterfaceAdapter.CreateAdapter(type, instance, SerializationContext);
    }

    private RefCounter<ICallTarget> GetServiceInstance(Guid interfaceId)
    {
        var type = _serviceHost?.FindTypeById(interfaceId);

        if (_serviceHost is null || type is null)
            throw new Exception("Requested service is not available");

        var (shared, factory) = _serviceHost.GetServiceInfo(type);

        RefCounter<ICallTarget> instance;

        var callTargetFactory = () =>
        {
            var callTarget = InterfaceAdapter.CreateAdapter(type, factory(), SerializationContext);
            return new RefCounter<ICallTarget>(callTarget, x => (x as IDisposable)?.Dispose());
        };

        switch (shared)
        {
            case ShareWithin.None:
                instance = new RefCounter<ICallTarget>
                (
                    CreateAdapter(type, factory()),
                    x => (x as IDisposable)?.Dispose()
                );
                break;

            case ShareWithin.Session:
                instance = CreateSessionInstance(type, factory);
                break;

            case ShareWithin.Host:
                instance = _serviceHost.CreateHostInstance(type, () => InterfaceAdapter.CreateAdapter(type, factory(), SerializationContext));
                break;

            case ShareWithin.Process:
                instance = _serviceHost.CreateGlobalInstance(type, () => InterfaceAdapter.CreateAdapter(type, factory(), SerializationContext));
                break;

            default:
                throw new Exception($"Unsupported sharing type for the service '{type}'");
        }

        return instance;
    }

    private async Task<RpcPacket?> OnRequest(RpcPacket request, CancellationToken cancellationToken)
    {
        try
        {
            using (SetSessionScope(this))
            {
                if (request is GetInstancePacket gip)
                {
                    var instance = GetServiceInstance(gip.InterfaceId);

                    return new ServiceInstancePacket(request)
                    {
                        InstanceId = RegisterInstance(instance)
                    };
                }
                else if (request is ReleaseInstancePacket rip)
                {
                    ReleaseInstance(rip.InstanceId);
                    return new SuccessPacket(request);
                }
                else if (request is MethodCallPacket mcp)
                {
                    var instance = GetAdapter(mcp.InstanceId);
                    if (instance is null)
                        throw new Exception("Unknown instance");

                    var result = await instance.InvokeMethodAsync(mcp.MethodId, mcp.Args, mcp.OneWay, cancellationToken);
                    return new MethodResultPacket(mcp)
                    {
                        Result = result.Result,
                        Args = result.Args
                    };
                }
                else if (request is GetPropertyPacket gpp)
                {
                    var instance = GetAdapter(gpp.InstanceId);
                    if (instance is null)
                        throw new Exception("Unknown instance");

                    var value = await instance.GetPropertyValueAsync(gpp.PropertyId, cancellationToken);
                    return new PropertyValuePacket(gpp)
                    {
                        Value = value
                    };
                }
                else if (request is SetPropertyPacket spp)
                {
                    var instance = GetAdapter(spp.InstanceId);
                    if (instance is null)
                        throw new Exception("Unknown instance");

                    await instance.SetPropertyValueAsync(spp.PropertyId, spp.Value, cancellationToken);
                    return new SuccessPacket(spp);
                }
                else if (request is SubscribeForEventPacket sfep)
                {
                    var instance = GetAdapter(sfep.InstanceId);
                    if (instance is null)
                        throw new Exception("Unknown instance");

                    await instance.AttachEventHandlerAsync(sfep.EventId, cancellationToken);
                    return new SuccessPacket(sfep);
                }
                else if (request is UnsubscribeFromEventPacket ufep)
                {
                    var instance = GetAdapter(ufep.InstanceId);
                    if (instance is null)
                        throw new Exception("Unknown instance");

                    await instance.DetachEventHandlerAsync(ufep.EventId, cancellationToken);
                    return new SuccessPacket(ufep);
                }

                throw new Exception("Unknown packet");
            }
        }
        catch (Exception ex)
        {
            if (request is null) return null;
            if (request is MethodCallPacket mcp && mcp.OneWay) return null;
            return new ExceptionPacket(request, ex);
        }
    }

    private void OnProxyDestroyed(Guid instanceId)
    {
        lock (_proxies)
        {
            if (_proxies.TryGetValue(instanceId, out var refCounter))
                refCounter.Release();
        }
    }
}
