using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;

namespace Sigurn.Rpc.Tests;

public class SslHostTests
{
    [Fact(Timeout = 15000)]
    public async Task ServerHandshakeFailure_DisposesAcceptedSocket()
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(GetSourceDirectory(), "sslhost.pfx"), null);

        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen();
        var endPoint = (IPEndPoint)listener.LocalEndPoint!;

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(endPoint, TestContext.Current.CancellationToken);

        var serverSocket = await listener.AcceptAsync(TestContext.Current.CancellationToken);

        // Feed the server garbage instead of a TLS ClientHello and drop the connection so the
        // server-side handshake fails deterministically.
        await client.SendAsync(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 }, SocketFlags.None);
        client.Shutdown(SocketShutdown.Both);
        client.Close();

        // The server handshake must fail...
        var error = Record.Exception(() =>
            new SslChannel(serverSocket, certificate, (cert, chain) => true, false, new ChannelProtocol()));
        Assert.NotNull(error);

        // ...and the accepted socket must be closed rather than leaked.
        Assert.True(serverSocket.SafeHandle.IsClosed,
            "Accepted socket must be disposed after a failed server handshake.");

        serverSocket.Dispose();
    }

    [Fact]
    public void OpenCloseHost()
    {
        using var sslHost = new SslHost();
        sslHost.Open();
        sslHost.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task AcceptConnectionTest()
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(GetSourceDirectory(), "sslhost.pfx"), null);

        BlockingCollection<string> eventHistory = new();
        using AutoResetEvent connectionEvent = new AutoResetEvent(false);

        using var sslHost = new SslHost();
        sslHost.Certificate = certificate;
        sslHost.CertificateValidator = (s, c) =>
        {
            return true;
        };
        
        sslHost.Connected += (s, e) =>
        {
            eventHistory.Add("Connected");
            connectionEvent.Set();
        };
        sslHost.Disconnected += (s, e) =>
        {
            eventHistory.Add("Disconnected");
            connectionEvent.Set();
        };

        sslHost.Open();
        Assert.Equal([], eventHistory.ToArray());

        var distinguishedName = new X500DistinguishedName(certificate.Subject);
        var client = new SslChannel(sslHost.EndPoint, (cert, chain) =>
        {
            if (cert is null) return false;
            return cert.Subject == distinguishedName.Name;
        });

        client.ServerName = certificate.GetNameInfo(X509NameType.SimpleName, false);
        await client.OpenAsync(CancellationToken.None);
        Assert.True(connectionEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal(["Connected"], eventHistory);
        await client.CloseAsync(CancellationToken.None);
        sslHost.Close();
        Assert.True(connectionEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal(["Connected", "Disconnected"], eventHistory);
    }

    [Fact(Timeout = 15000)]
    public async Task ClientHandshakeFailure_DisposesSocket()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen();
        var endPoint = (IPEndPoint)listener.LocalEndPoint!;

        // Broken "server": accept, read the ClientHello, answer with garbage (not a ServerHello),
        // then observe whether the client closes its TCP socket after the handshake fails.
        var serverTask = Task.Run(async () =>
        {
            using var serverSocket = await listener.AcceptAsync();
            var buffer = new byte[4096];
            await serverSocket.ReceiveAsync(buffer, SocketFlags.None);
            await serverSocket.SendAsync(new byte[] { 0x15, 0x03, 0x03, 0x00, 0x02, 0x02, 0x28 }, SocketFlags.None);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                // 0 => the client closed its socket (fixed); cancellation => the socket leaked (still open).
                return await serverSocket.ReceiveAsync(buffer, SocketFlags.None, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                return -1;
            }
        });

        var client = new SslChannel(endPoint, (cert, chain) => true) { ServerName = "localhost" };
        await Assert.ThrowsAnyAsync<Exception>(() => client.OpenAsync(CancellationToken.None));

        var observed = await serverTask;
        Assert.Equal(0, observed);
    }

    private static string GetSourceDirectory([CallerFilePath] string? path = null)
    {
        return Path.GetDirectoryName(path) ?? string.Empty;
    }
}