using Sigurn.Rpc.Infrastructure.Packets;

namespace Sigurn.Rpc;

public class RpcErrorException : Exception
{
    private readonly RpcError _error;
    private readonly string? _remoteStackTrace;

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

    public RpcErrorException(RpcError error, Exception innerException)
        : base("RPC error: ${error}", innerException)
    {
        _error = error;
    }

    public RpcError Error => _error;

    public string? RemoteStackTrace => _remoteStackTrace;


    internal static void Throw(ErrorPacket erp)
    {
        throw new RpcErrorException(erp);
    }
}