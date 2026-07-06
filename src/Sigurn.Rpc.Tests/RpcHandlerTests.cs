using System.Net;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.Infrastructure.Packets;

namespace Sigurn.Rpc.Tests;

public class RpcHandlerTests
{
    [Fact]
    public void CreateWithotChannelTest()
    {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        var ex = Assert.Throws<ArgumentNullException>(() => new RpcHandler(null));
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
        Assert.Equal("channel", ex.ParamName);
    }

    [Fact(Timeout = 15000)]
    public async Task SendWhenChannelIsClosedTest()
    {
        using var channel = new TcpChannel(new IPEndPoint(IPAddress.Loopback, 35768));
        using var handler = new RpcHandler(channel);

        CancellationTokenSource cts = new CancellationTokenSource();

        var packet = new SuccessPacket();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await handler.SendAsync(packet, cts.Token));
    }

    [Fact(Timeout = 15000)]
    public async Task CreateWithClosedChannelTest()
    {
        IChannel? serverChannel = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using ManualResetEvent connectEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverChannel = a.Channel;
            connectEvent.Set();
        };

        host.Open();

        using var channel = new TcpChannel(host.EndPoint);
        using var handler = new RpcHandler(channel);

        CancellationTokenSource cts = new CancellationTokenSource();
        await channel.OpenAsync(cts.Token);

        Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.NotNull(serverChannel);

        var packet = new SuccessPacket();
        await handler.SendAsync(packet, cts.Token);

        var serverPacket = await serverChannel.ReceiveAsync(cts.Token);
        Assert.NotNull(serverPacket);

        RpcPacket? request = await RpcPacket.FromPacketAsync(serverPacket, RpcPacket.DefaultSerializationContext, cts.Token);
        Assert.NotNull(request);

        var ssp = (SuccessPacket)request;
        Assert.Equal(packet.RequestId, ssp.RequestId);
    }

    [Fact(Timeout = 15000)]
    public async Task CreateWithOpenedChannelTest()
    {
        IChannel? serverChannel = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using ManualResetEvent connectEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverChannel = a.Channel;
            connectEvent.Set();
        };

        host.Open();

        using var channel = new TcpChannel(host.EndPoint);

        CancellationTokenSource cts = new CancellationTokenSource();
        await channel.OpenAsync(cts.Token);

        using var handler = new RpcHandler(channel);

        Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.NotNull(serverChannel);

        var packet = new SuccessPacket();
        await handler.SendAsync(packet, cts.Token);

        var serverPacket = await serverChannel.ReceiveAsync(cts.Token);
        Assert.NotNull(serverPacket);

        RpcPacket? request = await RpcPacket.FromPacketAsync(serverPacket, RpcPacket.DefaultSerializationContext, cts.Token);
        Assert.NotNull(request);

        var ssp = (SuccessPacket)request;
        Assert.Equal(packet.RequestId, ssp.RequestId);
    }

    [Fact(Timeout = 15000)]
    public async Task RequestTest()
    {
        IChannel? serverChannel = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using ManualResetEvent connectEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverChannel = a.Channel;
            connectEvent.Set();
        };

        host.Open();

        using var channel = new TcpChannel(host.EndPoint);
        using var handler = new RpcHandler(channel);

        CancellationTokenSource cts = new CancellationTokenSource();
        await channel.OpenAsync(cts.Token);

        Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.NotNull(serverChannel);

        var serverTask = serverChannel.ReceiveAsync(cts.Token)
            .ContinueWith(t =>
            {
                serverChannel.SendAsync(t.Result, cts.Token);
            }, cts.Token);

        var packet = new SuccessPacket();
        var answer = await handler.RequestAsync(packet, cts.Token);
        await serverTask;

        var ssp = (SuccessPacket)answer;
        Assert.Equal(packet.RequestId, ssp.RequestId);
    }

    [Fact(Timeout = 15000)]
    public async Task RequestWithTheSameIdTest()
    {
        IChannel? serverChannel = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using ManualResetEvent connectEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverChannel = a.Channel;
            connectEvent.Set();
        };

        host.Open();

        using var channel = new TcpChannel(host.EndPoint);
        using var handler = new RpcHandler(channel);

        CancellationTokenSource cts = new CancellationTokenSource();
        await channel.OpenAsync(cts.Token);

        Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.NotNull(serverChannel);

        var serverTask = serverChannel.ReceiveAsync(cts.Token)
            .ContinueWith(t =>
            {
                serverChannel.SendAsync(t.Result, cts.Token);
            }, cts.Token);

        var packet = new SuccessPacket();
        var answer = await handler.RequestAsync(packet, cts.Token);
        await serverTask;

        var ssp = (SuccessPacket)answer;
        Assert.Equal(packet.RequestId, ssp.RequestId);

        serverTask = serverChannel.ReceiveAsync(cts.Token)
            .ContinueWith(t =>
            {
                serverChannel.SendAsync(t.Result, cts.Token);
            }, cts.Token);

        answer = await handler.RequestAsync(packet, cts.Token);
        await serverTask;

        ssp = (SuccessPacket)answer;
        Assert.Equal(packet.RequestId, ssp.RequestId);
    }

    [Fact(Timeout = 15000)]
    public async Task RequestTimeoutTest()
    {
        IChannel? serverChannel = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using ManualResetEvent connectEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverChannel = a.Channel;
            connectEvent.Set();
        };

        host.Open();

        using var channel = new TcpChannel(host.EndPoint);
        using var handler = new RpcHandler(channel);
        handler.AnswerTimeout = TimeSpan.FromSeconds(1);

        CancellationTokenSource cts = new CancellationTokenSource();
        await channel.OpenAsync(cts.Token);

        Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.NotNull(serverChannel);

        var serverTask = serverChannel.ReceiveAsync(cts.Token);

        var packet = new SuccessPacket();
        await Assert.ThrowsAsync<TimeoutException>(() => handler.RequestAsync(packet, cts.Token));
        await serverTask;
    }

    [Fact(Timeout = 15000)]
    public async Task ConcurrentRequestsTest()
    {
        List<string> log = new();

        IChannel? serverChannel = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using ManualResetEvent connectEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverChannel = a.Channel;
            connectEvent.Set();
        };

        host.Open();

        using var channel = new TcpChannel(host.EndPoint);
        using var handler = new RpcHandler(channel);

        CancellationTokenSource cts = new CancellationTokenSource();
        await channel.OpenAsync(cts.Token);

        Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.NotNull(serverChannel);

        // SuccessPacket/ErrorPacket are response kinds and are never dispatched as requests; drive the
        // request/response plumbing with real request packets (GetInstance/GetProperty) that the server
        // answers with the matching response kind.
        using var serverHandler = new RpcHandler(serverChannel, async (request, cancellationToken) =>
        {
            log.AddWithLock($"Start: {request.GetType().Name}");
            if (request is GetInstancePacket)
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            log.AddWithLock($"Finish: {request.GetType().Name}");
            return request is GetInstancePacket
                ? new SuccessPacket(request)
                : new ErrorPacket(request);
        });

        var packet1 = new GetInstancePacket();
        var packet2 = new GetPropertyPacket();
        var req1 = handler.RequestAsync(packet1, cts.Token);
        var req2 = handler.RequestAsync(packet2, cts.Token);
        var answer2 = await req2;
        var answer1 = await req1;

        var ssp = (SuccessPacket)answer1;
        Assert.Equal(packet1.RequestId, ssp.RequestId);

        var ep = (ErrorPacket)answer2;
        Assert.Equal(packet2.RequestId, ep.RequestId);

        var expectedLog = new string[] { "Start: GetInstancePacket", "Start: GetPropertyPacket", "Finish: GetPropertyPacket", "Finish: GetInstancePacket" };

        Assert.Equal(expectedLog.Order(), log.ToImmutableArrayWithLock().Order());
    }

    [Fact(Timeout = 15000)]
    public async Task CancelRequestSendsCancelPacketTest()
    {
        IChannel? serverChannel = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using ManualResetEvent connectEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverChannel = a.Channel;
            connectEvent.Set();
        };

        host.Open();

        using var channel = new TcpChannel(host.EndPoint);
        using var handler = new RpcHandler(channel);

        using CancellationTokenSource cts = new CancellationTokenSource();
        await channel.OpenAsync(cts.Token);

        Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.NotNull(serverChannel);

        var serverTask = serverChannel.ReceiveAsync(cts.Token);

        using CancellationTokenSource rcts = new CancellationTokenSource();
        var packet = new SuccessPacket();
        var requestTask = handler.RequestAsync(packet, rcts.Token);
        var receivedPacket = await serverTask;
        var receivedRpcPacket = await RpcPacket.FromPacketAsync(receivedPacket, RpcPacket.DefaultSerializationContext, cts.Token);
        Assert.IsType<SuccessPacket>(receivedRpcPacket);

        serverTask = serverChannel.ReceiveAsync(cts.Token);
        rcts.Cancel();
        receivedPacket = await serverTask;
        receivedRpcPacket = await RpcPacket.FromPacketAsync(receivedPacket, RpcPacket.DefaultSerializationContext, cts.Token);
        Assert.IsType<CancelRequestPacket>(receivedRpcPacket);
        Assert.Equal(packet.RequestId, receivedRpcPacket.RequestId);
        Assert.True(requestTask.IsCanceled);
    }

    [Fact(Timeout = 15000)]
    public async Task PacketHandlingLoopDoesNotPostToUiSynchronizationContextTest()
    {
        // Reproduces the bug where StartPacketHandling captures the UI SynchronizationContext
        // (Avalonia Dispatcher) because ConfigureAwait(false) is missing on the inner awaits.
        // Without the fix every packet receive cycle is posted to the captured context,
        // stalling the UI thread.
        var trackingContext = new TrackingSynchronizationContext();

        IChannel? serverChannel = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using var connectEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverChannel = a.Channel;
            connectEvent.Set();
        };

        host.Open();

        using var channel = new TcpChannel(host.EndPoint);

        // Install the tracking context before constructing RpcHandler so that
        // StartPacketHandling captures it via the first await.
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(trackingContext);
        RpcHandler handler;
        try
        {
            handler = new RpcHandler(channel);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        using (handler)
        using (var cts = new CancellationTokenSource())
        {
            await channel.OpenAsync(cts.Token);
            Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
            Assert.NotNull(serverChannel);

            // Exchange several packets to exercise the receive loop
            for (int i = 0; i < 3; i++)
            {
                var serverTask = serverChannel.ReceiveAsync(cts.Token)
                    .ContinueWith(t => serverChannel.SendAsync(t.Result, cts.Token));

                var packet = new SuccessPacket();
                await handler.RequestAsync(packet, cts.Token);
                await serverTask;
            }

            // With fix: all awaits in StartPacketHandling use ConfigureAwait(false) so
            // continuations never run on the captured SynchronizationContext → PostCount == 0.
            // Without fix: WaitForChannelOpenAsync and ReceiveAsync continuations are
            // scheduled back onto the captured context → PostCount > 0.
            Assert.Equal(0, trackingContext.PostCount);
        }
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(_ => d(state), null);
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public override SynchronizationContext CreateCopy() => this;
    }

    // A response-type packet (e.g. ServiceInstancePacket) whose RequestId matches no pending request must
    // NOT be dispatched to the request handler. This reproduces the reconnect/restore failure where a
    // response that arrives after its request was already dropped from _requests was misinterpreted as an
    // incoming request -> "Unknown packet" -> an ExceptionPacket bounced back. Responses are identified by
    // their kind, not merely by presence in _requests, so an orphan response is dropped silently.
    [Fact(Timeout = 15000)]
    public async Task OrphanResponsePacketIsDroppedNotDispatchedAsRequestTest()
    {
        IChannel? serverChannel = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using ManualResetEvent connectEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverChannel = a.Channel;
            connectEvent.Set();
        };

        host.Open();

        using var channel = new TcpChannel(host.EndPoint);

        int handlerCalls = 0;
        using var handler = new RpcHandler(channel, (request, cancellationToken) =>
        {
            Interlocked.Increment(ref handlerCalls);
            return Task.FromResult<RpcPacket?>(new SuccessPacket(request));
        });

        using var cts = new CancellationTokenSource();
        await channel.OpenAsync(cts.Token);

        Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.NotNull(serverChannel);

        // Send a response packet that corresponds to no in-flight request on the handler.
        var orphan = new ServiceInstancePacket { InstanceId = Guid.NewGuid() };
        var bytes = await orphan.ToBytesAsync(RpcPacket.DefaultSerializationContext, cts.Token);
        await serverChannel!.SendAsync(IPacket.Create(bytes), cts.Token);

        // The handler must neither dispatch it as a request nor bounce anything back.
        var serverRecv = serverChannel.ReceiveAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

        Assert.False(serverRecv.IsCompleted, "Orphan response was misrouted and bounced back as a request answer");
        Assert.Equal(0, Volatile.Read(ref handlerCalls));

        cts.Cancel();
        try { await serverRecv; } catch { /* cancelled receive */ }
    }

    [Fact(Timeout = 15000)]
    public async Task ClientRequestCancelationCancelsRequestOnServerTest()
    {
        RpcHandler? serverHandler = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using ManualResetEvent connectEvent = new ManualResetEvent(false);
        using ManualResetEvent requestReceivedEvent = new ManualResetEvent(false);
        using ManualResetEvent requestCancelledEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverHandler = new RpcHandler(a.Channel);
            connectEvent.Set();
        };

        host.Open();

        using var channel = new TcpChannel(host.EndPoint);
        using var handler = new RpcHandler(channel);

        CancellationTokenSource cts = new CancellationTokenSource();
        await channel.OpenAsync(cts.Token);

        Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.NotNull(serverHandler);

        serverHandler.Handle<GetInstancePacket>(async (p, ct) =>
        {
            using var ctr = ct.Register(() =>
            {
                requestCancelledEvent.Set();
            });
            requestReceivedEvent.Set();
            ct.WaitHandle.WaitOne();
            await requestCancelledEvent.WaitOneAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            return null;
        });

        CancellationTokenSource rcts = new CancellationTokenSource();
        var packet = new GetInstancePacket();
        var requestTask = handler.RequestAsync(packet, rcts.Token);
        Assert.True(requestReceivedEvent.WaitOne(TimeSpan.FromSeconds(5)));
        rcts.Cancel();
        Assert.True(requestCancelledEvent.WaitOne(TimeSpan.FromSeconds(5)));
    }

    [Fact(Timeout = 15000)]
    public async Task ClientDisconnectCancelsLongRunningHandlerImmediatelyTest()
    {
        // Reproduces the server crash / hang when a client disconnects while a long running,
        // blocking RPC handler is still in flight. On disconnect the server channel faults,
        // and the handler's cancellation token must be cancelled immediately (not after the
        // 5 second teardown timeout) so blocking handlers can unwind cooperatively. Tearing
        // down the server handler afterwards must not throw.
        RpcHandler? serverHandler = null;
        using var host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        using ManualResetEvent connectEvent = new ManualResetEvent(false);
        using ManualResetEvent requestReceivedEvent = new ManualResetEvent(false);
        using ManualResetEvent requestCancelledEvent = new ManualResetEvent(false);
        host.Connected += (s, a) =>
        {
            serverHandler = new RpcHandler(a.Channel);
            connectEvent.Set();
        };

        host.Open();

        var channel = new TcpChannel(host.EndPoint);
        var handler = new RpcHandler(channel);

        CancellationTokenSource cts = new CancellationTokenSource();
        await channel.OpenAsync(cts.Token);

        Assert.True(connectEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.NotNull(serverHandler);

        serverHandler.Handle<GetInstancePacket>(async (p, ct) =>
        {
            using var ctr = ct.Register(() => requestCancelledEvent.Set());
            requestReceivedEvent.Set();
            // Simulate a blocking, no-timeout handler that only unwinds on cancellation.
            ct.WaitHandle.WaitOne();
            return null;
        });

        var packet = new GetInstancePacket();
        // Fire the request without awaiting - the server handler will block until cancelled.
        _ = handler.RequestAsync(packet, cts.Token);
        Assert.True(requestReceivedEvent.WaitOne(TimeSpan.FromSeconds(5)));

        // Abruptly disconnect the client.
        handler.Dispose();
        channel.Dispose();

        // The blocking server handler must be cancelled promptly after the disconnect,
        // well before the 5 second teardown timeout.
        Assert.True(await requestCancelledEvent.WaitOneAsync(TimeSpan.FromSeconds(2), CancellationToken.None));

        // Tearing down the server handler with a (now cancelled) in flight handler must not throw.
        serverHandler.Dispose();
    }
}