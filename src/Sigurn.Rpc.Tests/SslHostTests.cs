using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;

namespace Sigurn.Rpc.Tests;

public class SslHostTests
{
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
        var certificate = new X509Certificate2(Path.Combine(GetSourceDirectory(), "sslhost.pfx"));

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

    private static string GetSourceDirectory([CallerFilePath] string? path = null)
    {
        return Path.GetDirectoryName(path) ?? string.Empty;
    }
}