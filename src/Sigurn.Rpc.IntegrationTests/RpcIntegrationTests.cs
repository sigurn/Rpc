namespace Sigurn.Rpc.IntegrationTests;

[RemoteInterface]
public interface ITestService
{
    int Prop1 { get; set; }
    bool Prop2 { get; set; }
    IReadOnlyList<int> Prop3 { get; set; }

    string? Prop4 { get; set; }

    public void Method1();

    public Task Method2();

    public Task Method3(CancellationToken cancellationToken);

    public Task<string> Method4(string text1, string text2, CancellationToken cancellationToken);

    public Task SlowMethodAsync(int delayMs, CancellationToken cancellationToken);

    [NoRpcTimeout]
    public Task SlowMethodNoTimeoutAsync(int delayMs, CancellationToken cancellationToken);

    event EventHandler Event1;
}


public sealed class TestService : ITestService
{
    public int Prop1 { get; set; }
    public bool Prop2 { get; set; }
    public IReadOnlyList<int> Prop3 { get; set; } = new List<int>();

    public string? Prop4 { get; set; }

    public event EventHandler? Event1;

    public void Method1()
    {
        Event1?.Invoke(this, EventArgs.Empty);
    }

    public Task Method2()
    {
        return Task.CompletedTask;
    }

    public Task Method3(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<string> Method4(string text1, string text2, CancellationToken cancellationToken)
    {
        return Task.FromResult(text1 + text2);
    }

    public Task SlowMethodAsync(int delayMs, CancellationToken cancellationToken)
    {
        return Task.Delay(delayMs, cancellationToken);
    }

    public Task SlowMethodNoTimeoutAsync(int delayMs, CancellationToken cancellationToken)
    {
        return Task.Delay(delayMs, cancellationToken);
    }
}

public class RpcIntegrationTests
{
    private static async Task<(RpcClient client, ITestService service)> CreateClientAsync(TcpHost tcpHost)
    {
        var client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(tcpHost.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });
        await client.OpenAsync(CancellationToken.None);
        var service = await client.GetService<ITestService>(CancellationToken.None);
        return (client, service);
    }

    [Fact]
    public async Task CheckGenerator()
    {
        using var tcpHost = new TcpHost();
        var serviceHost = new ServiceHost(tcpHost);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService());
        serviceHost.Start();

        var (client, testService) = await CreateClientAsync(tcpHost);
        using var _ = client;

        using ManualResetEvent eventTriggered = new ManualResetEvent(false);
        testService.Event1 += (s, e) =>
        {
            eventTriggered.Set();
        };
        testService.Prop1 = 5;
        testService.Prop2 = true;
        Assert.Equal(5, testService.Prop1);
        Assert.True(testService.Prop2);
        testService.Method1();
        Assert.True(eventTriggered.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal("string1string2", await testService.Method4("string1", "string2", CancellationToken.None));
    }

    [Fact]
    public async Task RpcContext_Timeout_CausesTimeoutException()
    {
        using var tcpHost = new TcpHost();
        var serviceHost = new ServiceHost(tcpHost);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService());
        serviceHost.Start();

        var (client, testService) = await CreateClientAsync(tcpHost);
        using var _ = client;

        using var ctx = new RpcContext { Timeout = TimeSpan.FromMilliseconds(50) };
        await Assert.ThrowsAsync<TimeoutException>(() =>
            testService.SlowMethodAsync(500, CancellationToken.None));
    }

    [Fact]
    public async Task RpcContext_Timeout_ExtendsShortDefaultTimeout()
    {
        using var tcpHost = new TcpHost();
        var serviceHost = new ServiceHost(tcpHost);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService());
        serviceHost.Start();

        var (client, testService) = await CreateClientAsync(tcpHost);
        using var _ = client;

        client.AnswerTimeout = TimeSpan.FromMilliseconds(50);

        using var ctx = new RpcContext { Timeout = TimeSpan.FromSeconds(5) };
        await testService.SlowMethodAsync(200, CancellationToken.None);
    }

    [Fact]
    public async Task NoRpcTimeout_PreventsTimeoutException()
    {
        using var tcpHost = new TcpHost();
        var serviceHost = new ServiceHost(tcpHost);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService());
        serviceHost.Start();

        var (client, testService) = await CreateClientAsync(tcpHost);
        using var _ = client;

        client.AnswerTimeout = TimeSpan.FromMilliseconds(50);

        await testService.SlowMethodNoTimeoutAsync(300, CancellationToken.None);
    }

    [Fact]
    public async Task NoRpcTimeout_OverridesRpcContextTimeout()
    {
        using var tcpHost = new TcpHost();
        var serviceHost = new ServiceHost(tcpHost);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService());
        serviceHost.Start();

        var (client, testService) = await CreateClientAsync(tcpHost);
        using var _ = client;

        using var ctx = new RpcContext { Timeout = TimeSpan.FromMilliseconds(50) };
        await testService.SlowMethodNoTimeoutAsync(300, CancellationToken.None);
    }
}