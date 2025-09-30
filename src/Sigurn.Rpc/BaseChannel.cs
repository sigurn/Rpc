using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

public abstract class BaseChannel : IChannel, IDisposable
{
    protected readonly object _lock = new ();
    private volatile bool _isDisposed = false;

    private CancellationTokenSource? _openCancellationSource;
    private Task? _openTask;

    protected BaseChannel()
    {
    }

    public void Dispose()
    {
        lock(_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        Dispose(true);
    }

    private ChannelState _state;
    public ChannelState State
    {
        get
        {
            lock(_lock)
                return _state;
        }

        set
        {
            lock(_lock)
                _state = value;
        }
    }

    private object? _boundObject = null;
    public object? BoundObject
    { 
        get
        {
            lock(_lock)
                return _boundObject;
        }

        set
        {
            lock(_lock)
                _boundObject = value;
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        Task? task = null;

        lock (_lock)
        {
            if (_state == ChannelState.Closed || _state == ChannelState.Closing)
                return;

            if (_state == ChannelState.Opening)
            {
                if (_openCancellationSource is null || _openTask is null)
                    throw new InvalidOperationException("The cannel is in opening state. Cannot close it now");

                task = _openTask;
                _openCancellationSource.Cancel();
            }
        }

        if (task is not null)
        {
            try
            {
                await task;
            }
            catch
            {
            }
        }

        lock(_lock)
        {
            if (_state == ChannelState.Closed || _state == ChannelState.Closing)
                return;

            _state = ChannelState.Closing;
        }

        try
        {            
            RaiseClosing();

            await InternalCloseAsync(cancellationToken);

            lock(_lock)
                _state = ChannelState.Closed;

            RaiseClosed( );
        }
        catch
        {
            GoToFaultedState( );
            throw;
        }
    }

    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        CheckFaulted();

        using ManualResetEvent openEvent = new ManualResetEvent(false);
        Task task;
        CancellationTokenSource openCancellationSource;
        lock(_lock)
        {
            if (_state == ChannelState.Opened || _state == ChannelState.Opening)
                return;

            openCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _openCancellationSource = openCancellationSource;
            task = DelayedOpenAsync(openEvent, _openCancellationSource.Token);
            _openTask = task;

            _state = ChannelState.Opening;
        }

        try
        {
            RaiseOpening();

            openEvent.Set();
            await task;

            lock(_lock)
            {
                _openTask = null;
                _openCancellationSource = null;

                _state = ChannelState.Opened;
            }

            RaiseOpened();
        }
        catch
        {
            GoToFaultedState();
            throw;
        }
        finally
        {
            task.Dispose();
            openCancellationSource.Dispose();
        }
    }

    public async Task<IPacket> ReceiveAsync(CancellationToken cancellationToken)
    {
        lock(_lock)
            if (_state != ChannelState.Opened)
                throw new InvalidOperationException("The channel is not opened.");

        return await InternalReceiveAsync(cancellationToken);
    }

    public async Task<IPacket> SendAsync(IPacket packet, CancellationToken cancellationToken)
    {
        lock(_lock)
            if (_state != ChannelState.Opened)
                throw new InvalidOperationException("The channel is not opened.");

        return await InternalSendAsync(packet, cancellationToken);
    }

    private EventHandler? _opening;
    public event EventHandler Opening
    {
        add
        {
            lock(_lock)
                _opening += value;
        }
        remove
        {
            lock(_lock)
                _opening -= value;
        }
    }

    private EventHandler? _opened;
    public event EventHandler Opened
    {
        add
        {
            lock(_lock)
                _opened += value;
        }

        remove
        {
            lock(_lock)
                _opened -= value;
        }
    }

    private EventHandler? _closing;
    public event EventHandler Closing
    {
        add
        {
            lock(_lock)
                _closing += value;
        }

        remove
        {
            lock(_lock)
                _closing -= value;
        }
    }

    private EventHandler? _closed;
    public event EventHandler Closed
    {
        add
        {
            lock(_lock)
                _closed += value;
        }

        remove
        {
            lock(_lock)
                _closed -= value;
        }
    }

    private EventHandler? _faulted;
    public event EventHandler Faulted
    {
        add
        {
            lock(_lock)
                _faulted += value;
        }

        remove
        {
            lock(_lock)
                _faulted -= value;
        }
    }

    protected virtual void Dispose(bool disposing)
    {

    }

    protected void RaiseOpening()
    {
        OnOpening();

        EventHandler? opening;
        
        lock(_lock)
            opening = _opening;

        if (opening is not null)
            opening(this, EventArgs.Empty);
    }

    protected void RaiseOpened()
    {
        OnOpened();

        EventHandler? opened;
        
        lock(_lock)
            opened = _opened;

        if (opened is not null)
            opened(this, EventArgs.Empty);
    }

    protected void RaiseClosing()
    {
        OnClosing();

        EventHandler? handler;
        
        lock(_lock)
            handler = _closing;

        if (handler is not null)
            handler(this, EventArgs.Empty);
    }

    protected void RaiseClosed()
    {
        OnClosed();

        EventHandler? handler;
        
        lock(_lock)
            handler = _closed;

        if (handler is not null)
            handler(this, EventArgs.Empty);
    }

    protected void RaiseFaulted()
    {
        OnFaulted();

        EventHandler? handler;
        
        lock(_lock)
            handler = _faulted;

        if (handler is not null)
            handler(this, EventArgs.Empty);
    }

    protected void CheckFaulted( )
    {
        if (State == ChannelState.Faulted)
            throw new Exception("The channel is in faulted state");
    }

    protected void GoToFaultedState( )
    {
        lock (_lock)
        {
            if (_state == ChannelState.Faulted) return;
            _state = ChannelState.Faulted;
        }

        RaiseFaulted();
    }

    protected async Task DelayedOpenAsync(WaitHandle waitHandle, CancellationToken cancellationToken)
    {
        if (!await waitHandle.WaitOneAsync(cancellationToken))
            throw new TaskCanceledException();

        await InternalOpenAsync(cancellationToken);
    }

    protected abstract Task InternalOpenAsync(CancellationToken cancellationToken);

    protected abstract Task InternalCloseAsync(CancellationToken cancellationToken);

    protected abstract Task<IPacket> InternalReceiveAsync(CancellationToken cancellationToken);

    protected abstract Task<IPacket> InternalSendAsync(IPacket packet, CancellationToken cancellationToken);

    protected virtual void OnOpening()
    {
    }

    protected virtual void OnOpened()
    {
    }

    protected virtual void OnClosing()
    {
    }

    protected virtual void OnClosed()
    {
    }

    protected virtual void OnFaulted()
    {
    }
}