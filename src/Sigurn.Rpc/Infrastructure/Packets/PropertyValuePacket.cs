using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

class PropertyValuePacket : RpcPacket
{
    public PropertyValuePacket()
        : base(PacketType.PropertyValue)
    {
    }

    public PropertyValuePacket(RpcPacket rpcPacket)
        : base(PacketType.PropertyValue, rpcPacket)
    {
    }

    private byte[]? _value;
    public byte[]? Value
    {
        get => _value;
        init => _value = value;
    }

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _value = await Serializer.FromStreamAsync<byte[]>(stream, context,cancellationToken);
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _value, context, cancellationToken);
    }
}