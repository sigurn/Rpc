namespace Sigurn.Rpc;

/// <summary>
/// Defines RPC error codes.
/// </summary>
public enum RpcError: uint
{
    /// <summary>
    /// No error.
    /// </summary>
    None = 0x00000000,

    /// <summary>
    /// The requested service is not available.
    /// </summary>
    ServiceUnavailable = 0x80000001,
}