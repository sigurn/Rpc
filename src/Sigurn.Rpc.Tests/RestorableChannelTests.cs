using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace Sigurn.Rpc.Tests;

public class RestorableChannelTests
{
    [Fact]
    public void CreateChannelInvalidArgs()
    {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        Assert.Throws<ArgumentNullException>(() => new RestorableChannel(null));
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
    }

    private Task<IChannel> FakeFactory(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    [Fact]
    public void CreateChannelSuccess()
    {
        RestorableChannel channel = new RestorableChannel([FakeFactory]);
        Assert.Equal(ChannelState.Created, channel.State);
        Assert.True(channel.AutoReopen);
        Assert.Equal(TimeSpan.FromSeconds(5), channel.ReopenInterval);
        Assert.Null(channel.BoundObject);
    }

    [Fact]
    public async Task CannotOpenChannelOneFactory()
    {
        RestorableChannel channel = new RestorableChannel([FakeFactory]);
        var ex = await Assert.ThrowsAsync<AggregateException>(() => channel.OpenAsync(CancellationToken.None));
        Assert.Equal(ChannelState.Faulted, channel.State);
        Assert.StartsWith("None of the channel factories was able get connected to the server", ex.Message);
        Assert.Single(ex.InnerExceptions);
        var innerEx = ex.InnerExceptions[0];
        Assert.IsType<NotImplementedException>(innerEx);        
    }

    [Fact]
    public async Task CannotOpenChannelSeveralFactories()
    {
        RestorableChannel channel = new RestorableChannel([FakeFactory, FakeFactory, FakeFactory]);

        var ex = await Assert.ThrowsAsync<AggregateException>(() => channel.OpenAsync(CancellationToken.None));
        Assert.Equal(ChannelState.Faulted, channel.State);
        Assert.StartsWith("None of the channel factories was able get connected to the server", ex.Message);

        Assert.Equal(3, ex.InnerExceptions.Count);
        Assert.IsType<NotImplementedException>(ex.InnerExceptions[0]);        
        Assert.IsType<NotImplementedException>(ex.InnerExceptions[1]);        
        Assert.IsType<NotImplementedException>(ex.InnerExceptions[2]);        
    }

    private Task<IChannel> NullFactory(CancellationToken cancellationToken)
    {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        return Task.FromResult<IChannel>(null);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
    }

    [Fact]
    public async Task CannotOpenChannelFactoryReturnsNull()
    {
        RestorableChannel channel = new RestorableChannel([NullFactory]);

        var ex = await Assert.ThrowsAsync<Exception>(() => channel.OpenAsync(CancellationToken.None));
        Assert.Equal(ChannelState.Faulted, channel.State);
        Assert.Equal("None of the channel factories was able get connected to the server", ex.Message);
    }

    [Fact]
    public async Task CannotOpenChannelSeveralFactoriesReturnNull()
    {
        RestorableChannel channel = new RestorableChannel([NullFactory, NullFactory, NullFactory]);

        var ex = await Assert.ThrowsAsync<Exception>(() => channel.OpenAsync(CancellationToken.None));
        Assert.Equal(ChannelState.Faulted, channel.State);
        Assert.StartsWith("None of the channel factories was able get connected to the server", ex.Message);
    }

    [Fact]
    public async Task CannotOpenChannelAllFactoriesAreNull()
    {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
        RestorableChannel channel = new RestorableChannel([null, null, null]);
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

        var ex = await Assert.ThrowsAsync<Exception>(() => channel.OpenAsync(CancellationToken.None));
        Assert.Equal(ChannelState.Faulted, channel.State);
        Assert.StartsWith("None of the channel factories was able get connected to the server", ex.Message);
    }

    [Fact]
    public async Task OpenAndCloseChannelSuccessfully()
    {
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.Start();

        var log = new List<string>();
        RestorableChannel channel = new RestorableChannel(async (ct) =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(ct);
            return channel;
        });
        channel.AutoReopen = false;

        channel.Opening += (s,e) => log.AddWithLock("Opening");
        channel.Opened += (s,e) => log.AddWithLock("Opened");
        channel.Closing += (s,e) => log.AddWithLock("Closing");
        channel.Closed += (s,e) => log.AddWithLock("Closed");
        channel.Faulted += (s,e) => log.AddWithLock("Faulted");

        await channel.OpenAsync(CancellationToken.None);
        Assert.Equal(ChannelState.Opened, channel.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened"], log.ToImmutableArrayWithLock());

        await channel.CloseAsync(CancellationToken.None);

        Assert.Equal(ChannelState.Closed, channel.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened", "Closing", "Closed"], log.ToImmutableArrayWithLock());

        serviceHost.Stop();
    }

    [Fact]
    public async Task OpenAndCloseChannelSuccessfullyFromTheSecondTry()
    {
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.Start();

        var log = new List<string>();
        RestorableChannel channel = new RestorableChannel(FakeFactory, async (ct) =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(ct);
            return channel;
        });
        channel.AutoReopen = false;

        channel.Opening += (s,e) => log.AddWithLock("Opening");
        channel.Opened += (s,e) => log.AddWithLock("Opened");
        channel.Closing += (s,e) => log.AddWithLock("Closing");
        channel.Closed += (s,e) => log.AddWithLock("Closed");
        channel.Faulted += (s,e) => log.AddWithLock("Faulted");

        await channel.OpenAsync(CancellationToken.None);
        Assert.Equal(ChannelState.Opened, channel.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened"], log.ToImmutableArrayWithLock());

        await channel.CloseAsync(CancellationToken.None);

        Assert.Equal(ChannelState.Closed, channel.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened", "Closing", "Closed"], log.ToImmutableArrayWithLock());

        serviceHost.Stop();
    }

    [Fact]
    public async Task ConnectionTerminatedByServerNoAutoReopen()
    {
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.Start();

        var log = new List<string>();
        RestorableChannel channel = new RestorableChannel(async (ct) =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(ct);
            return channel;
        });
        channel.AutoReopen = false;

        using ManualResetEvent faultedEvent = new ManualResetEvent(false);
        channel.Opening += (s,e) => log.AddWithLock("Opening");
        channel.Opened += (s,e) => log.AddWithLock("Opened");
        channel.Closing += (s,e) => log.AddWithLock("Closing");
        channel.Closed += (s,e) => log.AddWithLock("Closed");
        channel.Faulted += (s,e) =>
        {
            log.AddWithLock("Faulted");
            faultedEvent.Set();
        };

        await channel.OpenAsync(CancellationToken.None);
        Assert.Equal(ChannelState.Opened, channel.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened"], log.ToImmutableArrayWithLock());

        faultedEvent.Reset();
        serviceHost.Stop();
        try
        {
            await channel.SendAsync(IPacket.Create(Array.Empty<byte>()), CancellationToken.None);
        }
        catch(AggregateException)
        {
        }
        Assert.True(faultedEvent.WaitOne(TimeSpan.FromSeconds(2)));
        Assert.Equal(ChannelState.Faulted, channel.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened", "Faulted"], log.ToImmutableArrayWithLock());

        await channel.CloseAsync(CancellationToken.None);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened", "Faulted", "Closing", "Closed"], log.ToImmutableArrayWithLock());
    }

    [Fact]
    public async Task ConnectionTerminatedByServerNoAutoReopenButOpenManuallyAgain()
    {
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.Start();

        var log = new List<string>();
        RestorableChannel channel = new RestorableChannel(async (ct) =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(ct);
            return channel;
        });
        channel.AutoReopen = false;

        using ManualResetEvent faultedEvent = new ManualResetEvent(false);
        channel.Opening += (s,e) => log.AddWithLock("Opening");
        channel.Opened += (s,e) => log.AddWithLock("Opened");
        channel.Closing += (s,e) => log.AddWithLock("Closing");
        channel.Closed += (s,e) => log.AddWithLock("Closed");
        channel.Faulted += (s,e) =>
        {
            log.AddWithLock("Faulted");
            faultedEvent.Set();
        };

        await channel.OpenAsync(CancellationToken.None);
        Assert.Equal(ChannelState.Opened, channel.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened"], log.ToImmutableArrayWithLock());

        faultedEvent.Reset();
        serviceHost.Stop();
        await Assert.ThrowsAsync<AggregateException>(() => channel.SendAsync(IPacket.Create(Array.Empty<byte>()), CancellationToken.None));
        Assert.True(faultedEvent.WaitOne(TimeSpan.FromSeconds(1)));
        Assert.Equal(ChannelState.Faulted, channel.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened", "Faulted"], log.ToImmutableArrayWithLock());

        serviceHost.Start();
        await channel.OpenAsync(CancellationToken.None);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened", "Faulted", "Opening", "Opened"], log.ToImmutableArrayWithLock());

        await channel.CloseAsync(CancellationToken.None);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened", "Faulted", "Opening", "Opened", "Closing", "Closed"], log.ToImmutableArrayWithLock());
    }
}