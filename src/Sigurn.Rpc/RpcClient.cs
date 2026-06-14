using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

/// <summary>
/// Provides client-side RPC functionality over a restorable channel with automatic reconnection support.
/// </summary>
public sealed class RpcClient : IDisposable, IAsyncDisposable
{
    private readonly RestorableChannel _channel;

    private readonly List<Task> _tasks = [];
    private CancellationTokenSource? _cts;

    private readonly Session _session;

    /// <summary>
    /// Initializes a new instance of <see cref="RpcClient"/> with the specified channel factories.
    /// </summary>
    /// <param name="channelFactories">The ordered sequence of channel factories tried during connection and reconnection.</param>
    public RpcClient(params Func<CancellationToken, Task<IChannel>>[] channelFactories)
    {
        ArgumentNullException.ThrowIfNull(channelFactories);

        _channel = new RestorableChannel(channelFactories);
        _session = new Session(_channel);
    }

    /// <summary>
    /// Closes the client and releases all resources synchronously.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().Wait();
    }

    /// <summary>
    /// Closes the client and releases all resources asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None);
    }

    /// <summary>
    /// Gets the current state of the underlying channel.
    /// </summary>
    public ChannelState State => _channel.State;

    /// <summary>
    /// Gets or sets a value indicating whether the client should automatically reconnect after a fault.
    /// </summary>
    public bool AutoReopen
    {
        get => _channel.AutoReopen;
        set => _channel.AutoReopen = value;
    }

    /// <summary>
    /// Gets or sets the interval between reconnection attempts.
    /// </summary>
    public TimeSpan ReopenInterval
    {
        get => _channel.ReopenInterval;
        set => _channel.ReopenInterval = value;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the factory iterator resets to the beginning after a successful connection.
    /// </summary>
    public bool ResetOnSuccess
    {
        get => _channel.ResetOnSuccess;
        set => _channel.ResetOnSuccess = value;
    }

    /// <summary>
    /// Opens the client channel asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
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

    /// <summary>
    /// Closes the client channel asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        Task[] tasks;
        CancellationTokenSource? cts;

        lock (_tasks)
        {
            tasks = [.. _tasks];
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

    /// <summary>
    /// Gets a proxy for the remote service of the specified interface type.
    /// </summary>
    /// <typeparam name="T">The interface type of the remote service.</typeparam>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A proxy that implements <typeparamref name="T"/> and forwards calls to the remote service.</returns>
    public Task<T> GetService<T>(CancellationToken cancellationToken) where T : class
    {
        return _session.CreateProxy<T>(cancellationToken);
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

    public Task WaitForState(ChannelState state, CancellationToken cancellationToken)
    {
        return _channel.WaitForStateAsync(state, cancellationToken);
    }
}