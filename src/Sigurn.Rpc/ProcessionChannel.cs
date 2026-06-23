namespace Sigurn.Rpc;

/// <summary>
/// Abstract base class for decorator channels that process packets before sending or after receiving.
/// </summary>
public abstract class ProcessionChannel : IChainedChannel, IDisposable
{
    private readonly IChannel _channel;
    private volatile int _isDisposed = 0;

    /// <summary>
    /// Initializes a new instance of <see cref="ProcessionChannel"/> wrapping the specified channel.
    /// </summary>
    /// <param name="channel">The underlying channel to wrap.</param>
    protected ProcessionChannel(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
    }

    /// <summary>
    /// Gets the underlying base channel being decorated.
    /// </summary>
    public IChannel BaseChannel => _channel;

    /// <summary>
    /// Gets the current state of the channel.
    /// </summary>
    public ChannelState State => _channel.State;

    /// <summary>
    /// An object associated with the channel.
    /// </summary>
    /// <remarks>Developer can associate any object with the channel for any purposes.
    /// This object is not processed by channel in any way.</remarks>
    public object? BoundObject
    {
        get => _channel.BoundObject;
        set => _channel.BoundObject = value;
    }

    /// <summary>
    /// Occures when the channel is opening.
    /// </summary>
    public event EventHandler Opening
    {
        add => _channel.Opening += value;
        remove => _channel.Opening -= value;
    }

    /// <summary>
    /// Occures when the channel is opened.
    /// </summary>
    public event EventHandler Opened
    {
        add => _channel.Opened += value;
        remove => _channel.Opened -= value;
    }

    /// <summary>
    /// Occures when the channel is closing.
    /// </summary>
    public event EventHandler Closing
    {
        add => _channel.Closing += value;
        remove => _channel.Closing -= value;

    }

    /// <summary>
    /// Occures when the channel is closed.
    /// </summary>
    public event EventHandler Closed
    {
        add => _channel.Closed += value;
        remove => _channel.Closed -= value;
    }

    /// <summary>
    /// Occures when the channel is in the faulted state.
    /// </summary>
    public event EventHandler Faulted
    {
        add => _channel.Faulted += value;
        remove => _channel.Faulted -= value;
    }

    /// <summary>
    /// Releases resources used by the channel.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        Dispose(true);
        
        if (_channel is IDisposable d)
            d.Dispose();
    }

    /// <summary>
    /// Opens the communication channel asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        Exception? exception = null;

        OnOpening(cancellationToken);

        try
        {
            await _channel.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        OnOpened(exception, cancellationToken);

        if (exception is not null)
            throw exception;
    }

    /// <summary>
    /// Closes the communication channel asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        Exception? exception = null;

        OnClosing(cancellationToken);

        try
        {
            await _channel.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        OnClosed(exception, cancellationToken);

        if (exception is not null)
            throw exception;
    }

    /// <summary>
    /// Receives a packet from the channel asynchronously and processes it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The processed received packet.</returns>
    public async Task<IPacket> ReceiveAsync(CancellationToken cancellationToken)
    {
        return await ProcessReceivedPacket(await _channel.ReceiveAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes and sends a packet through the channel asynchronously.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The packet that was sent.</returns>
    public async Task<IPacket> SendAsync(IPacket packet, CancellationToken cancellationToken)
    {
        return await _channel.SendAsync(await ProcessSendingPacket(packet, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
    }

    protected virtual void OnOpening(CancellationToken cancellationToken)
    {

    }

    protected virtual void OnOpened(Exception? ex, CancellationToken cancellationToken)
    {

    }

    protected virtual void OnClosing(CancellationToken cancellationToken)
    {

    }

    protected virtual void OnClosed(Exception? ex, CancellationToken cancellationToken)
    {

    }

    protected virtual void Dispose(bool disposing)
    {

    }

    protected abstract Task<IPacket> ProcessReceivedPacket(IPacket packet, CancellationToken cancellationToken);

    protected abstract Task<IPacket> ProcessSendingPacket(IPacket packet, CancellationToken cancellationToken);
}