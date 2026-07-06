using Sigurn.Rpc.Infrastructure.Packets;

namespace Sigurn.Rpc.Infrastructure;

internal class ServiceInstance : ICallTarget, IDisposable, IAsyncDisposable
{
    public readonly RpcHandler _handler;
    private readonly IDisposable _removeHandler;

    private readonly object _lock = new();
    private Guid _instanceId;

    // Events currently attached through this instance, ref-counted (several proxies can share one
    // ServiceInstance and each attaches independently). Replayed on RestoreAsync after a reopen.
    private readonly Dictionary<int, int> _attachedEvents = new();

    private bool _isValid = true;

    public ServiceInstance(Guid instanceId, RpcHandler handler)
        : this(instanceId, handler, typeof(object))
    {
    }

    public ServiceInstance(Guid instanceId, RpcHandler handler, Type interfaceType)
    {
        _instanceId = instanceId;
        _handler = handler;
        InterfaceType = interfaceType;
        _removeHandler = _handler.Handle<EventDataPacket>(EventDataPacketHandler);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().Wait();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _handler.ReleaseServiceInstanceAsync(InstanceId, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _removeHandler.Dispose();
        }
    }

    public Guid InstanceId
    {
        get { lock (_lock) return _instanceId; }
    }

    internal Type InterfaceType { get; }

    // False once the instance can no longer be used against the current remote session (a derived
    // proxy that could not be restored after a reopen). Call surfaces fail fast instead of hitting
    // the server with a stale id.
    internal bool IsValid
    {
        get { lock (_lock) return _isValid; }
    }

    internal void Invalidate()
    {
        lock (_lock) _isValid = false;
    }

    // Re-request a fresh instance id on the new remote session and replay all active event
    // subscriptions in a single batch round-trip.
    internal async Task RestoreAsync(CancellationToken cancellationToken)
    {
        Guid newId;
        try
        {
            newId = await _handler.GetServiceInstanceAsync(InterfaceType.GUID, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The service is no longer available on the new remote session (e.g. it is not registered
            // there anymore) or could not be re-acquired. Invalidate so subsequent calls fail fast with
            // RpcInvalidInstanceException instead of hitting the server with a stale instance id. A later
            // reopen re-attempts restoration and revalidates if the service comes back.
            Invalidate();
            throw;
        }

        int[] eventIds;
        lock (_lock)
        {
            _instanceId = newId;
            _isValid = true;
            eventIds = [.. _attachedEvents.Keys];
        }

        if (eventIds.Length > 0)
            await _handler.AttachEventsAsync(newId, eventIds, cancellationToken).ConfigureAwait(false);
    }

    internal RpcHandler Handler => _handler;

    private void CheckValid()
    {
        if (!IsValid)
            throw new RpcInvalidInstanceException();
    }

    public async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        CheckValid();
        return await _handler.InvokeMethodAsync(InstanceId, methodId, args ?? [], oneWay, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken)
    {
        CheckValid();
        return await _handler.GetPropertyAsync(InstanceId, propertyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken)
    {
        CheckValid();
        await _handler.SetPropertyAsync(InstanceId, propertyId, value, cancellationToken).ConfigureAwait(false);
    }

    public async Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        CheckValid();

        lock (_lock)
        {
            _attachedEvents[eventId] = _attachedEvents.GetValueOrDefault(eventId) + 1;
        }

        await _handler.AttachEventAsync(InstanceId, eventId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        CheckValid();

        lock (_lock)
        {
            if (_attachedEvents.TryGetValue(eventId, out var count))
            {
                if (count <= 1) _attachedEvents.Remove(eventId);
                else _attachedEvents[eventId] = count - 1;
            }
        }

        await _handler.DetachEventAsync(InstanceId, eventId, cancellationToken).ConfigureAwait(false);
    }

    public event EventHandler<EventDataArgs>? EventTriggered;

    private Task<RpcPacket?> EventDataPacketHandler(EventDataPacket packet, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (packet.InstanceId == InstanceId)
            ThreadPool.QueueUserWorkItem<EventDataPacket>((edp) =>
            {
                EventTriggered?.Invoke(this, new EventDataArgs(edp.EventId, edp.Args));
            }, packet, true);

        return Task.FromResult<RpcPacket?>(null);
    }
}
