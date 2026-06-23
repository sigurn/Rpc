using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

class SetPropertyPacket : RpcPacket
{
    public SetPropertyPacket()
        : base(PacketType.SetProperty)
    {
    }

    public SetPropertyPacket(RpcPacket rpcPacket)
        : base(PacketType.SetProperty, rpcPacket)
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

    private byte[]? _value;
    public byte[]? Value
    {
        get => _value;
        init => _value = value;
    }

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _instanceId = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken).ConfigureAwait(false);
        _propertyId = await Serializer.FromStreamAsync<int>(stream, context, cancellationToken).ConfigureAwait(false);
        _value = await Serializer.FromStreamAsync<byte[]>(stream, context, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _instanceId, context, cancellationToken).ConfigureAwait(false);
        await Serializer.ToStreamAsync(stream, _propertyId, context, cancellationToken).ConfigureAwait(false);
        await Serializer.ToStreamAsync(stream, _value, context, cancellationToken).ConfigureAwait(false);
    }
}