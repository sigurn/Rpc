using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

sealed class SuccessPacket : RpcPacket
{
    public SuccessPacket()
        : base(PacketType.Success)
    {        
    }

    public SuccessPacket(RpcPacket package)
        : base(PacketType.Success, package)
    {        
    }

    protected override Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected override Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}