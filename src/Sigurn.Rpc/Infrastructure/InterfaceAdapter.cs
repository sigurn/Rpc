using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

public abstract class InterfaceAdapter : ICallTarget, IDisposable, ISessionsAware
{
    private static readonly object _lock = new();
    private static Dictionary<Type, Func<object, InterfaceAdapter>> _factories = new();

    public static void RegisterAdapter<T>(Func<T, InterfaceAdapter> factory)
    {
        if (!typeof(T).IsInterface)
            throw new NotSupportedException($"Adapters can be created for interfaces only. Provided type {typeof(T)} is not interface.");

        ArgumentNullException.ThrowIfNull(factory);

        lock (_lock)
        {
            if (_factories.ContainsKey(typeof(T)))
                throw new ArgumentException($"Adapter for the type {typeof(T)} is already registered");
            _factories.Add(typeof(T), x => factory((T)x));
        }
    }

    internal static ICallTarget CreateAdapter<T>(T instance, SerializationContext context)
    {
        if (!typeof(T).IsInterface)
            throw new NotSupportedException($"Adapters can be created for interfaces only. Provided type {typeof(T)} is not interface.");

        ArgumentNullException.ThrowIfNull(instance);

        Func<object, InterfaceAdapter>? factory;
        lock (_factories)
            if (!_factories.TryGetValue(typeof(T), out factory))
                throw new Exception($"There is no adapter for type {typeof(T)}");

        var adapter = factory(instance);
        adapter.Context = context;

        return adapter;
    }

    internal static ICallTarget CreateAdapter(Type type, object instance, SerializationContext context)
    {
        if (!type.IsInterface)
            throw new NotSupportedException($"Adapters can be created for interfaces only. Provided type {type} is not interface.");

        ArgumentNullException.ThrowIfNull(instance);

        Func<object, InterfaceAdapter>? factory;
        lock (_factories)
            if (!_factories.TryGetValue(type, out factory))
                throw new Exception($"There is no adapter for type {type}");

        var adapter = factory(instance);
        adapter.Context = context;

        return adapter;
    }

    internal static ICallTarget CreateAdapter<T>(T instance, SerializationContext context, IDisposable disposable)
    {
        if (!typeof(T).IsInterface)
            throw new NotSupportedException($"Adapters can be created for interfaces only. Provided type {typeof(T)} is not interface.");

        ArgumentNullException.ThrowIfNull(instance);

        Func<object, InterfaceAdapter>? factory;
        lock (_factories)
            if (!_factories.TryGetValue(typeof(T), out factory))
                throw new Exception($"There is no adapter for type {typeof(T)}");

        var adapter = factory(instance);
        adapter.Context = context;
        adapter.Disposable = disposable;

        return adapter;
    }

    private volatile int _isDisposed = 0;
    private IDisposable? _disposable = null;
    private SerializationContext _context = RpcPacket.DefaultSerializationContext;
    private readonly object _instance;

    protected InterfaceAdapter(Type interfaceType, object instance)
    {
        InterfaceType = interfaceType;
        _instance = instance;
    }

    void IDisposable.Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        Dispose(true);

        _disposable?.Dispose();
    }

    public Type InterfaceType { get; }

    protected SerializationContext Context
    {
        get => _context;
        private set => _context = value;
    }

    private IDisposable? Disposable
    {
        get => _disposable;
        set => _disposable = value;
    }

    public virtual Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public virtual Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public virtual Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public virtual Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public virtual Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public event EventHandler<EventDataArgs>? EventTriggered;

    protected virtual void Dispose(bool disposing)
    {

    }

    protected byte[] ToBytes<T>(T? value)
    {
        return ToBytesAsync<T>(value, CancellationToken.None).Result;
    }

    protected Task<byte[]> ToBytesAsync<T>(T? value, CancellationToken cancellationToken)
    {
        return RpcPacket.ToBytesAsync<T>(value, _context, cancellationToken);
    }

    protected T? FromBytes<T>(byte[]? data)
    {
        return FromBytesAsync<T>(data, CancellationToken.None).Result;
    }

    protected async Task<T?> FromBytesAsync<T>(byte[]? data, CancellationToken cancellationToken)
    {
        return await RpcPacket.FromBytesAsync<T>(data, _context, cancellationToken);
    }

    protected void SendEvent(int eventId, params byte[][] args)
    {
        SendEventAsync(eventId, args, CancellationToken.None).Wait();
    }

    protected Task SendEventAsync(int eventId, IReadOnlyList<byte[]> args, CancellationToken cancellationToken)
    {
        EventTriggered?.Invoke(this, new EventDataArgs(eventId, args));
        return Task.CompletedTask;
    }

    void ISessionsAware.AttachSession(ISession session)
    {
        (_instance as ISessionsAware)?.AttachSession(session);
    }

    void ISessionsAware.DetachSession(ISession session)
    {
        (_instance as ISessionsAware)?.DetachSession(session);
    }
}