namespace Sigurn.Rpc;

public interface IChainedChannel : IChannel
{
    IChannel BaseChannel { get; }
}