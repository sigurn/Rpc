using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

class SubscribeForEventPacket : RpcPacket
{
    public SubscribeForEventPacket()
        : base(PacketType.SubscribeForEvent)
    {
    }

    public SubscribeForEventPacket(RpcPacket rpcPacket)
        : base(PacketType.SubscribeForEvent, rpcPacket)
    {
    }

    private Guid _instanceId;
    public Guid InstanceId
    {
        get => _instanceId;
        init => _instanceId = value;
    }

    private int _eventId;
    public int EventId
    {
        get => _eventId;
        init => _eventId = value;
    }

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _instanceId = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken).ConfigureAwait(false);
        _eventId = await Serializer.FromStreamAsync<int>(stream, context,cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _instanceId, context, cancellationToken).ConfigureAwait(false);
        await Serializer.ToStreamAsync(stream, _eventId, context, cancellationToken).ConfigureAwait(false);
    }
}