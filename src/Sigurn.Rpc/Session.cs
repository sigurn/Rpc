using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc;

sealed class Session : ISession, IDisposable, IAsyncDisposable
{
    private static readonly AsyncLocal<ISession?> _session = new AsyncLocal<ISession?>();

    private static readonly ILogger<Session> _logger = RpcLogging.CreateLogger<Session>();

    public static ISession? Current => _session.Value;

    internal static IDisposable SetSessionScope(ISession session)
    {
        _session.Value = session;
        return Disposable.Create(() => _session.Value = null);
    }

    private readonly IChannel _channel;
    private readonly object? _host;
    private readonly RpcHandler _handler;

    private readonly Dictionary<Type, RefCounter<ICallTarget>> _sessionInstances = [];
    private readonly Dictionary<object, RefCounter<ICallTarget>> _instances = [];

    private readonly Dictionary<Guid, RefCounter<ICallTarget>> _adapters = [];
    private readonly Dictionary<Guid, RefCounter<ICallTarget>> _proxies = [];

    // Root service proxies (obtained via CreateProxy<T>) that can be re-established after a channel
    // reopen, keyed by their original (stable) instance id. Derived proxies are not tracked here and
    // get invalidated on reopen instead.
    private readonly Dictionary<Guid, ServiceInstance> _restorableInstances = [];

    private int _openCount;

    // The EventTriggered lambda this session wired onto each registered adapter, kept so
    // it can be removed on teardown — otherwise a shared (Host/Process) adapter keeps
    // pushing events to this session's closed channel. Guarded by the _adapters lock.
    private readonly Dictionary<Guid, EventHandler<EventDataArgs>> _adapterEventHandlers = [];

    private readonly IServiceHost? _serviceHost = null;
    private readonly SerializationContext _context;

    private readonly Dictionary<Enum, (object? Value, object? Password)> _properties = [];

    private volatile int _isDisposed = 0;

    // The RpcHandler starts receiving as soon as it is created and dispatches requests to OnRequest on
    // other threads, so it is always created LAST: everything the request path needs — above all the
    // serialization context — must be in place first. A request arriving in that window would otherwise
    // serialize with a missing context, which Sigurn.Serialize silently replaces with the default one,
    // and marshaling an interface or a stream fails with "Cannot find serializer for type ...".
    internal Session(IChannel channel)
    {
        _channel = channel;
        _host = null;
        _context = new RpcSerializationContext(this);

        InterfaceProxy.InstanceDestroyed += OnProxyDestroyed;

        _handler = new RpcHandler(channel, OnRequest);
        _logger.LogInformation("Session created: {SessionId}", Id);

        // Client sessions restore their state when the restorable channel reconnects. Restore is driven by
        // RpcClient through the channel's pre-Opened initialize slot (see RestoreAfterReopenAsync) rather
        // than the Opened event, so the connection is announced as Opened only once restore has completed.
    }

    internal Session(IChannel channel, IChannelHost host)
    {
        _channel = channel;
        _host = host;
        _context = new RpcSerializationContext(this);

        _handler = new RpcHandler(channel, OnRequest);
        _logger.LogInformation("Session created: {SessionId}", Id);
    }

    internal Session(IChannel channel, IChannelHost host, IServiceHost serviceHost)
    {
        _channel = channel;
        _host = host;
        _serviceHost = serviceHost;
        _context = new RpcSerializationContext(this);

        _handler = new RpcHandler(channel, OnRequest);
        _logger.LogInformation("Session created: {SessionId}", Id);
    }

    internal Session(IChannel channel, IServiceHost serviceHost)
    {
        _channel = channel;
        _host = null;
        _serviceHost = serviceHost;
        _context = new RpcSerializationContext(this);

        _handler = new RpcHandler(channel, OnRequest);
        _logger.LogInformation("Session created: {SessionId}", Id);
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

    public object? ChannelHost
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        _logger.LogInformation("Session closed: {SessionId}", Id);

        // Client sessions subscribe to this in their constructor; detach so the session (and
        // everything it roots) can be collected. A no-op for server sessions.
        InterfaceProxy.InstanceDestroyed -= OnProxyDestroyed;

        RefCounter<ICallTarget>[] instances;

        lock (_proxies)
        {
            instances = _proxies.Values.ToArray();
            _proxies.Clear();
        }
        lock (_restorableInstances)
            _restorableInstances.Clear();

        await DisposeInstances(instances).ConfigureAwait(false);

        await TeardownExposedAdaptersAsync().ConfigureAwait(false);

        lock (_sessionInstances)
        {
            instances = [.. _sessionInstances.Values];
            _sessionInstances.Clear();
        }

        // Session-scoped services are adapters: dispose them asynchronously so a service that only
        // offers DisposeAsync is released properly when the connection goes away.
        foreach (var instance in instances)
            await instance.DisposeAsync().ConfigureAwait(false);

        if (_channel is IAsyncDisposable ad)
            await ad.DisposeAsync().ConfigureAwait(false);
        else if (_channel is IDisposable d)
            d.Dispose();
    }

    // Releases every adapter this session exposed to the remote peer (callbacks passed to the server).
    // Used both on session teardown and on a channel reopen — after a reopen the old remote session is
    // gone and would never send a ReleaseInstancePacket for them. Whether the wrapped object is
    // disposed is governed by the adapter's ownership flag (see RpcInterface.NoDispose).
    private void TeardownExposedAdapters()
    {
        foreach (var counter in DetachExposedAdapters())
            counter.Release();
    }

    // Asynchronous twin, used when the session is torn down asynchronously (a disconnect, or an
    // explicit DisposeAsync): an exposed instance that offers asynchronous disposal gets its
    // DisposeAsync awaited instead of bridged onto the calling thread.
    private async ValueTask TeardownExposedAdaptersAsync()
    {
        foreach (var counter in DetachExposedAdapters())
            await counter.ReleaseAsync().ConfigureAwait(false);
    }

    private IReadOnlyList<RefCounter<ICallTarget>> DetachExposedAdapters()
    {
        KeyValuePair<Guid, RefCounter<ICallTarget>>[] adapters;
        Dictionary<Guid, EventHandler<EventDataArgs>> handlers;
        lock (_adapters)
        {
            adapters = [.. _adapters];
            _adapters.Clear();
            handlers = new(_adapterEventHandlers);
            _adapterEventHandlers.Clear();
        }

        // Remove this session's event fan-out and notify ISessionsAware services before
        // releasing, while the (possibly shared) adapter is still alive.
        foreach (var (id, counter) in adapters)
            CleanupAdapter(counter, handlers.GetValueOrDefault(id));

        // Adapters were AddRef'd in RegisterInstance, so they are released symmetrically by the
        // caller. A shared (Host/Process) instance lives as one RefCounter across every session, so
        // disposing it directly would destroy it while other sessions still hold references —
        // Release disposes only at the last one.
        return [.. adapters.Select(x => x.Value)];
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        _logger.LogInformation("Session closed: {SessionId}", Id);

        // Client sessions subscribe to this in their constructor; detach so the session (and
        // everything it roots) can be collected. A no-op for server sessions.
        InterfaceProxy.InstanceDestroyed -= OnProxyDestroyed;

        RefCounter<ICallTarget>[] instances;

        lock (_proxies)
        {
            instances = [.. _proxies.Values];
            _proxies.Clear();
        }
        lock (_restorableInstances)
            _restorableInstances.Clear();

        foreach (var instance in instances)
            instance.Dispose();

        TeardownExposedAdapters();

        lock (_sessionInstances)
        {
            instances = [.. _sessionInstances.Values];
            _sessionInstances.Clear();
        }

        foreach (var instance in instances)
            instance.Dispose();

        if (_channel is IDisposable d)
            d.Dispose();
        else if (_channel is IAsyncDisposable ad)
            ad.DisposeAsync().AsTask().Wait();
    }

    // Disposes a call target (an adapter, or a client-side ServiceInstance) preferring asynchronous
    // disposal, so a service that only offers DisposeAsync is released properly and no instance ever
    // sees both of its disposal methods called.
    private static async ValueTask DisposeTargetAsync(ICallTarget target)
    {
        if (target is IAsyncDisposable ad)
            await ad.DisposeAsync().ConfigureAwait(false);
        else
            (target as IDisposable)?.Dispose();
    }

    // Releases the proxies this session holds. Notifying the remote peer is best-effort here — the
    // connection that owned those instances is usually already gone — so this uses the synchronous,
    // fire-and-forget release rather than waiting for the peer to confirm the way an explicit
    // `await proxy.DisposeAsync()` does.
    private static async ValueTask DisposeInstances(IEnumerable<RefCounter<ICallTarget>> instances)
    {
        await Task.WhenAll(instances.Select(i => Task.Run(() => i.Dispose()))).ConfigureAwait(false);
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

    internal Task<T> CreateProxy<T>(CancellationToken cancellationToken)
        => CreateProxy<T>(cancellationToken, restorable: true);

    // restorable:false yields a transient proxy that is not tracked in _restorableInstances, so session
    // restore never re-requests it. Used by the session-initialize hook, which releases such proxies when
    // it completes.
    internal async Task<T> CreateProxy<T>(CancellationToken cancellationToken, bool restorable)
    {
        var instanceId = await _handler.GetServiceInstanceAsync(typeof(T).GUID, cancellationToken).ConfigureAwait(false);
        return (T)GetProxy(typeof(T), instanceId, restorable);
    }

    private void CheckDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, $"Session {Id}");
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
                    },
                    async x =>
                    {
                        lock (_sessionInstances)
                            _sessionInstances.Remove(type);
                        await DisposeTargetAsync(x).ConfigureAwait(false);
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

        EventHandler<EventDataArgs> handler = (s, e) =>
        {
            var ec = EventContext.Current;

            if (ec is not null)
            {
                if (ec.Include is not null && !ec.Include.Contains(this)) return;
                if (ec.Exclude is not null && ec.Exclude.Contains(this)) return;
            }

            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace("Sending event {Interface}.{Member} [id={MemberId}] instance={InstanceId} session={SessionId}",
                    GetInterfaceName(s), GetMemberName(s, RpcTraceOperation.EventRaise, e.EventId), e.EventId, id, Id);

            var packet = new EventDataPacket(id, e.EventId, e.Args);

            // Event delivery is fire-and-forget: a service raising an event must never fault because this
            // session's channel is momentarily down (e.g. between reopens). Drop the event and log — the
            // client replays its subscriptions on reopen, so a lost notification during the gap is expected.
            try
            {
                _handler.SendAsync(packet, CancellationToken.None).Wait();
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug(ex, "Dropped event {Interface}.{Member} [id={MemberId}] instance={InstanceId} session={SessionId}: channel unavailable",
                        GetInterfaceName(s), GetMemberName(s, RpcTraceOperation.EventRaise, e.EventId), e.EventId, id, Id);
            }
        };

        lock (_adapters)
        {
            foreach (var kvp in _adapters)
                if (kvp.Value == instance) return kvp.Key;

            _adapters.Add(id, instance);
            _adapterEventHandlers.Add(id, handler);
            instance.AddRef();
            instance.Value.EventTriggered += handler;
        }

        if (_logger.IsEnabled(LogLevel.Information))
            _logger.LogInformation("Instance registered: {InstanceId} session={SessionId} adapter=#{AdapterId} interface={Interface} type={InstanceType}",
                id, Id, GetAdapterId(instance.Value), GetInterfaceName(instance.Value), GetInstanceTypeName(instance.Value));

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
                // Ownership is decided once, at adapter creation: an instance the caller marked with
                // RpcInterface.NoDispose in the current flow is borrowed (not disposed by the adapter).
                var ownsInstance = !RpcInterface.ConsumeBorrowed(instance);
                refCounter = new RefCounter<ICallTarget>
                (
                    CreateAdapter(type, instance, ownsInstance),
                    x =>
                    {
                        lock (_instances)
                            _instances.Remove(instance);

                        // Disposing the adapter disposes the wrapped instance too
                        // (InterfaceAdapter owns it), so no separate instance dispose here.
                        (x as IDisposable)?.Dispose();
                    },
                    async x =>
                    {
                        lock (_instances)
                            _instances.Remove(instance);

                        await DisposeTargetAsync(x).ConfigureAwait(false);
                    }
                );
                _instances.Add(instance, refCounter);
            }
        }

        return RegisterInstance(refCounter);
    }

    // Undoes what this session wired onto an adapter, before its reference is released.
    // Safe while other sessions still hold a shared adapter: it removes only this
    // session's own EventTriggered lambda. Best-effort — a misbehaving service must not
    // abort teardown.
    private void CleanupAdapter(RefCounter<ICallTarget> counter, EventHandler<EventDataArgs>? handler)
    {
        ICallTarget adapter;
        try
        {
            adapter = counter.Value;
        }
        catch (ObjectDisposedException ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug(ex, "Adapter already disposed during cleanup");
            return;
        }

        // Stop this session's event fan-out so a shared adapter stops pushing to the
        // (closing) channel.
        if (handler is not null)
            adapter.EventTriggered -= handler;

        if (adapter is ISessionsAware sas)
        {
            try
            {
                sas.DetachSession(this);
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug(ex, "DetachSession failed during adapter cleanup");
            }
        }
    }

    // An adapter always knows the interface it exposes and the object behind it; both are what makes
    // an instance id in the log identifiable. A target that is not an adapter (or one already torn
    // down) still gets a usable name instead of aborting the log call.
    private static string GetInterfaceName(object? target)
    {
        if (target is InterfaceAdapter adapter)
            return adapter.InterfaceType.FullName ?? adapter.InterfaceType.Name;

        return target?.GetType().FullName ?? target?.GetType().Name ?? "?";
    }

    private static string GetInstanceTypeName(object? target)
    {
        if (target is InterfaceAdapter adapter)
            return adapter.InstanceType.FullName ?? adapter.InstanceType.Name;

        return target?.GetType().FullName ?? target?.GetType().Name ?? "?";
    }

    // Number of the adapter behind this registration, so the log ties one object to the several
    // instance ids it is exposed under. Null for a target that is not an adapter.
    private static int? GetAdapterId(object? target) => (target as InterfaceAdapter)?.AdapterId;

    // Only a generated adapter knows its member names; anything else falls back to the id.
    private static string GetMemberName(object? target, RpcTraceOperation operation, int memberId)
    {
        return (target as InterfaceAdapter)?.GetMemberName(operation, memberId)
            ?? memberId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // Releasing an exposed instance disposes the wrapped object when the last reference goes, and the
    // caller awaits that: the remote peer's DisposeAsync must not be answered before the local object
    // is actually disposed.
    private async Task ReleaseInstanceAsync(Guid instanceId)
    {
        RefCounter<ICallTarget>? instance;
        EventHandler<EventDataArgs>? handler;
        lock (_adapters)
        {
            if (!_adapters.TryGetValue(instanceId, out instance)) return;

            _adapters.Remove(instanceId);
            _adapterEventHandlers.Remove(instanceId, out handler);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            // Read the names before the reference is released — after that the adapter is gone.
            try
            {
                _logger.LogInformation("Instance released: {InstanceId} session={SessionId} adapter=#{AdapterId} interface={Interface} type={InstanceType}",
                    instanceId, Id, GetAdapterId(instance.Value), GetInterfaceName(instance.Value), GetInstanceTypeName(instance.Value));
            }
            catch (ObjectDisposedException)
            {
                _logger.LogInformation("Instance released: {InstanceId} session={SessionId}", instanceId, Id);
            }
        }

        CleanupAdapter(instance, handler);

        await instance.ReleaseAsync().ConfigureAwait(false);
    }

    internal object GetProxy(Type type, Guid instanceId, bool restorable = false)
    {
        lock (_proxies)
        {
            if (!_proxies.TryGetValue(instanceId, out var proxyRef))
            {
                var serviceInstance = new ServiceInstance(instanceId, _handler, type);
                proxyRef = new RefCounter<ICallTarget>(serviceInstance, x =>
                {
                    RemoveProxy(instanceId);
                    if (x is IDisposable d) d.Dispose();
                },
                async x =>
                {
                    RemoveProxy(instanceId);
                    await DisposeTargetAsync(x).ConfigureAwait(false);
                });
                _proxies.Add(instanceId, proxyRef);

                if (restorable)
                    lock (_restorableInstances)
                        _restorableInstances[instanceId] = serviceInstance;

                if (_logger.IsEnabled(LogLevel.Information))
                    _logger.LogInformation("Proxy created: {InstanceId} session={SessionId} interface={Interface}",
                        instanceId, Id, type.FullName ?? type.Name);
            }

            return InterfaceProxy.CreateProxy(instanceId, type, proxyRef, SerializationContext);
        }
    }

    private void RemoveProxy(Guid instanceId)
    {
        lock (_proxies)
            _proxies.Remove(instanceId);
        lock (_restorableInstances)
            _restorableInstances.Remove(instanceId);
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

    private ICallTarget CreateAdapter(Type type, object instance, bool ownsInstance)
    {
        return InterfaceAdapter.CreateAdapter(type, instance, SerializationContext, ownsInstance);
    }

    private RefCounter<ICallTarget> GetServiceInstance(Guid interfaceId)
    {
        var type = _serviceHost?.FindTypeById(interfaceId);

        if (_serviceHost is null || type is null)
            throw new Exception("Requested service is not available");

        var (shared, factory) = _serviceHost.GetServiceInfo(type);

        RefCounter<ICallTarget> instance;

        switch (shared)
        {
            case ShareWithin.None:
                instance = new RefCounter<ICallTarget>
                (
                    CreateAdapter(type, factory()),
                    x => (x as IDisposable)?.Dispose(),
                    async x => await DisposeTargetAsync(x).ConfigureAwait(false)
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

            case ShareWithin.ProcessNoDispose:
                // Same process-wide sharing as Process, but the adapter does not own the
                // instance, so the externally-managed singleton is never disposed by the host.
                instance = _serviceHost.CreateGlobalInstance(type, () => InterfaceAdapter.CreateAdapter(type, factory(), SerializationContext, ownsInstance: false));
                break;

            default:
                throw new Exception($"Unsupported sharing type for the service '{type}'");
        }

        return instance;
    }

    // What a dispatch trace needs, resolved once on entry and reused on exit.
    private readonly record struct DispatchTrace(RpcTraceOperation Operation, string Interface, string Member, int MemberId, Guid InstanceId);

    // Which member of which instance a request addresses, or null for a packet that is not a
    // member access (instance lifecycle, batch subscribe).
    private static (RpcTraceOperation Operation, int MemberId, Guid InstanceId)? GetTraceTarget(RpcPacket request) => request switch
    {
        MethodCallPacket p => (RpcTraceOperation.MethodCall, p.MethodId, p.InstanceId),
        GetPropertyPacket p => (RpcTraceOperation.PropertyGet, p.PropertyId, p.InstanceId),
        SetPropertyPacket p => (RpcTraceOperation.PropertySet, p.PropertyId, p.InstanceId),
        SubscribeForEventPacket p => (RpcTraceOperation.EventAttach, p.EventId, p.InstanceId),
        UnsubscribeFromEventPacket p => (RpcTraceOperation.EventDetach, p.EventId, p.InstanceId),
        _ => null,
    };

    // The session owns the instance id and the request; the adapter owns the member names. Nothing
    // here runs when trace logging is off.
    private DispatchTrace? BeginTrace(RpcPacket request)
    {
        if (!_logger.IsEnabled(LogLevel.Trace)) return null;
        if (GetTraceTarget(request) is not var (operation, memberId, instanceId)) return null;

        var target = GetAdapter(instanceId);
        var trace = new DispatchTrace(
            operation,
            GetInterfaceName(target),
            GetMemberName(target, operation, memberId),
            memberId,
            instanceId);

        _logger.LogTrace("==> {Operation} {Interface}.{Member} [id={MemberId}] instance={InstanceId} session={SessionId}",
            trace.Operation, trace.Interface, trace.Member, trace.MemberId, trace.InstanceId, Id);

        return trace;
    }

    private void EndTrace(DispatchTrace? trace)
    {
        if (trace is not { } t) return;

        _logger.LogTrace("<== {Operation} {Interface}.{Member} [id={MemberId}] instance={InstanceId} session={SessionId}",
            t.Operation, t.Interface, t.Member, t.MemberId, t.InstanceId, Id);
    }

    // Always false: used as an exception filter, so the failure is logged during the first pass
    // without catching the exception or disturbing its stack.
    private bool TraceFailure(DispatchTrace? trace, Exception exception)
    {
        if (trace is { } t)
            _logger.LogTrace(exception, "<== {Operation} {Interface}.{Member} [id={MemberId}] instance={InstanceId} session={SessionId} FAILED",
                t.Operation, t.Interface, t.Member, t.MemberId, t.InstanceId, Id);

        return false;
    }

    private async Task<RpcPacket?> OnRequest(RpcPacket request, CancellationToken cancellationToken)
    {
        try
        {
            using (SetSessionScope(this))
            {
                var trace = BeginTrace(request);
                try
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
                        await ReleaseInstanceAsync(rip.InstanceId).ConfigureAwait(false);
                        return new SuccessPacket(request);
                    }
                    else if (request is MethodCallPacket mcp)
                    {
                        var instance = GetAdapter(mcp.InstanceId) ??
                            throw new Exception("Unknown instance");


                        var (Result, Args) = await instance.InvokeMethodAsync(mcp.MethodId, mcp.Args, mcp.OneWay, cancellationToken).ConfigureAwait(false);
                        return new MethodResultPacket(mcp)
                        {
                            Result = Result,
                            Args = Args
                        };
                    }
                    else if (request is GetPropertyPacket gpp)
                    {
                        var instance = GetAdapter(gpp.InstanceId) ??
                            throw new Exception("Unknown instance");


                        var value = await instance.GetPropertyValueAsync(gpp.PropertyId, cancellationToken).ConfigureAwait(false);
                        return new PropertyValuePacket(gpp)
                        {
                            Value = value
                        };
                    }
                    else if (request is SetPropertyPacket spp)
                    {
                        var instance = GetAdapter(spp.InstanceId) ??
                            throw new Exception("Unknown instance");


                        await instance.SetPropertyValueAsync(spp.PropertyId, spp.Value, cancellationToken).ConfigureAwait(false);
                        return new SuccessPacket(spp);
                    }
                    else if (request is SubscribeForEventPacket sfep)
                    {
                        var instance = GetAdapter(sfep.InstanceId) ??
                            throw new Exception("Unknown instance");


                        await instance.AttachEventHandlerAsync(sfep.EventId, cancellationToken).ConfigureAwait(false);
                        return new SuccessPacket(sfep);
                    }
                    else if (request is SubscribeForEventsPacket sfeps)
                    {
                        var instance = GetAdapter(sfeps.InstanceId) ??
                            throw new Exception("Unknown instance");


                        await instance.AttachEventHandlersAsync(sfeps.EventIds, cancellationToken).ConfigureAwait(false);
                        return new SuccessPacket(sfeps);
                    }
                    else if (request is UnsubscribeFromEventPacket ufep)
                    {
                        var instance = GetAdapter(ufep.InstanceId) ??
                            throw new Exception("Unknown instance");


                        await instance.DetachEventHandlerAsync(ufep.EventId, cancellationToken).ConfigureAwait(false);
                        return new SuccessPacket(ufep);
                    }
                    else if (request is EventDataPacket)
                    {
                        // Fire-and-forget from the remote side; already handled by
                        // _packetHandlers (ServiceInstance). No response expected.
                        return null;
                    }
                    else if (request is ExceptionPacket)
                    {
                        // Stale error response not matched by any pending TCS.
                        // Drop silently to break any ExceptionPacket ping-pong loop.
                        return null;
                    }

                    throw new Exception("Unknown packet");
                }
                catch (Exception @__ex) when (TraceFailure(trace, @__ex))
                {
                    throw;
                }
                finally
                {
                    EndTrace(trace);
                }
            }
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace(ex, "Request handling failed: {Request}", request);
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

    // Re-establishes session state after the restorable channel reconnected to a brand-new remote session,
    // and is awaited BEFORE the connection is announced as Opened so app code reacting to Opened sees a
    // fully restored session (matching instance ids, replayed subscriptions). Invalidates proxies that
    // cannot be restored, drops the callbacks exposed to the dead session, then re-requests the root
    // service proxies and replays their event subscriptions. On the primary connect there is nothing to
    // restore yet, so it is a no-op. Best-effort: it never throws, so a restore failure does not abort the
    // open (the next reopen retries).
    internal async Task RestoreAfterReopenAsync(CancellationToken cancellationToken)
    {
        // The primary connection is the first open; only reconnects restore state.
        if (Interlocked.Increment(ref _openCount) == 1) return;
        if (_isDisposed != 0) return;

        try
        {
            HashSet<Guid> restorableIds;
            ServiceInstance[] toRestore;
            lock (_restorableInstances)
            {
                restorableIds = [.. _restorableInstances.Keys];
                toRestore = [.. _restorableInstances.Values];
            }

            KeyValuePair<Guid, RefCounter<ICallTarget>>[] proxies;
            lock (_proxies)
                proxies = [.. _proxies];

            // 1. Fail-fast every derived (non-restorable) proxy: its old instance id is meaningless
            //    on the new session and can never be recreated there.
            foreach (var (id, counter) in proxies)
            {
                if (restorableIds.Contains(id)) continue;

                ICallTarget target;
                try { target = counter.Value; }
                catch (ObjectDisposedException) { continue; }

                if (target is ServiceInstance si)
                    si.Invalidate();
            }

            // 2. Drop the adapters this session exposed to the (now gone) remote session.
            TeardownExposedAdapters();

            // 3. Re-request the root service proxies and replay their subscriptions. Best-effort:
            //    one failing instance must not abort the rest.
            await Task.WhenAll(toRestore.Select(i => RestoreInstanceAsync(i, cancellationToken))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug(ex, "Session restore failed after channel reopen: {SessionId}", Id);
        }
    }

    private async Task RestoreInstanceAsync(ServiceInstance instance, CancellationToken cancellationToken)
    {
        try
        {
            await instance.RestoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug(ex, "Failed to restore instance of type {Type}", instance.InterfaceType);
        }
    }
}
