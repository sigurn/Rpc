namespace Sigurn.Rpc;

/// <summary>
/// Specifies API for the communication channel.
/// </summary>
public interface IChannel
{
    /// <summary>
    /// Opens communication channel asyncronously
    /// </summary>
    Task OpenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Closes communication channel asynchronously.
    /// </summary>
    Task CloseAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends packet asynchronuously.
    /// </summary>
    /// <param name="packet">Packet that should be sent.</param>
    /// <param name="cancellationToken">Canvellation token.</param>
    /// <returns>Packet that was sent.</returns>
    Task<IPacket> SendAsync(IPacket packet, CancellationToken cancellationToken);

    /// <summary>
    /// Receives packet from channel asynchronuously.
    /// </summary>
    /// <returns>Received packet.</returns>
    Task<IPacket> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The current state of the channel.
    /// </summary>
    ChannelState State { get; }

    /// <summary>
    /// An object associated with the channel.
    /// </summary>
    /// <remarks>Developer can associate any object with the channel for the any purposes.
    /// This object is not processed by channel in any way.</remarks>
    object? BoundObject { get; set; }

    /// <summary>
    /// Occures when the channel is opening.
    /// </summary>
    event EventHandler Opening;

    /// <summary>
    /// Occures when the channel is opened.
    /// </summary>
    event EventHandler Opened;

    /// <summary>
    /// Occures when the channel is closing.
    /// </summary>
    event EventHandler Closing;

    /// <summary>
    /// Occures when the channel is closed.
    /// </summary>
    event EventHandler Closed;

    /// <summary>
    /// Occures when the channel is in the faulted state.
    /// </summary>
    event EventHandler Faulted;
};