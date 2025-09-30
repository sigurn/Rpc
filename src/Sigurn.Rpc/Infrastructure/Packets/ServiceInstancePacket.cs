using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

class ServiceInstancePacket : RpcPacket
{
    public ServiceInstancePacket()
        : base(PacketType.ServiceInstance)
    {
        _instanceId = Guid.Empty;
    }

    public ServiceInstancePacket(RpcPacket package)
        : base(PacketType.ServiceInstance, package)
    {
        _instanceId = Guid.Empty;
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