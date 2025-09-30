using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

sealed class CancelRequestPacket : RpcPacket
{
    internal CancelRequestPacket()
        : base(PacketType.CancelRequest)
    {
    }

    public CancelRequestPacket(RpcPacket packet)
        : base(PacketType.CancelRequest, packet)
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