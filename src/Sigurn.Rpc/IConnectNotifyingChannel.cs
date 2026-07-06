namespace Sigurn.Rpc;

/// <summary>
/// Implemented by channels that distinguish "transport is live for RPC" from "channel is ready for
/// normal use". The <see cref="Connected"/> event is raised once the transport can carry RPC traffic
/// (before any session-initialize hook and before the public <see cref="IChannel.Opened"/> event), so
/// the RPC receive loop can resume and the hook can perform calls before the connection is announced
/// as ready.
/// </summary>
interface IConnectNotifyingChannel
{
    /// <summary>
    /// Occurs when the transport is live and RPC traffic can flow, ahead of <see cref="IChannel.Opened"/>.
    /// </summary>
    event EventHandler Connected;
}
