namespace Sigurn.Rpc;

/// <summary>
/// Represents a decorator channel that wraps an underlying base channel.
/// </summary>
public interface IChainedChannel : IChannel
{
    /// <summary>
    /// Gets the underlying base channel being decorated.
    /// </summary>
    IChannel BaseChannel { get; }
}