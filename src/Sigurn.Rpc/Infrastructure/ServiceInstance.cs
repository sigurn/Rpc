using Sigurn.Rpc.Infrastructure.Packets;

namespace Sigurn.Rpc.Infrastructure;

internal class ServiceInstance : ICallTarget, IDisposable, IAsyncDisposable
{
    public readonly RpcHandler _handler;
    private readonly IDisposable _removeHandler;
    
    public ServiceInstance(Guid instanceId, RpcHandler handler)
    {
        InstanceId = instanceId;
        _handler = handler;
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

    public Guid InstanceId { get; }

    internal RpcHandler Handler => _handler;

    public async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        return await _handler.InvokeMethodAsync(InstanceId, methodId, args ?? [], oneWay, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken)
    {
        return await _handler.GetPropertyAsync(InstanceId, propertyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken)
    {
        await _handler.SetPropertyAsync(InstanceId, propertyId, value, cancellationToken).ConfigureAwait(false);
    }

    public async Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        await _handler.AttachEventAsync(InstanceId, eventId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
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