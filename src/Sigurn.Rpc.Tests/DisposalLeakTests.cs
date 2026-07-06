using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Tests;

/// <summary>
/// Regression tests for resource leaks — objects whose Dispose could be skipped on failure/teardown paths.
/// </summary>
public class DisposalLeakTests
{
    private static CancellationToken CurrentToken => TestContext.Current.CancellationToken;

    // Minimal IChannel double that records Close/Dispose and can raise Faulted.
    private sealed class RecordingChannel : IChannel, IDisposable
    {
        public int CloseCount;
        public int DisposeCount;

        public ChannelState State { get; private set; } = ChannelState.Created;
        public object? BoundObject { get; set; }

#pragma warning disable CS0067 // interface-mandated events this test double never raises
        public event EventHandler? Opening;
        public event EventHandler? Opened;
        public event EventHandler? Closing;
        public event EventHandler? Closed;
        public event EventHandler? Faulted;
#pragma warning restore CS0067

        public Task OpenAsync(CancellationToken cancellationToken)
        {
            State = ChannelState.Opened;
            Opened?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CloseCount);
            State = ChannelState.Closed;
            Closed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task<IPacket> SendAsync(IPacket packet, CancellationToken cancellationToken) => Task.FromResult(packet);

        // Never completes on its own — the receive loop parks here until cancelled.
        public Task<IPacket> ReceiveAsync(CancellationToken cancellationToken)
            => Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => (IPacket)null!, TaskContinuationOptions.ExecuteSynchronously);

        public void RaiseFaulted()
        {
            State = ChannelState.Faulted;
            Faulted?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }

    private sealed class TestAcceptor : BaseAcceptor
    {
        private readonly IChannel _channel;
        public TestAcceptor(IChannel channel) : base(null) => _channel = channel;
        protected override Task<IChannel?> Accept(CancellationToken cancellationToken) => Task.FromResult<IChannel?>(_channel);
        protected override Task InternalDispose() => Task.CompletedTask;
    }

    private sealed class ThrowingHost : IChannelHost
    {
        public bool IsOpened => false;
        public void Open() => throw new InvalidOperationException("cannot open host");
        public void Close() { }
#pragma warning disable CS0067 // interface-mandated events this test double never raises
        public event EventHandler<ChannelEventArgs>? Connected;
        public event EventHandler<ChannelEventArgs>? Disconnected;
#pragma warning restore CS0067
    }

    private static T? GetField<T>(object target, string name)
        => (T?)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);

    // #4 — RestorableChannel.Dispose must close/dispose the inner channel.
    [Fact(Timeout = 15000)]
    public async Task RestorableChannel_Dispose_ClosesInnerChannel()
    {
        var recording = new RecordingChannel();
        var channel = new RestorableChannel(_ => Task.FromResult<IChannel>(recording));
        channel.AutoReopen = false;

        await channel.OpenAsync(CurrentToken);
        channel.Dispose();

        Assert.True(recording.CloseCount > 0, "Inner channel must be closed when the RestorableChannel is disposed.");
        Assert.True(recording.DisposeCount > 0, "Inner channel must be disposed when the RestorableChannel is disposed.");
    }

    // #5 — On reconnect the old faulted channel must be disposed and its handlers detached.
    [Fact(Timeout = 15000)]
    public async Task RestorableChannel_Reopen_DisposesOldChannel()
    {
        var first = new RecordingChannel();
        var second = new RecordingChannel();
        var queue = new Queue<RecordingChannel>([first, second]);

        var factoryCalls = 0;
        var channel = new RestorableChannel(_ =>
        {
            Interlocked.Increment(ref factoryCalls);
            return Task.FromResult<IChannel>(queue.Dequeue());
        });
        channel.AutoReopen = true;

        await channel.OpenAsync(CurrentToken);

        // Fault the current inner channel -> triggers reconnect to `second`.
        first.RaiseFaulted();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (first.DisposeCount == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20, CurrentToken);

        // The old faulted channel must be closed and disposed rather than dropped.
        Assert.True(first.CloseCount > 0, "The old faulted channel must be closed after reconnect.");
        Assert.True(first.DisposeCount > 0, "The old faulted channel must be disposed after reconnect.");
        Assert.Equal(2, factoryCalls); // one initial connect + one reconnect

        // The RestorableChannel must have detached its fault handler from the old channel:
        // faulting it again must not trigger another reconnect.
        first.RaiseFaulted();
        await Task.Delay(200, CurrentToken);
        Assert.Equal(2, factoryCalls);

        await channel.CloseAsync(CurrentToken);
    }

    // #8 — BaseAcceptor must dispose the accepted channel if the validator throws.
    [Fact(Timeout = 15000)]
    public async Task BaseAcceptor_ValidatorThrows_DisposesAcceptedChannel()
    {
        var recording = new RecordingChannel();
        var acceptor = new TestAcceptor(recording);
        acceptor.SetChannelValidator((ch, ct) => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => acceptor.AcceptAsync(CurrentToken));

        Assert.True(recording.DisposeCount > 0, "Accepted channel must be disposed when the validator throws.");
    }

    // #7 — ServiceHost.Start must not leak _cts/_runTask if the host fails to open.
    [Fact(Timeout = 15000)]
    public void ServiceHost_StartHostOpenThrows_DoesNotLeakCts()
    {
        var host = new ThrowingHost();
        var serviceHost = new ServiceHost(host);

        Assert.Throws<InvalidOperationException>(() => serviceHost.Start());

        var cts = GetField<CancellationTokenSource>(serviceHost, "_cts");
        Assert.Null(cts); // disposed and cleared, not left dangling
    }

    // #3 — RpcClient must dispose its Session on disposal.
    [Fact(Timeout = 15000)]
    public async Task RpcClient_Dispose_DisposesSession()
    {
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var serviceHost = new ServiceHost(host);
        serviceHost.Start();

        var client = new RpcClient(async ct =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(ct);
            return channel;
        });
        await client.OpenAsync(CurrentToken);

        await client.DisposeAsync();

        var session = GetField<object>(client, "_session")!;
        var isDisposed = GetField<int>(session, "_isDisposed");
        Assert.NotEqual(0, isDisposed);

        serviceHost.Stop();
    }
}
