namespace Sigurn.Rpc;

// <summary>
  /// Specifies the state of the channel
  /// </summary>
  public enum ChannelState
  {
    /// <summary>
    /// The channel was just created.
    /// </summary>
    Created,

    /// <summary>
    /// Channel is opening.
    /// </summary>
    Opening,

    /// <summary>
    /// Channel is opened.
    /// </summary>
    Opened,

    /// <summary>
    /// Channel is closing.
    /// </summary>
    Closing,

    /// <summary>
    /// Channel is closed.
    /// </summary>
    Closed,

    /// <summary>
    /// Channel is in the faulted state.
    /// An error has happened during communication through the channel.
    /// </summary>
    Faulted
  }