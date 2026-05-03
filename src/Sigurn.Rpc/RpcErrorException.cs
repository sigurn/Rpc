using Sigurn.Rpc.Infrastructure.Packets;

namespace Sigurn.Rpc;

/// <summary>
/// Represents an RPC error returned by the remote side.
/// </summary>
public class RpcErrorException : Exception
{
    private readonly RpcError _error;
    private readonly string? _remoteStackTrace;

    /// <summary>
    /// Initializes a new instance of <see cref="RpcErrorException"/> with the specified RPC error code.
    /// </summary>
    /// <param name="error">The RPC error code.</param>
    public RpcErrorException(RpcError error)
        : base("RPC error: ${error}")
    {
        _error = error;
    }

    internal RpcErrorException(ErrorPacket erp)
        : base("RPC error: ${error}")
    {
        _error = erp.Error;
        _remoteStackTrace = erp.StackTrace;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="RpcErrorException"/> with the specified RPC error code and inner exception.
    /// </summary>
    /// <param name="error">The RPC error code.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public RpcErrorException(RpcError error, Exception innerException)
        : base("RPC error: ${error}", innerException)
    {
        _error = error;
    }

    /// <summary>
    /// Gets the RPC error code.
    /// </summary>
    public RpcError Error => _error;

    /// <summary>
    /// Gets the stack trace from the remote side, if available.
    /// </summary>
    public string? RemoteStackTrace => _remoteStackTrace;


    internal static void Throw(ErrorPacket erp)
    {
        throw new RpcErrorException(erp);
    }
}