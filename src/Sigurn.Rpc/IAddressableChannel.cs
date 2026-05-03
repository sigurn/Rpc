namespace Sigurn.Rpc;

/// <summary>
/// Represents a channel that exposes its local and remote address information.
/// </summary>
public interface IAddressableChannel
{
    /// <summary>
    /// Gets the local address of the channel.
    /// </summary>
    string LocalAddress { get; }

    /// <summary>
    /// Gets the remote address of the channel.
    /// </summary>
    string RemoteAddress { get; }
}