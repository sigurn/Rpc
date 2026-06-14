using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

static class TaskHelpers
{
    public static Task WaitNoThrow(this Task task)
    {
        return task.ContinueWith(_ => { });
    }
}

/// <summary>
/// Provides a base implementation for communication channels with thread-safe state management.
/// </summary>
public abstract class BaseChannel : IChannel, IDisposable, IAsyncDisposable
{
    private static readonly ILogger<BaseChannel> _logger = RpcLogging.CreateLogger<BaseChannel>();

    protected readonly object _lock = new();
    private volatile bool _isDisposed = false;

    private CancellationTokenSource? _receiveCancellationSource = null;
    private Task<IPacket>? _receiveTask = null;

    private CancellationTokenSource? _sendCancellationSource = null;
    private Task<IPacket>? _sendTask = null;

    private CancellationTokenSource? _openCancellationSource;
    private Task? _openTask;

    protected BaseChannel()
    {
    }

    /// <summary>
    /// Releases resources used by the channel.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    private ChannelState _state;
    /// <summary>
    /// Gets or sets the current state of the channel.
    /// </summary>
    public ChannelState State
    {
        get
        {
            lock (_lock)
                return _state;
        }

        set
        {
            lock (_lock)
                _state = value;
        }
    }

    private object? _boundObject = null;
    /// <summary>
    /// An object associated with the channel.
    /// </summary>
    /// <remarks>Developer can associate any object with the channel for any purposes.
    /// This object is not processed by channel in any way.</remarks>
    public object? BoundObject
    {
        get
        {
            lock (_lock)
                return _boundObject;
        }

        set
        {
            lock (_lock)
                _boundObject = value;
        }
    }

    /// <summary>
    /// Closes the communication channel asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        using var _ = _logger.Scope();
        Task? task = null;

        lock (_lock)
        {
            if (_state == ChannelState.Closed || _state == ChannelState.Closing)
                return;

            if (_state == ChannelState.Opening)
            {
                if (_openCancellationSource is null || _openTask is null)
                    throw new InvalidOperationException("The channel is in opening state. Cannot close it now");

                task = _openTask;
                _openCancellationSource.Cancel();
            }
        }

        if (task is not null)
        {
            try
            {
                _logger.LogTrace("Wait for opening task completion");
                await task;
            }
            catch
            {
            }
        }

        lock (_lock)
        {
            if (_state == ChannelState.Closed || _state == ChannelState.Closing)
                return;

            _state = ChannelState.Closing;
        }

        try
        {
            RaiseClosing();

            Task? receiveTask;
            Task? sendTask;

            lock (_lock)
            {
                receiveTask = _receiveTask;
                if (_receiveCancellationSource is not null && !_receiveCancellationSource.IsCancellationRequested)
                    _receiveCancellationSource.Cancel();

                sendTask = _sendTask;
                if (_sendCancellationSource is not null && !_sendCancellationSource.IsCancellationRequested)
                    _sendCancellationSource.Cancel();
            }

            if (receiveTask is not null)
            {
                _logger.LogTrace("Wait for receiving task completion");
                await receiveTask.WaitNoThrow();
            }

            if (sendTask is not null)
            {
                _logger.LogTrace("Wait for sending task completion");
                await sendTask.WaitNoThrow();
            }

            await InternalCloseAsync(cancellationToken);

            lock (_lock)
                _state = ChannelState.Closed;

            RaiseClosed();
        }
        catch
        {
            GoToFaultedState();
            throw;
        }
    }

    /// <summary>
    /// Opens the communication channel asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        using var _ = _logger.Scope();

        CheckFaulted();

        using ManualResetEvent openEvent = new ManualResetEvent(false);
        Task? task = null;
        CancellationTokenSource? openCancellationSource = null;

        try
        {
            lock (_lock)
            {
                if (_state == ChannelState.Opened || _state == ChannelState.Opening)
                    return;

                openCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _openCancellationSource = openCancellationSource;
                task = DelayedOpenAsync(openEvent, _openCancellationSource.Token);
                _openTask = task;

                _state = ChannelState.Opening;
            }

            RaiseOpening();

            openEvent.Set();
            await task;

            lock (_lock)
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
            lock (_lock)
            {
                _openTask = null;
                _openCancellationSource = null;
            }

            task?.Dispose();
            openCancellationSource?.Dispose();
        }
    }

    /// <summary>
    /// Receives a packet from the channel asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The received packet.</returns>
    public async Task<IPacket> ReceiveAsync(CancellationToken cancellationToken)
    {
        Task<IPacket> task;
        lock (_lock)
        {
            if (_state != ChannelState.Opened)
                throw new InvalidOperationException("The channel is not opened.");

            if (_receiveTask is not null)
                throw new InvalidOperationException("The receive operation is already running. Cannot run concurrent receive operations.");

            _receiveCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            task = InternalReceiveAsync(_receiveCancellationSource.Token);
            _receiveTask = task;
        }

        try
        {
            return await task;
        }
        finally
        {
            lock (_lock)
            {
                _receiveTask = null;
                _receiveCancellationSource?.Dispose();
                _receiveCancellationSource = null;
            }            
        }
    }

    /// <summary>
    /// Sends a packet through the channel asynchronously.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The packet that was sent.</returns>
    public async Task<IPacket> SendAsync(IPacket packet, CancellationToken cancellationToken)
    {
        Task<IPacket> task;
        lock (_lock)
        {
            if (_state != ChannelState.Opened)
                throw new InvalidOperationException("The channel is not opened.");

            if (_sendTask is not null)
                throw new InvalidOperationException("The send operation is already running. Cannot run concurrent send operations.");

            _sendCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            task = InternalSendAsync(packet, _sendCancellationSource.Token);
            _sendTask = task;
        }

        try
        {
            return await task;
        }
        finally
        {
            lock (_lock)
            {
                _sendTask = null;
                _sendCancellationSource?.Dispose();
                _sendCancellationSource = null;
            }            
        }
    }

    private EventHandler? _opening;
    /// <summary>
    /// Occures when the channel is opening.
    /// </summary>
    public event EventHandler Opening
    {
        add
        {
            lock (_lock)
                _opening += value;
        }
        remove
        {
            lock (_lock)
                _opening -= value;
        }
    }

    private EventHandler? _opened;
    /// <summary>
    /// Occures when the channel is opened.
    /// </summary>
    public event EventHandler Opened
    {
        add
        {
            lock (_lock)
                _opened += value;
        }

        remove
        {
            lock (_lock)
                _opened -= value;
        }
    }

    private EventHandler? _closing;
    /// <summary>
    /// Occures when the channel is closing.
    /// </summary>
    public event EventHandler Closing
    {
        add
        {
            lock (_lock)
                _closing += value;
        }

        remove
        {
            lock (_lock)
                _closing -= value;
        }
    }

    private EventHandler? _closed;
    /// <summary>
    /// Occures when the channel is closed.
    /// </summary>
    public event EventHandler Closed
    {
        add
        {
            lock (_lock)
                _closed += value;
        }

        remove
        {
            lock (_lock)
                _closed -= value;
        }
    }

    private EventHandler? _faulted;
    /// <summary>
    /// Occures when the channel is in the faulted state.
    /// </summary>
    public event EventHandler Faulted
    {
        add
        {
            lock (_lock)
                _faulted += value;
        }

        remove
        {
            lock (_lock)
                _faulted -= value;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        DisposeAsync(disposing).AsTask().Wait();
    }

    protected async virtual ValueTask DisposeAsync(bool disposing)
    {
        if (!disposing) return;
        await CloseAsync(CancellationToken.None);
    }

    protected void RaiseOpening()
    {
        OnOpening();

        EventHandler? opening;

        lock (_lock)
            opening = _opening;

        if (opening is not null)
            opening(this, EventArgs.Empty);
    }

    protected void RaiseOpened()
    {
        OnOpened();

        EventHandler? opened;

        lock (_lock)
            opened = _opened;

        if (opened is not null)
            opened(this, EventArgs.Empty);
    }

    protected void RaiseClosing()
    {
        OnClosing();

        EventHandler? handler;

        lock (_lock)
            handler = _closing;

        if (handler is not null)
            handler(this, EventArgs.Empty);
    }

    protected void RaiseClosed()
    {
        OnClosed();

        EventHandler? handler;

        lock (_lock)
            handler = _closed;

        if (handler is not null)
            handler(this, EventArgs.Empty);
    }

    protected void RaiseFaulted()
    {
        OnFaulted();

        EventHandler? handler;

        lock (_lock)
            handler = _faulted;

        if (handler is not null)
            handler(this, EventArgs.Empty);
    }

    protected void CheckFaulted()
    {
        if (State == ChannelState.Faulted)
            throw new Exception("The channel is in faulted state");
    }

    protected void GoToFaultedState()
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