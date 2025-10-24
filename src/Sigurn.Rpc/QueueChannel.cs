namespace Sigurn.Rpc;

sealed class QueueChannel : IChainedChannel, IDisposable
{
    record class SendWorkItem(TaskCompletionSource<IPacket> TaskSource, IPacket Packet, CancellationToken CancellationToken);

    private readonly IChannel _channel;
    private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(1,1);
    private readonly Queue<SendWorkItem> _sendQueue = [];
    private volatile bool _isSending;

    public QueueChannel(IChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        _channel = channel;
        _isSending = false;

        _channel.Opening += (s, e) => Opening?.Invoke(this, e);
        _channel.Opened += (s, e) => Opened?.Invoke(this, e);
        _channel.Closing += (s, e) => Closing?.Invoke(this, e);
        _channel.Closed += (s, e) =>
        {
            CancelAllOperations();
            Closed?.Invoke(this, e);
        };

        _channel.Faulted += (s, e) =>
        {
            CancelAllOperations();
            Faulted?.Invoke(this, e);
        };
    }

    private void CancelAllOperations()
    {
        IReadOnlyList<SendWorkItem> items;
        lock(_sendQueue)
        {
            items = _sendQueue.ToList();
            _sendQueue.Clear();
        }

        foreach(var swi in items)
            swi.TaskSource.TrySetCanceled();
    }
    
    public void Dispose()
    {
        if (_channel is IDisposable d)
            d.Dispose();
    }

    public IChannel BaseChannel => _channel;
    
    public ChannelState State => _channel.State;

    public object? BoundObject
    { 
        get => _channel.BoundObject;
        set => _channel.BoundObject = value; 
    }

    public event EventHandler? Opening;
    public event EventHandler? Opened;
    public event EventHandler? Closing;
    public event EventHandler? Closed;
    public event EventHandler? Faulted;

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        await _channel.CloseAsync(cancellationToken);

        IReadOnlyList<SendWorkItem> items;
        lock(_sendQueue)
        {
            items = _sendQueue.ToList();
            _sendQueue.Clear();
        }

        foreach(var swi in items)
            swi.TaskSource.SetCanceled();
    }

    public Task OpenAsync(CancellationToken cancellationToken)
    {
        return _channel.OpenAsync(cancellationToken);
    }

    public Task<IPacket> ReceiveAsync(CancellationToken cancellationToken)
    {
        return _channel.ReceiveAsync(cancellationToken);
    }

    public Task<IPacket> SendAsync(IPacket packet, CancellationToken cancellationToken)
    {
        if (_channel.State != ChannelState.Opened)
            throw new InvalidOperationException("The channel is not opened.");

        TaskCompletionSource<IPacket> tcs = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock(_sendQueue)
        {
            if (_sendQueue.Count != 0 || _isSending)
                _sendQueue.Enqueue(new SendWorkItem(tcs, packet, cancellationToken));

            if (!_isSending)
            {
                _isSending = true;
                _channel.SendAsync(packet, cancellationToken)
                    .ContinueWith(t => SendCompleteionHandler(t, tcs));
            }
        }

        return tcs.Task;
    }

    private void SendCompleteionHandler(Task<IPacket> task, TaskCompletionSource<IPacket> tcs)
    {
        if (task.IsCanceled)
        {
            tcs.TrySetCanceled();
        }
        else if (task.IsFaulted)
        {
            tcs.TrySetException(task.Exception);
        }
        else
        {
            tcs.TrySetResult(task.Result);
        }

        if (_channel.State != ChannelState.Opened) return;

        SendWorkItem? swi;
        lock (_sendQueue)
        {
            if (!_sendQueue.TryDequeue(out swi))
            {
                _isSending = false;
                return;
            }

            _isSending = true;
        }

        _channel.SendAsync(swi.Packet, swi.CancellationToken)
            .ContinueWith(t => SendCompleteionHandler(t, swi.TaskSource));
    }
}