namespace Sigurn.Rpc.Infrastructure.Packets;

using Sigurn.Serialize;

class ReleaseInstancePacket : RpcPacket
{
    public ReleaseInstancePacket()
        : base(PacketType.ReleaseInstance)
    {        
    }


    private Guid _instanceId;
    public Guid InstanceId
    {
        get => _instanceId;
        init => _instanceId = value;
    }

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _instanceId = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken);
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _instanceId, context, cancellationToken);
    }
}

