using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc;

/// <summary>
/// Represents an exception thrown on the server side and propagated to the client.
/// </summary>
public class RpcServerException : Exception
{
    private string _exceptionType;
    private string _exceptionMessage;
    private string? _exceptionStack;

    /// <summary>
    /// Initializes a new instance of <see cref="RpcServerException"/> from an exception that occurred on the server.
    /// </summary>
    /// <param name="exception">The server-side exception to wrap.</param>
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

    /// <summary>
    /// Gets the fully qualified type name of the original server-side exception.
    /// </summary>
    public string ServerExceptionType => _exceptionType;

    /// <summary>
    /// Gets the message of the original server-side exception.
    /// </summary>
    public string ServerExceptionMessage => _exceptionMessage;

    /// <summary>
    /// Gets the stack trace of the original server-side exception, if available.
    /// </summary>
    public string? ServerExceptionStack => _exceptionStack;

    internal static void Throw(ExceptionPacket packet)
    {
        throw new RpcServerException(packet);
    }
}