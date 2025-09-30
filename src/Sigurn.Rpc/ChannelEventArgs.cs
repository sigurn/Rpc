namespace Sigurn.Rpc;

/// <summary>
/// Provides information about channel to event handler.
/// </summary>
public class ChannelEventArgs : EventArgs
{
    /// <summary>
    /// Initializes new instance of the class.
    /// </summary>
    /// <param name="channel">Channel.</param>
    public ChannelEventArgs (IChannel channel)
    {
      Channel = channel;
    }

    /// <summary>
    /// Channel.
    /// </summary>
    public IChannel Channel { get; }
}
