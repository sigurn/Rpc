namespace Sigurn.Rpc;

public abstract class ProcessionChannel : IChainedChannel, IDisposable
{
    private readonly IChannel _channel;
    private volatile int _isDisposed = 0;

    protected ProcessionChannel(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
    }

    public IChannel BaseChannel => _channel;

    public ChannelState State => _channel.State;

    public object? BoundObject
    {
        get => _channel.BoundObject;
        set => _channel.BoundObject = value;
    }

    public event EventHandler Opening
    {
        add => _channel.Opening += value;
        remove => _channel.Opening -= value;
    }

    public event EventHandler Opened
    {
        add => _channel.Opened += value;
        remove => _channel.Opened -= value;
    }

    public event EventHandler Closing
    {
        add => _channel.Closing += value;
        remove => _channel.Closing -= value;

    }

    public event EventHandler Closed
    {
        add => _channel.Closed += value;
        remove => _channel.Closed -= value;
    }

    public event EventHandler Faulted
    {
        add => _channel.Faulted += value;
        remove => _channel.Faulted -= value;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        Dispose(true);
        
        if (_channel is IDisposable d)
            d.Dispose();
    }

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        Exception? exception = null;

        OnOpening(cancellationToken);

        try
        {
            await _channel.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        OnOpened(exception, cancellationToken);

        if (exception is not null)
            throw exception;
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        Exception? exception = null;

        OnClosing(cancellationToken);

        try
        {
            await _channel.CloseAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        OnClosed(exception, cancellationToken);

        if (exception is not null)
            throw exception;
    }

    public async Task<IPacket> ReceiveAsync(CancellationToken cancellationToken)
    {
        return await ProcessReceivedPacket(await _channel.ReceiveAsync(cancellationToken), cancellationToken);
    }

    public async Task<IPacket> SendAsync(IPacket packet, CancellationToken cancellationToken)
    {
        return await _channel.SendAsync(await ProcessSendingPacket(packet, cancellationToken), cancellationToken);
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