using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

sealed class MethodResultPacket : RpcPacket
{
    public MethodResultPacket()
        : base(PacketType.MethodResult)
    {
    }

    public MethodResultPacket(RpcPacket package)
        : base(PacketType.MethodResult, package)
    {
    }

    public byte[]? Result { get; set; }

    public IReadOnlyList<byte[]>? Args { get; set; }

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        Result = await Serializer.FromStreamAsync<byte[]?>(stream, context, cancellationToken);
        Args = await Serializer.FromStreamAsync<IReadOnlyList<byte[]>?>(stream, context,cancellationToken);
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, Result, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, Args, context, cancellationToken);
    }
}