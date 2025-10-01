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
            });

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
            });

        var packet = new SuccessPacket();
        var answer = await handler.RequestAsync(packet, cts.Token);
        await serverTask;

        var ssp = (SuccessPacket)answer;
        Assert.Equal(packet.RequestId, ssp.RequestId);

        serverTask = serverChannel.ReceiveAsync(cts.Token)
            .ContinueWith(t =>
            {
                serverChannel.SendAsync(t.Result, cts.Token);
            });

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

        using var serverHandler = new RpcHandler(serverChannel, async (request, cancellationToken) =>
        {
            log.AddWithLock($"Start: {request.GetType().Name}");
            if (request is SuccessPacket)
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            log.AddWithLock($"Finish: {request.GetType().Name}");
            return request;
        });

        var packet1 = new SuccessPacket();
        var packet2 = new ErrorPacket();
        var req1 = handler.RequestAsync(packet1, cts.Token);
        var req2 = handler.RequestAsync(packet2, cts.Token);
        var answer2 = await req2;
        var answer1 = await req1;

        var ssp = (SuccessPacket)answer1;
        Assert.Equal(packet1.RequestId, ssp.RequestId);

        var ep = (ErrorPacket)answer2;
        Assert.Equal(packet2.RequestId, ep.RequestId);

        var expectedLog = new string[] { "Start: SuccessPacket", "Start: ErrorPacket", "Finish: ErrorPacket", "Finish: SuccessPacket" };

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

        serverHandler.Handle<SuccessPacket>((p, ct) =>
        {
            using var ctr = ct.Register(() =>
            {
                requestCancelledEvent.Set();
            });
            requestReceivedEvent.Set();
            ct.WaitHandle.WaitOne();
            return Task.FromResult<RpcPacket?>(null);
        });

        CancellationTokenSource rcts = new CancellationTokenSource();
        var packet = new SuccessPacket();
        var requestTask = handler.RequestAsync(packet, rcts.Token);
        Assert.True(requestReceivedEvent.WaitOne(TimeSpan.FromSeconds(5)));
        rcts.Cancel();
        Assert.True(requestCancelledEvent.WaitOne(TimeSpan.FromSeconds(5)));
    }
}