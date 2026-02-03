using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

class ErrorPacket : RpcPacket
{
    private RpcError _error;
    private string? _stackTrace;

    public ErrorPacket()
        : base (PacketType.Error)
    {
        _error = RpcError.None;
    }

    public ErrorPacket(RpcError error)
        : base(PacketType.Error)
    {
        _error = error;
    }

    public ErrorPacket(RpcErrorException ex)
        : base(PacketType.Error)
    {
        _error = ex.Error;
        _stackTrace = ex.StackTrace;
    }

    public ErrorPacket(RpcPacket request)
        : base (PacketType.Error, request)
    {
        _error = RpcError.None;
    }

    public ErrorPacket(RpcPacket request, RpcError error)
        : base(PacketType.Error, request)
    {
        _error = error;
    }

    public ErrorPacket(RpcPacket request, RpcErrorException ex)
        : base(PacketType.Error, request)
    {
        _error = ex.Error;
        _stackTrace = ex.StackTrace;
    }

    public RpcError Error => _error;

    public string? StackTrace => _stackTrace;

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _error = await Serializer.FromStreamAsync<RpcError>(stream, context, cancellationToken);
        _stackTrace = await Serializer.FromStreamAsync<string>(stream, context, cancellationToken);
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _error, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, _stackTrace, context, cancellationToken);
    }
}