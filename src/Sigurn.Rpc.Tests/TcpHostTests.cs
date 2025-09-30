using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection.Metadata;

namespace Sigurn.Rpc.Tests;

public class TcpHostTests
{
    [Fact]
    public void OpenCloseHost()
    {
        using var tcpHost = new TcpHost();
        tcpHost.Open();
        tcpHost.Close();
    }

    [Fact(Timeout=15000)]
    public async Task AcceptConnectionTest()
    {
        BlockingCollection<string> eventHistory = new ();
        using AutoResetEvent connectionEvent = new AutoResetEvent(false);

        using var tcpHost = new TcpHost();
        tcpHost.Connected += (s, e) =>
        {
            eventHistory.Add("Connected");
            connectionEvent.Set();
        };
        tcpHost.Disconnected += (s, e) =>
        {
            eventHistory.Add("Disconnected");
            connectionEvent.Set();
        };

        tcpHost.Open();
        Assert.Equal([], eventHistory.ToArray());

        var client = new TcpChannel(tcpHost.EndPoint);
        await client.OpenAsync(CancellationToken.None);
        Assert.True(connectionEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal(["Connected"], eventHistory);
        await client.CloseAsync(CancellationToken.None);
        tcpHost.Close();
        Assert.True(connectionEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal(["Connected", "Disconnected"], eventHistory);
    }
}