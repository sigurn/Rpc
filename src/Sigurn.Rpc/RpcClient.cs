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

    private readonly Func<ISessionInitializer, CancellationToken, Task>? _sessionInitializeHandler;

    /// <summary>
    /// Initializes a new instance of <see cref="RpcClient"/> with the specified channel factories.
    /// </summary>
    /// <param name="channelFactories">The ordered sequence of channel factories tried during connection and reconnection.</param>
    public RpcClient(params Func<CancellationToken, Task<IChannel>>[] channelFactories)
        : this(null, channelFactories)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="RpcClient"/> with a session-initialize hook and the specified channel factories.
    /// </summary>
    /// <param name="sessionInitializeHandler">
    /// A hook invoked after the transport (re)connects but before session state is restored and before the
    /// connection is announced as ready. It runs on every (re)open with full RPC available (see
    /// <see cref="ISessionInitializer"/>); if it throws, the open is treated as a failed connect and retried.
    /// Pass <see langword="null"/> (e.g. for TLS/cert flows) to skip it.
    /// </param>
    /// <param name="channelFactories">The ordered sequence of channel factories tried during connection and reconnection.</param>
    public RpcClient(Func<ISessionInitializer, CancellationToken, Task>? sessionInitializeHandler, params Func<CancellationToken, Task<IChannel>>[] channelFactories)
    {
        ArgumentNullException.ThrowIfNull(channelFactories);

        _sessionInitializeHandler = sessionInitializeHandler;
        _channel = new RestorableChannel(channelFactories);
        _session = new Session(_channel);
        _channel.SessionInitializeAsync = RunSessionInitializeAsync;
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
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
        await _session.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the current state of the underlying channel.
    /// </summary>
    public ChannelState State => _channel.State;

    /// <summary>
    /// Gets or sets the timeout applied to each RPC call. Defaults to 15 seconds.
    /// Use <see cref="Timeout.InfiniteTimeSpan"/> to disable the timeout.
    /// </summary>
    public TimeSpan AnswerTimeout
    {
        get => _session.Rpc.AnswerTimeout;
        set => _session.Rpc.AnswerTimeout = value;
    }

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
            await Task.WhenAll(tasks).ConfigureAwait(false);
            cts.Dispose();
        }

        await _channel.CloseAsync(cancellationToken).ConfigureAwait(false);
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

    // Invoked by the restorable channel after the transport (re)connects but before Opened is raised. Runs
    // the user's session-initialize hook (releasing every proxy it obtained, on success and failure alike),
    // then re-establishes the session before returning. Because this whole callback is awaited ahead of
    // Opened, the connection is announced only once instances and subscriptions have been restored, so app
    // code reacting to Opened sees a ready session. A failing hook rethrows to abort the open (the channel
    // retries) and, being before restore, keeps restore gated behind hook success.
    private async Task RunSessionInitializeAsync(IChannel channel, CancellationToken cancellationToken)
    {
        var handler = _sessionInitializeHandler;
        if (handler is not null)
        {
            var context = new SessionInitializer(_session, channel);
            try
            {
                await handler(context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                context.ReleaseAll();
            }
        }

        await _session.RestoreAfterReopenAsync(cancellationToken).ConfigureAwait(false);
    }

    // Session-initialize hook context. Proxies obtained via GetService are transient (never restorable) and
    // tracked so they can be released when the hook finishes.
    private sealed class SessionInitializer : ISessionInitializer
    {
        private readonly Session _session;
        private readonly List<IDisposable> _acquired = [];

        public SessionInitializer(Session session, IChannel channel)
        {
            _session = session;
            Channel = channel;
        }

        public IChannel Channel { get; }

        public async Task<T> GetService<T>(CancellationToken cancellationToken) where T : class
        {
            var proxy = await _session.CreateProxy<T>(cancellationToken, restorable: false).ConfigureAwait(false);
            if (proxy is IDisposable disposable)
                lock (_acquired)
                    _acquired.Add(disposable);
            return proxy;
        }

        public void ReleaseAll()
        {
            IDisposable[] items;
            lock (_acquired)
            {
                items = [.. _acquired];
                _acquired.Clear();
            }

            foreach (var disposable in items)
            {
                try { disposable.Dispose(); }
                catch { /* best effort: releasing a transient hook proxy must not abort the open */ }
            }
        }
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