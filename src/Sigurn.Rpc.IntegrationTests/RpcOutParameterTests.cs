namespace Sigurn.Rpc.IntegrationTests;

/// <summary>
/// An <c>out</c> parameter carries no value into the call, only out of it. The generated proxy must
/// therefore not read it before the call, and the generated adapter must not expect a value for it.
/// </summary>
[RemoteInterface]
public interface IOutParamService
{
    void GetText(out string text);

    bool Modify(ref string text, out int length);

    void Mixed(int factor, out int product, ref int seed);
}

public sealed class OutParamService : IOutParamService
{
    public void GetText(out string text)
    {
        text = "from service";
    }

    public bool Modify(ref string text, out int length)
    {
        text += "!";
        length = text.Length;
        return true;
    }

    public void Mixed(int factor, out int product, ref int seed)
    {
        product = factor * seed;
        seed += 1;
    }
}

public class RpcOutParameterTests
{
    private static async Task<(RpcClient client, IOutParamService service)> ConnectAsync(TcpHost tcpHost)
    {
        var client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(tcpHost.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(CancellationToken.None);
        var service = await client.GetService<IOutParamService>(CancellationToken.None);
        return (client, service);
    }

    [Fact]
    public async Task OutAndRefParameters_RoundTrip()
    {
        using var tcpHost = new TcpHost();
        var serviceHost = new ServiceHost(tcpHost);
        serviceHost.RegisterSerive<IOutParamService>(ShareWithin.None, () => new OutParamService());
        serviceHost.Start();

        var (client, service) = await ConnectAsync(tcpHost);
        using var _ = client;

        // A pure out parameter: nothing is assigned before the call.
        service.GetText(out var text);
        Assert.Equal("from service", text);

        // ref in, out back.
        var input = "abc";
        Assert.True(service.Modify(ref input, out var length));
        Assert.Equal("abc!", input);
        Assert.Equal(4, length);

        // by-value, out and ref mixed, in that declaration order.
        var seed = 3;
        service.Mixed(5, out var product, ref seed);
        Assert.Equal(15, product);
        Assert.Equal(4, seed);
    }
}
