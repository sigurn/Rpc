namespace Sigurn.Rpc;

/// <summary>
/// This interface represents a channel host.
/// </summary>
public interface IChannelHost
{
    /// <summary>
    /// Defines if the host is opened
    /// </summary>
    bool IsOpened { get; }

    /// <summary>
    /// Opens a host.
    /// </summary>
    void Open();

    /// <summary>
    /// Closes a host.
    /// </summary>
    void Close();

    /// <summary>
    /// Occures when new client is connected.
    /// </summary>
    event EventHandler<ChannelEventArgs> Connected;

    /// <summary>
    /// Occures when cleint is disconnected.
    /// </summary>
    event EventHandler<ChannelEventArgs> Disconnected;
}
