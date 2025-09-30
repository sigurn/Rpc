namespace Sigurn.Rpc;

public enum RpcError: uint
{
    None = 0x00000000,
    ServiceUnavailable = 0x80000001,
}