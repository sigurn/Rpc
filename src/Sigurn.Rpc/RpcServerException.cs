using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc;

public class RpcServerException : Exception
{
    private string _exceptionType;
    private string _exceptionMessage;
    private string? _exceptionStack;

    public RpcServerException(Exception exception)
        : base ("Server has thrown exception")
    {
        ArgumentNullException.ThrowIfNull(exception);

        _exceptionType = exception.GetType().FullName ?? throw new ArgumentException("Exception type name cannot be null", nameof(exception));
        _exceptionMessage = exception.Message;
        _exceptionStack = exception.StackTrace;
    }

    internal RpcServerException(ExceptionPacket packet)
        : base (packet.StackTrace is not null ? 
            $"Server has thrown exception.\nException: {packet.Type}\nMessage: {packet.Message}\nStack:\n{packet.StackTrace}":
            $"Server has thrown exception.\nException: {packet.Type}\nMessage: {packet.Message}")
    {
        _exceptionType = packet.Type;
        _exceptionMessage = packet.Message;
        _exceptionStack = packet.StackTrace;
    }

    public string ServerExceptionType => _exceptionType;

    public string ServerExceptionMessage => _exceptionMessage;

    public string? ServerExceptionStack => _exceptionStack;

    internal static void Throw(ExceptionPacket packet)
    {
        throw new RpcServerException(packet);
    }
}