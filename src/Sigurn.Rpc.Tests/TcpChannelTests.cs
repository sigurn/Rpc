using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Sigurn.Rpc.Tests;

public class TcpChannelTests
{
    [Fact(Timeout = 15000)]
    public async Task ConnectTest()
    {
        BlockingCollection<string> historyClient = new ();
        BlockingCollection<string> historyServer = new ();

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverTask = Task.Run(async () =>
        {
            TcpChannel? serverChannel = null;
            try
            {
                socket.Listen();
                serverChannel = new TcpChannel(await socket.AcceptAsync(), new ChannelProtocol());
                serverChannel.Closing += (s,e) => historyServer.Add("Closing");
                serverChannel.Closed += (s,e) => historyServer.Add("Closed");
                serverChannel.Faulted += (s,e) => historyServer.Add("Faulted");
                await serverChannel.ReceiveAsync(CancellationToken.None);
            }
            catch(Exception)
            {

            }

            if (serverChannel is not null)
                await serverChannel.CloseAsync(CancellationToken.None);
            serverChannel?.Dispose();
        });

        Assert.NotNull(socket.LocalEndPoint);
        
        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        clientChannel.Opening += (s,e) => historyClient.Add("Opening");
        clientChannel.Opened += (s,e) => historyClient.Add("Opened");
        clientChannel.Closing += (s,e) => historyClient.Add("Closing");
        clientChannel.Closed += (s,e) => historyClient.Add("Closed");
        clientChannel.Faulted += (s,e) => historyClient.Add("Faulted");

        await clientChannel.OpenAsync(CancellationToken.None);
        Assert.Equal((IPEndPoint)socket.LocalEndPoint, clientChannel.RemoteEndPoint);
        Assert.Equal(socket.LocalEndPoint.ToString(), ((IAddressableChannel)clientChannel).RemoteAddress);
        await clientChannel.CloseAsync(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => clientChannel.RemoteEndPoint);
        Assert.Equal(string.Empty, ((IAddressableChannel)clientChannel).RemoteAddress);

        await serverTask;

        Assert.Equal(["Opening", "Opened", "Closing", "Closed"], historyClient);
        Assert.Equal(["Faulted", "Closing", "Closed"], historyServer);
    }

    [Fact(Timeout = 15000)]
    public async Task ServerDisconnectTest()
    {
        BlockingCollection<string> historyClient = new ();

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverTask = Task.Run(async () =>
        {
            socket.Listen();
            var clientSocket = await socket.AcceptAsync();
            clientSocket.Close();
            clientSocket.Dispose();
        });

        Assert.NotNull(socket.LocalEndPoint);

        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        clientChannel.Opening += (s,e) => historyClient.Add("Opening");
        clientChannel.Opened += (s,e) => historyClient.Add("Opened");
        clientChannel.Closing += (s,e) => historyClient.Add("Closing");
        clientChannel.Closed += (s,e) => historyClient.Add("Closed");
        clientChannel.Faulted += (s,e) => historyClient.Add("Faulted");

        await clientChannel.OpenAsync(CancellationToken.None);
        await serverTask;
        await clientChannel.CloseAsync(CancellationToken.None);

        Assert.Equal(["Opening", "Opened", "Closing", "Closed"], historyClient);
    }

    [Fact(Timeout = 15000)]
    public async Task SendReceiveTest()
    {
        BlockingCollection<string> historyClient = new ();
        BlockingCollection<string> historyServer = new ();
        byte[]? receivedPacket = null;
        byte[] sentPacket = [0x01, 0x02, 0x03, 0x04, 0x05];

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverTask = Task.Run(async () =>
        {
            TcpChannel? serverChannel = null;
            try
            {
                socket.Listen();
                serverChannel = new TcpChannel(await socket.AcceptAsync(), new ChannelProtocol());
                serverChannel.Closing += (s,e) => historyServer.Add("Closing");
                serverChannel.Closed += (s,e) => historyServer.Add("Closed");
                serverChannel.Faulted += (s,e) => historyServer.Add("Faulted");
                var packet = await serverChannel.ReceiveAsync(CancellationToken.None);
                receivedPacket = packet.Data.ToArray();
            }
            catch(Exception)
            {

            }

            if (serverChannel is not null)
                await serverChannel.CloseAsync(CancellationToken.None);
            serverChannel?.Dispose();
        });

        Assert.NotNull(socket.LocalEndPoint);

        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        clientChannel.Opening += (s, e) => historyClient.Add("Opening");
        clientChannel.Opened += (s, e) => historyClient.Add("Opened");
        clientChannel.Closing += (s, e) => historyClient.Add("Closing");
        clientChannel.Closed += (s, e) => historyClient.Add("Closed");
        clientChannel.Faulted += (s, e) => historyClient.Add("Faulted");

        await clientChannel.OpenAsync(CancellationToken.None);
        await clientChannel.SendAsync(IPacket.Create(sentPacket), CancellationToken.None);

        await serverTask;

        await clientChannel.CloseAsync(CancellationToken.None);

        Assert.Equal(sentPacket, receivedPacket);
        Assert.Equal(["Opening", "Opened", "Closing", "Closed"], historyClient);
        Assert.Equal(["Closing", "Closed"], historyServer);
    }


    [Fact(Timeout = 15000)]
    public async Task CancelReceiveTest()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Listen();

        Assert.NotNull(socket.LocalEndPoint);

        var acceptTask = socket.AcceptAsync();

        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        await clientChannel.OpenAsync(CancellationToken.None);
        
        CancellationTokenSource cts = new CancellationTokenSource();
        var receiveTask = clientChannel.ReceiveAsync(cts.Token);
        cts.Cancel();
        
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await receiveTask);

        using var servreSocket = await acceptTask;

        await clientChannel.CloseAsync(CancellationToken.None);
    }
}