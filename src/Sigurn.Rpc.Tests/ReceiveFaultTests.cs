using System.IO;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Tests;

/// <summary>
/// Covers the "idle client wedges in a receive-spin and never reconnects" bug: a transport failure
/// surfaced only through the receive (or send) path must fault the channel so the reconnect machinery
/// engages, and the RPC handler must not busy-spin when receive keeps failing.
/// </summary>
public class ReceiveFaultTests
{
    private static CancellationToken CurrentToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// A <see cref="BaseChannel"/> whose receive/send behaviour is supplied by the test.
    /// </summary>
    private sealed class TestChannel : BaseChannel
    {
        public Func<CancellationToken, Task<IPacket>> OnReceive { get; set; } = async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return IPacket.Create([]);
        };

        public Func<IPacket, CancellationToken, Task<IPacket>> OnSend { get; set; } =
            (p, ct) => Task.FromResult(p);

        protected override Task InternalOpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override Task InternalCloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override Task<IPacket> InternalReceiveAsync(CancellationToken cancellationToken)
            => OnReceive(cancellationToken);

        protected override Task<IPacket> InternalSendAsync(IPacket packet, CancellationToken cancellationToken)
            => OnSend(packet, cancellationToken);
    }

    [Fact(Timeout = 15000)]
    public async Task ReceiveFailureFaultsTheChannel()
    {
        await using var channel = new TestChannel
        {
            OnReceive = async _ =>
            {
                await Task.Yield();
                throw new IOException("Unable to read data from the transport connection: Connection reset by peer");
            }
        };

        var log = new List<string>();
        channel.Faulted += (s, e) => log.AddWithLock("Faulted");

        await channel.OpenAsync(CurrentToken);
        Assert.Equal(ChannelState.Opened, channel.State);

        await Assert.ThrowsAsync<IOException>(() => channel.ReceiveAsync(CurrentToken));

        Assert.Equal(ChannelState.Faulted, channel.State);
        Assert.Equal<IEnumerable<string>>(["Faulted"], log.ToImmutableArrayWithLock());
    }

    [Fact(Timeout = 15000)]
    public async Task SendFailureFaultsTheChannel()
    {
        await using var channel = new TestChannel
        {
            OnSend = async (p, ct) =>
            {
                await Task.Yield();
                throw new IOException("Unable to write data to the transport connection: Broken pipe");
            }
        };

        var log = new List<string>();
        channel.Faulted += (s, e) => log.AddWithLock("Faulted");

        await channel.OpenAsync(CurrentToken);
        Assert.Equal(ChannelState.Opened, channel.State);

        await Assert.ThrowsAsync<IOException>(() => channel.SendAsync(IPacket.Create([1, 2, 3]), CurrentToken));

        Assert.Equal(ChannelState.Faulted, channel.State);
        Assert.Equal<IEnumerable<string>>(["Faulted"], log.ToImmutableArrayWithLock());
    }

    [Fact(Timeout = 15000)]
    public async Task ReceiveCancellationDoesNotFaultTheChannel()
    {
        await using var channel = new TestChannel();

        var log = new List<string>();
        channel.Faulted += (s, e) => log.AddWithLock("Faulted");

        await channel.OpenAsync(CurrentToken);

        using var cts = new CancellationTokenSource();
        var receiveTask = channel.ReceiveAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiveTask);

        Assert.Equal(ChannelState.Opened, channel.State);
        Assert.Empty(log.ToImmutableArrayWithLock());
    }

    [Fact(Timeout = 15000)]
    public async Task CloseCancellingAnActiveReceiveDoesNotFaultTheChannel()
    {
        await using var channel = new TestChannel();

        var log = new List<string>();
        channel.Faulted += (s, e) => log.AddWithLock("Faulted");

        await channel.OpenAsync(CurrentToken);

        var receiveTask = channel.ReceiveAsync(CurrentToken);
        await channel.CloseAsync(CurrentToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiveTask);

        Assert.Equal(ChannelState.Closed, channel.State);
        Assert.Empty(log.ToImmutableArrayWithLock());
    }

    [Fact(Timeout = 15000)]
    public async Task RestorableChannelReopensAfterReceiveFailure()
    {
        var created = new List<TestChannel>();
        using var reopenedEvent = new ManualResetEvent(false);

        var channel = new RestorableChannel(ct =>
        {
            var inner = new TestChannel();
            if (created.Count == 0)
            {
                // Only the first connection dies, and it dies through the receive path alone:
                // no concurrent send, no close, exactly the wedged idle-client scenario.
                inner.OnReceive = async _ =>
                {
                    await Task.Yield();
                    throw new IOException("Connection reset by peer");
                };
            }

            created.AddWithLock(inner);
            return Task.FromResult<IChannel>(inner);
        });
        channel.AutoReopen = true;
        channel.ReopenInterval = TimeSpan.FromSeconds(1);

        var openedCount = 0;
        channel.Opened += (s, e) =>
        {
            if (Interlocked.Increment(ref openedCount) > 1)
                reopenedEvent.Set();
        };

        await channel.OpenAsync(CurrentToken);
        Assert.Equal(ChannelState.Opened, channel.State);

        await Assert.ThrowsAsync<IOException>(() => channel.ReceiveAsync(CurrentToken));

        Assert.True(await reopenedEvent.WaitOneAsync(TimeSpan.FromSeconds(5), CurrentToken),
            "The restorable channel did not reopen after a receive-detected connection failure");
        Assert.Equal(2, created.ToArrayWithLock().Length);
        Assert.Equal(ChannelState.Opened, channel.State);

        await channel.CloseAsync(CurrentToken);
    }

    /// <summary>
    /// A channel which always fails the receive and, unlike a well behaved channel, stays
    /// <see cref="ChannelState.Opened"/> — so the handler loop cannot rely on the state to park.
    /// </summary>
    private sealed class AlwaysFailingReceiveChannel : IChannel
    {
        private int _receiveCount;

        public int ReceiveCount => Volatile.Read(ref _receiveCount);

        public ChannelState State { get; set; } = ChannelState.Opened;

        public object? BoundObject { get; set; }

        public Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            State = ChannelState.Closed;
            return Task.CompletedTask;
        }

        public Task<IPacket> SendAsync(IPacket packet, CancellationToken cancellationToken) => Task.FromResult(packet);

        public Task<IPacket> ReceiveAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _receiveCount);
            return Task.FromException<IPacket>(new IOException("Connection reset by peer"));
        }

#pragma warning disable CS0067 // The event is never used
        public event EventHandler? Opening;
        public event EventHandler? Opened;
        public event EventHandler? Closing;
        public event EventHandler? Closed;
        public event EventHandler? Faulted;
#pragma warning restore CS0067
    }

    [Fact(Timeout = 15000)]
    public async Task HandlerDoesNotBusySpinOnPersistentReceiveFailure()
    {
        var channel = new AlwaysFailingReceiveChannel();
        using var handler = new RpcHandler(channel);

        await Task.Delay(TimeSpan.FromSeconds(1), CurrentToken);

        // Without a backoff this loop runs hundreds of iterations per second on a dead socket.
        Assert.InRange(channel.ReceiveCount, 1, 30);
    }
}
