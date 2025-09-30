namespace Sigurn.Rpc.Infrastructure.Packets;

using Sigurn.Serialize;

class GetInstancePacket : RpcPacket
{
    public GetInstancePacket()
        : base(PacketType.GetInstance)
    {        
    }

    private Guid _id;
    public Guid InterfaceId
    { 
        get => _id;
        init => _id = value;
    }

    private Guid? _instanceId;
    public Guid? InstanceId
    {
        get => _instanceId;
        init => _instanceId = value;
    }

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _id = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken);
        _instanceId = await Serializer.FromStreamAsync<Guid?>(stream, context, cancellationToken);
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _id, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, _instanceId, context, cancellationToken);
    }
}

