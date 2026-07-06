using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

// Batch counterpart of SubscribeForEventPacket: subscribes to several events of one instance in a
// single round-trip. Used when restoring a proxy's subscriptions after a channel reopen.
class SubscribeForEventsPacket : RpcPacket
{
    public SubscribeForEventsPacket()
        : base(PacketType.SubscribeForEvents)
    {
    }

    public SubscribeForEventsPacket(RpcPacket rpcPacket)
        : base(PacketType.SubscribeForEvents, rpcPacket)
    {
    }

    private Guid _instanceId;
    public Guid InstanceId
    {
        get => _instanceId;
        init => _instanceId = value;
    }

    private IReadOnlyList<int> _eventIds = [];
    public IReadOnlyList<int> EventIds
    {
        get => _eventIds;
        init => _eventIds = value;
    }

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _instanceId = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken).ConfigureAwait(false);
        _eventIds = await Serializer.FromStreamAsync<IReadOnlyList<int>>(stream, context, cancellationToken).ConfigureAwait(false) ?? [];
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _instanceId, context, cancellationToken).ConfigureAwait(false);
        await Serializer.ToStreamAsync(stream, _eventIds, context, cancellationToken).ConfigureAwait(false);
    }
}
