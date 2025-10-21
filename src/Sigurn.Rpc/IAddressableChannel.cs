namespace Sigurn.Rpc;

public interface IAddressableChannel
{
    string LocalAddress { get; }

    string RemoteAddress { get; }
}