using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

public class RpcClient : IDisposable, IAsyncDisposable
{
    private readonly RestorableChannel _channel;

    private readonly List<Task> _tasks = new();
    private CancellationTokenSource? _cts;

    private readonly Session _session;


    public RpcClient(params Func<CancellationToken, Task<IChannel>>[] channelFactories)
    {
        ArgumentNullException.ThrowIfNull(channelFactories);

        _channel = new RestorableChannel(channelFactories);
        _session = new Session(_channel);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().Wait();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None);
    }
    
    public ChannelState State => _channel.State;

    public bool AutoReopen
    {
        get => _channel.AutoReopen;
        set => _channel.AutoReopen = value;
    }

    public TimeSpan ReopenInterval
    {
        get => _channel.ReopenInterval;
        set => _channel.ReopenInterval = value;
    }

    public bool ResetOnSuccess
    {
        get => _channel.ResetOnSuccess;
        set => _channel.ResetOnSuccess = value;
    }

    public Task OpenAsync(CancellationToken cancellationToken)
    {
        if (_channel.State != ChannelState.Closed && _channel.State != ChannelState.Created)
            return Task.CompletedTask;

        lock (_tasks)
        {
            if (_cts is null || _cts.IsCancellationRequested)
                _cts = new CancellationTokenSource();
        }

        return _channel.OpenAsync(cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        Task[] tasks;
        CancellationTokenSource? cts;

        lock (_tasks)
        {
            tasks = _tasks.ToArray();
            _tasks.Clear();
            cts = _cts;
            _cts = null;
        }

        if (cts is not null)
        {
            cts.Cancel();
            await Task.WhenAll(tasks);
            cts.Dispose();
        }

        await _channel.CloseAsync(cancellationToken);
    }

    public Task<T> GetService<T>(CancellationToken cancellationToken) where T : class
    {
        return _session.CreateProxy<T>(cancellationToken);
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
}