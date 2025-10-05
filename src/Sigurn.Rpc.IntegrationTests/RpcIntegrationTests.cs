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
}

public class RpcIntegrationTests
{
    [Fact]
    public async Task CheckGenerator()
    {
        using var tcpHost = new TcpHost();
        var serviceHost = new ServiceHost(tcpHost);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService());
        serviceHost.Start();

        using var client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(tcpHost.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(CancellationToken.None);

        using ManualResetEvent eventTriggered = new ManualResetEvent(false);
        var testService = await client.GetService<ITestService>(CancellationToken.None);
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
}