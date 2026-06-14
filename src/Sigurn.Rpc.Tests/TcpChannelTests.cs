using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Sigurn.Rpc.Tests;

public class TcpChannelTests
{
    private static CancellationToken CurrentToken => TestContext.Current.CancellationToken;

    [Fact(Timeout = 15000)]
    public async Task ConnectTest()
    {
        BlockingCollection<string> historyClient = [];
        BlockingCollection<string> historyServer = [];

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverTask = Task.Run(async () =>
        {
            TcpChannel? serverChannel = null;
            try
            {
                socket.Listen();
                serverChannel = new TcpChannel(await socket.AcceptAsync(), new ChannelProtocol());
                serverChannel.Closing += (s, e) => historyServer.Add("Closing");
                serverChannel.Closed += (s, e) => historyServer.Add("Closed");
                serverChannel.Faulted += (s, e) => historyServer.Add("Faulted");
                await serverChannel.ReceiveAsync(CurrentToken);
            }
            catch (Exception)
            {

            }

            if (serverChannel is not null)
                await serverChannel.CloseAsync(CurrentToken);
            serverChannel?.Dispose();
        }, CurrentToken);

        Assert.NotNull(socket.LocalEndPoint);

        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        clientChannel.Opening += (s, e) => historyClient.Add("Opening");
        clientChannel.Opened += (s, e) => historyClient.Add("Opened");
        clientChannel.Closing += (s, e) => historyClient.Add("Closing");
        clientChannel.Closed += (s, e) => historyClient.Add("Closed");
        clientChannel.Faulted += (s, e) => historyClient.Add("Faulted");

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
        BlockingCollection<string> historyClient = new();

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var serverTask = Task.Run(async () =>
        {
            socket.Listen();
            var clientSocket = await socket.AcceptAsync(CurrentToken);
            clientSocket.Shutdown(SocketShutdown.Both);
            clientSocket.Close();
            clientSocket.Dispose();
        }, CurrentToken);

        Assert.NotNull(socket.LocalEndPoint);

        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        clientChannel.Opening += (s, e) => historyClient.Add("Opening");
        clientChannel.Opened += (s, e) => historyClient.Add("Opened");
        clientChannel.Closing += (s, e) => historyClient.Add("Closing");
        clientChannel.Closed += (s, e) => historyClient.Add("Closed");
        clientChannel.Faulted += (s, e) => historyClient.Add("Faulted");

        await clientChannel.OpenAsync(CurrentToken);
        await serverTask;
        await clientChannel.CloseAsync(CurrentToken);

        Assert.Equal(["Opening", "Opened", "Closing", "Closed"], historyClient);
    }

    [Fact(Timeout = 15000)]
    public async Task SendReceiveTest()
    {
        BlockingCollection<string> historyClient = [];
        BlockingCollection<string> historyServer = [];
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
                serverChannel.Closing += (s, e) => historyServer.Add("Closing");
                serverChannel.Closed += (s, e) => historyServer.Add("Closed");
                serverChannel.Faulted += (s, e) => historyServer.Add("Faulted");
                var packet = await serverChannel.ReceiveAsync(CurrentToken);
                receivedPacket = [.. packet.Data];
            }
            catch (Exception)
            {

            }

            if (serverChannel is not null)
                await serverChannel.CloseAsync(CurrentToken);
            serverChannel?.Dispose();
        }, CurrentToken);

        Assert.NotNull(socket.LocalEndPoint);

        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        clientChannel.Opening += (s, e) => historyClient.Add("Opening");
        clientChannel.Opened += (s, e) => historyClient.Add("Opened");
        clientChannel.Closing += (s, e) => historyClient.Add("Closing");
        clientChannel.Closed += (s, e) => historyClient.Add("Closed");
        clientChannel.Faulted += (s, e) => historyClient.Add("Faulted");

        await clientChannel.OpenAsync(CurrentToken);
        await clientChannel.SendAsync(IPacket.Create(sentPacket), CurrentToken);

        await serverTask;

        await clientChannel.CloseAsync(CurrentToken);

        Assert.Equal(sentPacket, receivedPacket);
        Assert.Equal(["Opening", "Opened", "Closing", "Closed"], historyClient);
        Assert.Equal(["Closing", "Closed"], historyServer);
    }


    [Fact(Timeout = 15000)]
    public async Task CancelSendTest()
    {
        BlockingCollection<string> historyClient = [];
        BlockingCollection<string> historyServer = [];
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
                serverChannel.Closing += (s, e) => historyServer.Add("Closing");
                serverChannel.Closed += (s, e) => historyServer.Add("Closed");
                serverChannel.Faulted += (s, e) => historyServer.Add("Faulted");
            }
            catch (Exception)
            {

            }

            if (serverChannel is not null)
                await serverChannel.CloseAsync(CurrentToken);
            serverChannel?.Dispose();
        }, CurrentToken);

        Assert.NotNull(socket.LocalEndPoint);

        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        clientChannel.Opening += (s, e) => historyClient.Add("Opening");
        clientChannel.Opened += (s, e) => historyClient.Add("Opened");
        clientChannel.Closing += (s, e) => historyClient.Add("Closing");
        clientChannel.Closed += (s, e) => historyClient.Add("Closed");
        clientChannel.Faulted += (s, e) => historyClient.Add("Faulted");

        await clientChannel.OpenAsync(CurrentToken);
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await clientChannel.SendAsync(IPacket.Create(sentPacket), cts.Token);
        });

        await serverTask;

        await clientChannel.CloseAsync(CurrentToken);

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

        var acceptTask = socket.AcceptAsync(CurrentToken);

        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        await clientChannel.OpenAsync(CurrentToken);

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);
        var receiveTask = clientChannel.ReceiveAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await receiveTask);

        using var servreSocket = await acceptTask;

        Assert.Equal(ChannelState.Opened, clientChannel.State);
        
        await clientChannel.CloseAsync(CurrentToken);
    }

    [Fact(Timeout = 15000)]
    public async Task ReceiveIsFailedOnCloseTest()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Listen();

        Assert.NotNull(socket.LocalEndPoint);

        var acceptTask = socket.AcceptAsync(CurrentToken);

        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        await clientChannel.OpenAsync(CurrentToken);
        
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);
        var receiveTask = clientChannel.ReceiveAsync(cts.Token);
        await clientChannel.CloseAsync(CurrentToken);
        
        using var serverSocket = await acceptTask;

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await receiveTask);
    }

    [Fact(Timeout = 15000)]
    public async Task ReceiveIsFailedOnServerCloseTest()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Listen();

        Assert.NotNull(socket.LocalEndPoint);

        var acceptTask = socket.AcceptAsync(CurrentToken);

        var clientChannel = new TcpChannel((IPEndPoint)socket.LocalEndPoint, new ChannelProtocol());
        await clientChannel.OpenAsync(CurrentToken);
        
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);
        var receiveTask = clientChannel.ReceiveAsync(cts.Token);

        using var serverSocket = await acceptTask;
        serverSocket.Shutdown(SocketShutdown.Both);
        serverSocket.Close();
        serverSocket.Dispose();

        var ex = await Assert.ThrowsAsync<SocketException>(async () => await receiveTask);
        Assert.Equal(SocketError.ConnectionAborted, ex.SocketErrorCode);
        Assert.Equal(ChannelState.Faulted, clientChannel.State);

        await clientChannel.CloseAsync(CurrentToken);
    }
}