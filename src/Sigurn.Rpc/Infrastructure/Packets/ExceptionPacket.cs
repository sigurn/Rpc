using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

class ExceptionPacket : RpcPacket
{
    private string _type;
    private string _message;
    private string? _stackTrace;

    public ExceptionPacket()
        : base(PacketType.Exception)
    {
        _type = string.Empty;
        _message = string.Empty;
        _stackTrace = null;
    }

    public ExceptionPacket(Exception ex)
        : base(PacketType.Exception)
    {
        _type = ex.GetType().ToString();
        _message = ex.Message;
        _stackTrace = ex.StackTrace;
    }

    public ExceptionPacket(RpcPacket rpcPacket, Exception ex)
        : base(PacketType.Exception, rpcPacket)
    {        
        _type = ex.GetType().ToString();
        _message = ex.Message;
        _stackTrace = ex.StackTrace;
    }

    public string Type => _type;

    public string Message => _message;

    public string? StackTrace => _stackTrace;

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _type = await Serializer.FromStreamAsync<string>(stream, context, cancellationToken) ?? string.Empty;
        _message = await Serializer.FromStreamAsync<string>(stream, context, cancellationToken) ?? string.Empty;
        _stackTrace = await Serializer.FromStreamAsync<string>(stream, context, cancellationToken);
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _type, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, _message, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, _stackTrace, context, cancellationToken);
    }
}