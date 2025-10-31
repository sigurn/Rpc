using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

public class InterfaceProxy : IDisposable
{
    private static readonly List<Action<Guid>> _handlers = new();

    internal static void NotifyAboutInstanceDestruction(Guid instanceId)
    {
        ThreadPool.QueueUserWorkItem(x =>
        {
            Action<Guid>[] handlers;
            lock (_handlers)
                handlers = _handlers.ToArray();

            foreach (var handler in handlers)
            {
                try
                {
                    handler(x);
                }
                catch
                {
                    continue;
                }
            }
        }, instanceId, true);
    }

    internal static event Action<Guid> InstanceDestroyed
    {
        add
        {
            lock (_handlers)
                _handlers.Add(value);
        }

        remove
        {
            lock (_handlers)
                _handlers.Remove(value);
        }
    }

    private static Dictionary<Type, Func<Guid, object>> _factories = new();

    static InterfaceProxy()
    {
        RegisterProxy<IServiceCatalog>(x => new ServiceCatalogProxy(x));
    }

    internal static T CreateProxy<T>(Guid instanceId, RefCounter<ICallTarget> callTarget, SerializationContext context)
    {
        if (!typeof(T).IsInterface)
            throw new NotSupportedException($"Proxies can be created for interfaces only. Provided type {typeof(T)} is not interface.");

        Func<Guid, object>? factory;
        lock (_factories)
            if (!_factories.TryGetValue(typeof(T), out factory))
                throw new Exception($"There is no proxy for type {typeof(T)}");

        var proxy = factory(instanceId);
        if (proxy is InterfaceProxy ip)
        {
            ip.Context = context;
            ip.AttachCallTarget(callTarget);
        }

        return (T)proxy;
    }

    internal static InterfaceProxy CreateProxy(Guid instanceId, Type type, RefCounter<ICallTarget> callTarget, SerializationContext context)
    {
        if (!type.IsInterface)
            throw new NotSupportedException($"Proxies can be created for interfaces only. Provided type '{type}' is not an interface.");

        Func<Guid, object>? factory;
        lock (_factories)
            if (!_factories.TryGetValue(type, out factory))
                throw new Exception($"There is no proxy for type '{type}'");

        var proxy = (InterfaceProxy)factory(instanceId);
        proxy.Context = context;
        proxy.AttachCallTarget(callTarget);

        return proxy;
    }

    public static void RegisterProxy<T>(Func<Guid, T> factory)
    {
        if (!typeof(T).IsInterface)
            throw new NotSupportedException($"Proxies can be registered for interfaces only. Provided type '{typeof(T)}' is not an interface.");

        ArgumentNullException.ThrowIfNull(factory);

        lock (_factories)
        {
            if (_factories.ContainsKey(typeof(T)))
                throw new ArgumentException($"Proxy for the type '{typeof(T)}' is already registered");

            _factories.Add(typeof(T), x => factory(x) ?? throw new InvalidOperationException("Factory returned null as the proxy instance."));
        }
    }

    public static bool IsThereProxyFor<T>()
    {
        return IsThereProxyFor(typeof(T));
    }

    public static bool IsThereProxyFor(Type interfaceType)
    {
        lock (_factories)
            return _factories.ContainsKey(interfaceType);
    }

    public static bool IsInterfaceProxy<T>(T obj)
    {
        return obj is InterfaceProxy;
    }

    public static IChannel? GetChannel<T>(T obj)
    {
        var ip = obj as InterfaceProxy;
        if (ip is null) return null;
        if (ip.CallTarget is ServiceInstance si) return si.Handler.Channel;
        return null;
    }

    private readonly Guid _instanceId;
    private readonly Dictionary<int, int> _events = new();

    private RefCounter<ICallTarget>? _callTarget;
    private EventHandler<EventDataArgs>? _eventHandler;
    private SerializationContext _context = RpcPacket.DefaultSerializationContext;

    private volatile int _isDisposed = 0;

    protected InterfaceProxy(Guid instanceId)
    {
        _instanceId = instanceId;
    }

    ~InterfaceProxy()
    {
        NotifyAboutInstanceDestruction(_instanceId);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        GC.SuppressFinalize(this);

        CallTarget.EventTriggered -= _eventHandler;
        _callTarget?.Release();
        _callTarget = null;
    }

    public Guid InstanceId => _instanceId;

    private void AttachCallTarget(RefCounter<ICallTarget> callTarget)
    {
        ArgumentNullException.ThrowIfNull(callTarget);
        if (_callTarget is not null)
            throw new InvalidOperationException("Call target is already attached to the proxy");

        _callTarget = callTarget;
        _callTarget.AddRef();

        WeakReference<InterfaceProxy> proxy = new(this);

        _eventHandler = (s, e) =>
        {
            if (proxy.TryGetTarget(out InterfaceProxy? ip))
                ip.EventHandler(s, e);
        };

        CallTarget.EventTriggered += _eventHandler;
    }

    protected SerializationContext Context
    {
        get => _context;
        private set => _context = value;
    }

    private ICallTarget CallTarget => _callTarget?.Value ?? throw new InvalidOperationException("Call target is not attached to the proxy");

    protected (byte[]? Result, IReadOnlyList<byte[]>? Args) InvokeMethod(int methodId, IReadOnlyList<byte[]>? args, bool oneWay)
    {
        CheckDisposed();

        try
        {
            return InvokeMethodAsync(methodId, args, oneWay, CancellationToken.None).Result;
        }
        catch (AggregateException ex)
        {
            if (ex.InnerExceptions.Count == 1)
                throw ex.InnerExceptions[0];

            throw;
        }
    }

    protected Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        CheckDisposed();

        return CallTarget.InvokeMethodAsync(methodId, args, oneWay, cancellationToken);
    }

    protected T? GetProperty<T>(int propertyId)
    {
        CheckDisposed();

        try
        {
            return GetPropertyAsync<T>(propertyId, CancellationToken.None).Result;
        }
        catch (AggregateException ex)
        {
            if (ex.InnerExceptions.Count == 1)
                throw ex.InnerExceptions[0];

            throw;
        }
    }

    protected async Task<T?> GetPropertyAsync<T>(int propertyId, CancellationToken cancellationToken)
    {
        CheckDisposed();

        var value = await CallTarget.GetPropertyValueAsync(propertyId, cancellationToken);
        if (value is null)
            return default;

        return FromBytes<T>(value);
    }

    protected void SetProperty<T>(int propertyId, T value)
    {
        CheckDisposed();

        try
        {
            SetPropertyAsync<T>(propertyId, value, CancellationToken.None).Wait();
        }
        catch (AggregateException ex)
        {
            if (ex.InnerExceptions.Count == 1)
                throw ex.InnerExceptions[0];

            throw;
        }
    }

    protected async Task SetPropertyAsync<T>(int propertyId, T value, CancellationToken cancellationToken)
    {
        CheckDisposed();

        await CallTarget.SetPropertyValueAsync(propertyId, ToBytes<T>(value), cancellationToken);
    }

    protected void AttachEventHandler(int eventId)
    {
        CheckDisposed();

        AttachEventHandlerAsync(eventId, CancellationToken.None).Wait();
    }

    protected async Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        CheckDisposed();

        lock (_events)
        {
            if (_events.ContainsKey(eventId))
            {
                _events[eventId]++;
                return;
            }
            _events.Add(eventId, 1);
        }

        await CallTarget.AttachEventHandlerAsync(eventId, cancellationToken);
    }

    protected void DetachEventHandler(int eventId)
    {
        CheckDisposed();

        DetachEventHandlerAsync(eventId, CancellationToken.None).Wait();
    }

    protected async Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        CheckDisposed();

        lock (_events)
        {
            if (!_events.ContainsKey(eventId)) return;

            var count = --_events[eventId];
            if (count != 0) return;

            _events.Remove(eventId);
        }

        await CallTarget.DetachEventHandlerAsync(eventId, cancellationToken);
    }

    private void CheckDisposed()
    {
        if (Interlocked.Exchange(ref _isDisposed, _isDisposed) != 0)
            throw new InvalidOperationException("The object is already disposed");
    }

    protected Task<byte[]> ToBytesAsync<T>(T value, CancellationToken cancellationToken)
    {
        return RpcPacket.ToBytesAsync(value, Context, cancellationToken);
    }

    protected Task<T?> FromBytesAsync<T>(byte[]? data, CancellationToken cancellationToken)
    {
        return RpcPacket.FromBytesAsync<T>(data, Context, cancellationToken);
    }

    protected byte[] ToBytes<T>(T value)
    {
        return ToBytesAsync<T>(value, CancellationToken.None).Result;
    }

    protected T? FromBytes<T>(byte[]? data)
    {
        return FromBytesAsync<T>(data, CancellationToken.None).Result;
    }

    private void EventHandler(object? sender, EventDataArgs args)
    {
        OnEvent(args.EventId, args.Args);
    }

    protected virtual void OnEvent(int eventId, IReadOnlyList<byte[]> args)
    {

    }

    public override int GetHashCode()
    {
        return _instanceId.GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        if (obj is not InterfaceProxy ip) return false;
        return ip._instanceId == _instanceId;
    }

    public override string ToString()
    {
        return $"InterfaceProxy [Instance={_instanceId}]";
    }
}