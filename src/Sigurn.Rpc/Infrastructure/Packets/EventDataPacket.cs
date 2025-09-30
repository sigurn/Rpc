using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

class EventDataPacket : RpcPacket
{
    private Guid _instanceId;
    private int _eventId;
    private IReadOnlyList<byte[]> _args;

    public EventDataPacket()
        : base(PacketType.EventDataPacket)
    {
        _eventId = 0;
        _args = [];
    }

    public EventDataPacket(Guid instanceId, int eventId, IReadOnlyList<byte[]> args)
        : base(PacketType.EventDataPacket)
    {
        _instanceId = instanceId;
        _eventId = eventId;
        _args = args;
    }

    public EventDataPacket(RpcPacket rpcPacket, Guid instanceId, int eventId, IReadOnlyList<byte[]> args)
        : base(PacketType.EventDataPacket, rpcPacket)
    {
        _instanceId = instanceId;
        _eventId = eventId;
        _args = args;   
    }

    public Guid InstanceId => _instanceId;

    public int EventId => _eventId;

    public IReadOnlyList<byte[]> Args => _args;

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _instanceId = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken);
        _eventId = await Serializer.FromStreamAsync<int>(stream, context, cancellationToken);
        _args = await Serializer.FromStreamAsync<IReadOnlyList<byte[]>>(stream, context, cancellationToken) ?? [];
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _instanceId, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, _eventId, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, _args, context, cancellationToken);
    }
}