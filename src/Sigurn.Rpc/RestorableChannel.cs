using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

public class RestorableChannel : IChannel
{
    private readonly object _lock = new ();
    private readonly IEnumerable<Func<CancellationToken, Task<IChannel>>> _channelFactories;

    private IEnumerator<Func<CancellationToken, Task<IChannel>>> _factories;
    private IChannel? _channel;
    private CancellationTokenSource? _cancellationSource;
    private bool _isReopening = false;
    private Task? _reopeningTask = null;

    public RestorableChannel(params Func<CancellationToken, Task<IChannel>>[] channelFactories)
    {
        ArgumentNullException.ThrowIfNull(channelFactories);

        _channelFactories = channelFactories;

        _factories = _channelFactories.GetEnumerator();
        if (!_factories.MoveNext())
            throw new ArgumentException("Cannot get a factory from provided channel factories", nameof(channelFactories));
    }

    public RestorableChannel(IEnumerable<Func<CancellationToken, Task<IChannel>>> channelFactories)
    {
        ArgumentNullException.ThrowIfNull(channelFactories);

        _channelFactories = channelFactories;

        _factories = _channelFactories.GetEnumerator();
        if (!_factories.MoveNext())
            throw new ArgumentException("Cannot get a factory from provided channel factories", nameof(channelFactories));
    }

    private bool _autoReopen = true;
    public bool AutoReopen
    {
        get
        {
            lock(_lock)
                return _autoReopen;
        }

        set
        {
            lock(_lock)
                _autoReopen = value;
        }
    }

    private TimeSpan _reopenInterval = TimeSpan.FromSeconds(5);
    public TimeSpan ReopenInterval
    {
        get
        {
            lock(_lock)
                return _reopenInterval;
        }

        set
        {
            if (value < TimeSpan.FromSeconds(1))
                throw new ArgumentOutOfRangeException("Reopen interval cannot be less than 1 second.");

            lock(_lock)
                _reopenInterval = value;
        }
    }

    private bool _resetOnSuccess;
    public bool ResetOnSuccess
    {
        get
        {
            lock(_lock)
                return _resetOnSuccess;
        }

        set
        {
            lock(_lock)
                _resetOnSuccess = value;
        }
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

    private CancellationTokenSource? _openCancellationSource;
    private Task? _openTask;
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        using ManualResetEvent startEvent = new ManualResetEvent(false);
        Task<IChannel> task;

        lock(_lock)
        {
            if (_state == ChannelState.Closing)
                throw new InvalidOperationException($"Cannot open the client due to invalid state '{_state}'");

            if (_state == ChannelState.Opened || _state == ChannelState.Opening)
                return;

            _factories = _channelFactories.GetEnumerator();
            if (!_factories.MoveNext())
                throw new Exception("There are no factories to open the client");

            if (_openCancellationSource is not null)
                _openCancellationSource.Dispose();

            _openCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            task = DelayedConnectToServerAsync(startEvent, _factories, _openCancellationSource.Token);
            _openTask = task;

            _state = ChannelState.Opening;
        }

        RaiseOpening();

        try
        {
            startEvent.Set();
            var channel = await task;

            lock(_lock)
            {
                _openTask = null;
                if (_openCancellationSource is not null)
                {
                    _openCancellationSource.Dispose();
                    _openCancellationSource = null;
                }

                _channel = new QueueChannel(channel);
                _cancellationSource = new CancellationTokenSource();
                _state = ChannelState.Opened;
            }

            RaiseOpened();
        }
        catch
        {
            lock(_lock)
            {
                _openTask = null;
                if (_openCancellationSource is not null)
                {
                    _openCancellationSource.Dispose();
                    _openCancellationSource = null;
                }
            }
            GoToFaultedState();
            throw;
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? openCancellationToken = null;
        Task? openTask = null;
        lock(_lock)
        {
            if (_state == ChannelState.Closed || _state == ChannelState.Closing) return;

            if (_state == ChannelState.Opening && !_isReopening)
            {
                openCancellationToken = _openCancellationSource;
                openTask = _openTask;
            }
        }

        if (openCancellationToken is not null)
        {
            openCancellationToken.Cancel();
            if (openTask is not null)
                await openTask;
        }

        IChannel? channel;
        CancellationTokenSource? cancellationTokenSource;
        Task? reopeningTask;

        lock(_lock)
        {
            if (_state == ChannelState.Closed || _state == ChannelState.Closing) return;

            channel = _channel;
            _channel = null;

            cancellationTokenSource = _cancellationSource;
            _cancellationSource = null;

            reopeningTask = _reopeningTask;
            _reopeningTask = null;

            _state = ChannelState.Closing;
        }

        RaiseClosing();

        try
        {
            if (cancellationTokenSource is not null)
                cancellationTokenSource.Cancel();

            if (reopeningTask is not null)
                await reopeningTask;

            if (channel is not null)
            {
                await channel.CloseAsync(cancellationToken);
                if (channel is IDisposable d) d.Dispose();
                channel = null;
            }

            lock(_lock)
                _state = ChannelState.Closed;

            RaiseClosed();
        }
        catch
        {
            GoToFaultedState();
            throw;
        }
    }

    public Task<IPacket> ReceiveAsync(CancellationToken cancellationToken)
    {
        IChannel? channel;
        lock(_lock)
        {
            if (_state != ChannelState.Opened)
                throw new InvalidOperationException("Channel is not opened");
            channel = _channel;
        }

        if (channel is null)
            throw new InvalidOperationException("There is no connection");

        return channel.ReceiveAsync(cancellationToken);
    }

    public async Task<IPacket> SendAsync(IPacket packet, CancellationToken cancellationToken)
    {
        IChannel? channel;
        lock(_lock)
        {
            if (_state != ChannelState.Opened)
                throw new InvalidOperationException("Channel is not opened");
            channel = _channel;
        }

        if (channel is null)
            throw new InvalidOperationException("There is no connection");

        return await channel.SendAsync(packet, cancellationToken);
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
        EventHandler? opening;
        
        lock(_lock)
            opening = _opening;

        if (opening is not null)
            opening(this, EventArgs.Empty);
    }

    protected void RaiseOpened()
    {
        EventHandler? opened;
        
        lock(_lock)
            opened = _opened;

        if (opened is not null)
            opened(this, EventArgs.Empty);
    }

    protected void RaiseClosing()
    {
        EventHandler? handler;
        
        lock(_lock)
            handler = _closing;

        if (handler is not null)
            handler(this, EventArgs.Empty);
    }

    protected void RaiseClosed()
    {
        EventHandler? handler;
        
        lock(_lock)
            handler = _closed;

        if (handler is not null)
            handler(this, EventArgs.Empty);
    }

    protected void RaiseFaulted()
    {
        EventHandler? handler;
        
        lock(_lock)
            handler = _faulted;

        if (handler is not null)
            handler(this, EventArgs.Empty);
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


    private IEnumerator<Func<CancellationToken, Task<IChannel>>> GetFactories()
    {
        lock(_lock)
        {
            if (_resetOnSuccess)
            {
                _factories = _channelFactories.GetEnumerator();
                if (!_factories.MoveNext())
                    throw new Exception("Cannot get any channel factory");
            }

            return _factories;
        }
    }

    private async Task<IChannel> DelayedConnectToServerAsync(WaitHandle waitHandle, IEnumerator<Func<CancellationToken, Task<IChannel>>> factories, CancellationToken cancellationToken)
    {
        if (!await waitHandle.WaitOneAsync(cancellationToken))
            throw new TaskCanceledException();

        return await ConnectToServerAsync(factories, cancellationToken);
    }

    private const string _cannotConnectMessage = "None of the channel factories was able to get connected to the server";
    private async Task<IChannel> ConnectToServerAsync (IEnumerator<Func<CancellationToken, Task<IChannel>>> factories, CancellationToken cancellationToken)
    {
        List<Exception> exceptions = [];
        do
        {
            try
            {
                if (factories.Current is null) continue;
                var channel = await factories.Current(cancellationToken);
                if (channel is null) continue;

                channel.Faulted += ChannelFaultedHandler;
                channel.Closed += ChannelClosedHandler;
                return channel;
            }
            catch(Exception ex)
            {
                exceptions.Add(ex);
            }
        }
        while (factories.MoveNext());

        if (exceptions.Count == 0)
            throw new Exception(_cannotConnectMessage);

        throw new AggregateException(_cannotConnectMessage, exceptions);
    }

    private void ChannelFaultedHandler(object? sender, EventArgs args)
    {
        GoToFaultedState();

        if (!AutoReopen) return;

        lock(_lock)
        {
            if (_state == ChannelState.Closing) return;
            if (_cancellationSource is null) return;
            if (_reopeningTask is not null) return;

            _reopeningTask = ReopenChannelAsync(_cancellationSource.Token);
        }
    }

    private void ChannelClosedHandler(object? sender, EventArgs args)
    {

    }

    private async Task ReopenChannelAsync(CancellationToken cancellationToken)
    {
        try
        {
            IEnumerator<Func<CancellationToken, Task<IChannel>>> factories = GetFactories();

            lock(_lock)
            {
                _isReopening = true;
                _state = ChannelState.Opening;
            }

            RaiseOpening();

            while(!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var channel = await ConnectToServerAsync(factories, cancellationToken);

                    lock(_lock)
                    {
                        _factories = factories;
                        _channel = new QueueChannel(channel);
                    }

                    RaiseOpened();
                    return;
                }
                catch(TaskCanceledException)
                {
                    return;
                }
                catch
                {
                    TimeSpan interval;
                    lock(_lock)
                    {
                        factories = _channelFactories.GetEnumerator();
                        if (!factories.MoveNext()) return;
                        interval = _reopenInterval;
                    }

                    await Task.Delay(interval, cancellationToken);
                }
            }
        }
        catch
        {
            GoToFaultedState();
            return;
        }
        finally
        {
            lock(_lock)
            {
                _isReopening = false;
                _reopeningTask = null;
            }
        }
    }
}