using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

class GetPropertyPacket : RpcPacket
{
    public GetPropertyPacket()
        : base(PacketType.GetProperty)
    {
    }

    public GetPropertyPacket(RpcPacket rpcPacket)
        : base(PacketType.GetProperty, rpcPacket)
    {
    }

    private Guid _instanceId;
    public Guid InstanceId
    {
        get => _instanceId;
        init => _instanceId = value;
    }

    private int _propertyId;
    public int PropertyId
    {
        get => _propertyId;
        init => _propertyId = value;
    }

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _instanceId = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken);
        _propertyId = await Serializer.FromStreamAsync<int>(stream, context, cancellationToken);
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _instanceId, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, _propertyId, context, cancellationToken);
    }
}