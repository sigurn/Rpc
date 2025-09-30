namespace Sigurn.Rpc;

interface IChainedChannel : IChannel
{
    IChannel BaseChannel { get; }
}