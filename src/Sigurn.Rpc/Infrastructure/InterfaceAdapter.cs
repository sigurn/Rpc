using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

public abstract class InterfaceAdapter : ICallTarget, IDisposable, IAsyncDisposable, ISessionsAware
{
    private static readonly ILogger<InterfaceAdapter> _logger = RpcLogging.CreateLogger<InterfaceAdapter>();

    // Guards _factories. Registration and lookup must take the SAME lock: a lookup running while a
    // registration resizes the dictionary can otherwise report a registered interface as missing,
    // which surfaces much later as "Cannot find serializer for type ..." during marshaling.
    private static readonly Dictionary<Type, Func<object, InterfaceAdapter>> _factories = new();

    // Upper bound for bridging an asynchronous disposal onto a synchronous teardown path.
    private static readonly TimeSpan _syncDisposeTimeout = TimeSpan.FromSeconds(5);

    static InterfaceAdapter()
    {
        RegisterAdapter<IServiceCatalog>(x => new ServiceCatalogAdapter(x));
        RegisterAdapter<IRemoteStream>(x => new RemoteStreamAdapter(x));
        RegisterAdapter<IAsyncDisposable>(x => new AsyncDisposableAdapter(x));
    }

    public static void RegisterAdapter<T>(Func<T, InterfaceAdapter> factory)
    {
        if (!typeof(T).IsInterface)
            throw new NotSupportedException($"Adapters can be created for interfaces only. Provided type {typeof(T)} is not interface.");

        ArgumentNullException.ThrowIfNull(factory);

        lock (_factories)
        {
            if (_factories.ContainsKey(typeof(T)))
                throw new ArgumentException($"Adapter for the type {typeof(T)} is already registered");
            _factories.Add(typeof(T), x => factory((T)x));
        }
        
        InterfaceTypeRegistry.RegisterType<T>();
    }
    
    public static bool IsThereAdapterFor<T>()
    {
        return IsThereAdapterFor(typeof(T));
    }

    public static bool IsThereAdapterFor(Type interfaceType)
    {
        lock (_factories)
            return _factories.ContainsKey(interfaceType);
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
        return CreateAdapter(type, instance, context, ownsInstance: true);
    }

    internal static ICallTarget CreateAdapter(Type type, object instance, SerializationContext context, bool ownsInstance)
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
        adapter._ownsInstance = ownsInstance;

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
    private bool _ownsInstance = true;
    private IDisposable? _disposable = null;
    private SerializationContext _context = RpcPacket.DefaultSerializationContext;
    private readonly object _instance;

    // Event ids each session subscribed to through this adapter. A shared (Host/Process)
    // adapter outlives the sessions that use it, so it owns undoing their event handler
    // subscriptions when a session detaches — otherwise the source-event handler lingers
    // and accumulates across reconnects.
    private readonly object _eventSubscriptionsLock = new();
    private readonly Dictionary<Guid, HashSet<int>> _eventSubscriptions = new();

    protected InterfaceAdapter(Type interfaceType, object instance)
    {
        InterfaceType = interfaceType;
        _instance = instance;
    }

    void IDisposable.Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        Dispose(true);

        // The adapter owns the wrapped instance: disposing the adapter disposes
        // the service. This is the single owner for every sharing scope (None,
        // Session, Host, Process) and for sub-services returned from calls, so
        // session-scoped services release their resources on session teardown.
        // The exception is ShareWithin.ProcessNoDispose, where the instance is an
        // externally-owned singleton the host must not dispose.
        if (_ownsInstance)
            DisposeInstanceSync();

        _disposable?.Dispose();
    }

    // Asynchronous counterpart of Dispose. It shares the _isDisposed guard with the synchronous path,
    // so whichever runs first wins and the wrapped instance is disposed exactly once — never through
    // both IDisposable.Dispose and IAsyncDisposable.DisposeAsync.
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        Dispose(true);

        if (_ownsInstance)
        {
            if (_instance is IAsyncDisposable ad)
                await ad.DisposeAsync().ConfigureAwait(false);
            else
                (_instance as IDisposable)?.Dispose();
        }

        _disposable?.Dispose();
    }

    // An instance that offers asynchronous disposal is always disposed through it, so a service never
    // sees both of its disposal methods called. On the synchronous path (session teardown, a proxy
    // finalizer) that call has to be bridged: it runs off the caller's thread, so no captured
    // SynchronizationContext can deadlock it, and the wait is bounded so a misbehaving service cannot
    // stall teardown indefinitely.
    private void DisposeInstanceSync()
    {
        if (_instance is IAsyncDisposable ad)
        {
            try
            {
                if (!Task.Run(() => ad.DisposeAsync().AsTask()).Wait(_syncDisposeTimeout) && _logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Asynchronous disposal of {InstanceType} did not complete within {Timeout}",
                        _instance.GetType().FullName, _syncDisposeTimeout);
            }
            catch (Exception ex)
            {
                // Best effort: a faulting service must not abort teardown.
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug(ex, "Asynchronous disposal of {InstanceType} failed", _instance.GetType().FullName);
            }

            return;
        }

        (_instance as IDisposable)?.Dispose();
    }

    public Type InterfaceType { get; }

    /// <summary>Runtime type of the wrapped service instance.</summary>
    internal Type InstanceType => _instance.GetType();

    /// <summary>
    /// Full name of the remote interface, used in trace messages. Generated adapters override it
    /// with a compile time constant.
    /// </summary>
    protected virtual string RpcInterfaceName => InterfaceType.FullName ?? InterfaceType.Name;

    /// <summary>
    /// True when trace logging is on. Call sites check it before building trace arguments so that
    /// nothing is computed when the level is off.
    /// </summary>
    protected static bool IsTraceEnabled => _logger.IsEnabled(LogLevel.Trace);

    /// <summary>
    /// Maps an event id to its name, for log lines raised outside a dispatch (an event fanned out to
    /// the subscribed sessions). Generated adapters override it; the base class has no way to know
    /// the names and returns <c>null</c>.
    /// </summary>
    protected internal virtual string? GetEventName(int eventId) => null;

    /// <summary>Traces the start of a dispatched member access.</summary>
    protected void TraceEnter(RpcTraceOperation operation, string member, int memberId)
    {
        if (!_logger.IsEnabled(LogLevel.Trace)) return;

        _logger.LogTrace("==> {Operation} {Interface}.{Member} [id={MemberId}] instance={InstanceId}",
            operation, RpcInterfaceName, member, memberId, RpcDispatchContext.InstanceId);
    }

    /// <summary>Traces the end of a dispatched member access.</summary>
    protected void TraceExit(RpcTraceOperation operation, string member, int memberId)
    {
        if (!_logger.IsEnabled(LogLevel.Trace)) return;

        _logger.LogTrace("<== {Operation} {Interface}.{Member} [id={MemberId}] instance={InstanceId}",
            operation, RpcInterfaceName, member, memberId, RpcDispatchContext.InstanceId);
    }

    /// <summary>
    /// Traces a failed member dispatch and always returns false, so it can be used as an exception
    /// filter (<c>catch (Exception ex) when (TraceFailure(ex, ...))</c>). The filter runs during the
    /// first pass, which logs the failure without catching it or unwinding the stack.
    /// </summary>
    protected bool TraceFailure(Exception exception, RpcTraceOperation operation, string member, int memberId)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
            _logger.LogTrace(exception, "<== {Operation} {Interface}.{Member} [id={MemberId}] instance={InstanceId} FAILED",
                operation, RpcInterfaceName, member, memberId, RpcDispatchContext.InstanceId);

        return false;
    }

    protected SerializationContext Context
    {
        get => _context;

        // Never allow a null context: Sigurn.Serialize silently substitutes the default context for a
        // missing one, and the default context cannot marshal interfaces or streams — a failure that
        // only shows up much later, as an unrelated-looking serialization error.
        private set => _context = value ?? throw new ArgumentNullException(nameof(value));
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

    // The session-facing entry points (callers hold the adapter as ICallTarget). They wrap
    // the virtual handlers to keep per-session subscription bookkeeping so DetachSession can
    // undo them — the generated/overridden handlers stay untouched.
    async Task ICallTarget.AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        var session = Session.Current;

        await AttachEventHandlerAsync(eventId, cancellationToken).ConfigureAwait(false);

        if (session is not null)
        {
            lock (_eventSubscriptionsLock)
            {
                if (!_eventSubscriptions.TryGetValue(session.Id, out var events))
                {
                    events = [];
                    _eventSubscriptions.Add(session.Id, events);
                }
                events.Add(eventId);
            }
        }
    }

    async Task ICallTarget.DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        var session = Session.Current;

        await DetachEventHandlerAsync(eventId, cancellationToken).ConfigureAwait(false);

        if (session is not null)
        {
            lock (_eventSubscriptionsLock)
            {
                if (_eventSubscriptions.TryGetValue(session.Id, out var events))
                    events.Remove(eventId);
            }
        }
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
        return await RpcPacket.FromBytesAsync<T>(data, _context, cancellationToken).ConfigureAwait(false);
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

    protected static void CheckAuthenticated()
    {
        var session = Session.Current;
        if (session is not null && session.IsAuthenticated) return;
        throw new AccessDeniedException("Access is denied for unauthenticated clients");
    }

    protected static void CheckPermissions(ImmutableHashSet<Enum> permissions)
    {
        var session = Session.Current ?? 
            throw new AccessDeniedException("Access is denied because session is undefined");

        if (permissions.Any(x => !session.Permissions.Contains(x)))
            throw new AccessDeniedException("Access is denied due to insufficient client permissions");
    }

    void ISessionsAware.AttachSession(ISession session)
    {
        (_instance as ISessionsAware)?.AttachSession(session);
    }

    void ISessionsAware.DetachSession(ISession session)
    {
        HashSet<int>? events;
        lock (_eventSubscriptionsLock)
            _eventSubscriptions.Remove(session.Id, out events);

        // Undo the event subscriptions this session made — important for a shared adapter
        // that keeps living after the session is gone.
        if (events is not null)
        {
            foreach (var eventId in events)
            {
                try
                {
                    DetachEventHandlerAsync(eventId, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // Best effort: a faulting handler must not abort teardown.
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug(ex, "Detaching event {EventId} failed during session teardown", eventId);
                }
            }
        }

        (_instance as ISessionsAware)?.DetachSession(session);
    }
}