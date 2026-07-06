using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

/// <summary>
/// Hosts RPC services over a synchronous channel host. This is a thin synchronous
/// facade over <see cref="ServiceHostAsync"/>: incoming channels raised by the
/// <see cref="IChannelHost"/> are bridged into the asynchronous host, which owns
/// session lifecycle and disconnect handling. Reusing <see cref="ServiceHostAsync"/>
/// avoids disposing a channel synchronously from its own Faulted/Closed event
/// (which would self-deadlock on the receive loop).
/// </summary>
public class ServiceHost : IServiceHost
{
    private static readonly ILogger<ServiceHost> _logger = RpcLogging.CreateLogger<ServiceHost>();

    private readonly IChannelHost _host;
    private readonly ServiceHostAsync _inner = new();
    private readonly BridgeAcceptor _acceptor = new();

    private CancellationTokenSource? _cts;
    private Task? _runTask;

    /// <summary>
    /// Initializes a new instance of <see cref="ServiceHost"/> with the specified channel host.
    /// </summary>
    /// <param name="host">The channel host that provides connected channels.</param>
    public ServiceHost(IChannelHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        _host.Connected += OnHostConnected;
        _inner.RegisterAcceptor(() => _acceptor);
    }

    /// <summary>
    /// Starts the service host by opening the underlying channel host.
    /// </summary>
    public void Start()
    {
        using var _ = _logger.Scope();
        if (_host.IsOpened) return;

        _cts = new CancellationTokenSource();
        _runTask = _inner.RunAsync(_cts.Token);
        try
        {
            _host.Open();
        }
        catch
        {
            _cts.Cancel();
            try { _runTask.Wait(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) { _logger.LogDebug(ex, "Run task faulted after failed host open"); }
            _cts.Dispose();
            _cts = null;
            _runTask = null;
            throw;
        }
    }

    /// <summary>
    /// Stops the service host by closing the underlying channel host and all active sessions.
    /// </summary>
    public void Stop()
    {
        using var _ = _logger.Scope();
        if (!_host.IsOpened) return;

        _host.Close();

        _cts?.Cancel();
        try { _runTask?.Wait(); }
        catch (AggregateException ex) { _logger.LogDebug(ex, "Run task faulted during service host stop"); }

        _cts?.Dispose();
        _cts = null;
        _runTask = null;
    }

    /// <summary>
    /// Registers a service with the specified interface type, sharing scope, and factory.
    /// </summary>
    /// <typeparam name="T">The interface type of the service. Must be an interface.</typeparam>
    /// <param name="share">The scope within which service instances are shared.</param>
    /// <param name="factory">The factory used to create service instances.</param>
    public void RegisterSerive<T>(ShareWithin share, Func<T> factory) where T : class
        => _inner.RegisterSerive(share, factory);

    /// <summary>
    /// Gets or sets a value indicating whether the service catalog is published and discoverable by clients.
    /// </summary>
    public bool PublishServicesCatalog
    {
        get => _inner.PublishServicesCatalog;
        set => _inner.PublishServicesCatalog = value;
    }

    private void OnHostConnected(object? sender, ChannelEventArgs args)
    {
        if (args.Channel is null || args.Channel.State != ChannelState.Opened)
            return;

        _acceptor.Enqueue(args.Channel);
    }

    (ShareWithin Shared, Func<object> Factory) IServiceHost.GetServiceInfo(Type type)
        => ((IServiceHost)_inner).GetServiceInfo(type);

    Type? IServiceHost.FindTypeById(Guid id)
        => ((IServiceHost)_inner).FindTypeById(id);

    RefCounter<ICallTarget> IServiceHost.CreateHostInstance(Type type, Func<ICallTarget> factory)
        => ((IServiceHost)_inner).CreateHostInstance(type, factory);

    RefCounter<ICallTarget> IServiceHost.CreateGlobalInstance(Type type, Func<ICallTarget> factory)
        => ((IServiceHost)_inner).CreateGlobalInstance(type, factory);

    /// <summary>
    /// Bridges channels raised by a synchronous <see cref="IChannelHost"/> into the
    /// asynchronous accept loop of <see cref="ServiceHostAsync"/>.
    /// </summary>
    private sealed class BridgeAcceptor : IAsyncChannelAcceptor
    {
        private readonly Channel<IChannel> _queue = Channel.CreateUnbounded<IChannel>();

        public bool IsAccepting => true;

        public void Enqueue(IChannel channel) => _queue.Writer.TryWrite(channel);

        public async Task<IChannel?> AcceptAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _queue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogDebug(ex, "Accept cancelled");
                return null;
            }
            catch (ChannelClosedException ex)
            {
                _logger.LogDebug(ex, "Accept channel closed");
                return null;
            }
        }

        public Func<IChannel, CancellationToken, Task<bool>>? SetChannelValidator(Func<IChannel, CancellationToken, Task<bool>>? validator)
            => null;

        public ValueTask DisposeAsync()
        {
            // The acceptor is reused across Start/Stop cycles, so the queue must
            // outlive a single accept loop. The loop is stopped via cancellation.
            return ValueTask.CompletedTask;
        }
    }
}
