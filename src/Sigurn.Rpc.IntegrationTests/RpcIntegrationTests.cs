using System.Collections.Concurrent;

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

/// <summary>
/// Minimal single-threaded SynchronizationContext that captures posted continuations into a
/// queue, mimicking a UI message loop. While the owning thread blocks on a sync-over-async call,
/// nothing drains the queue, so a captured continuation can never run — the deadlock the fix
/// must avoid.
/// </summary>
internal sealed class SingleThreadSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public void Complete() => _queue.CompleteAdding();
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

    /// <summary>
    /// Reproduces the deadlock that occurs when a proxy method or proxy/client disposal is
    /// invoked synchronously (sync-over-async) from a thread that carries a SynchronizationContext
    /// (e.g. an Avalonia/WPF UI thread). Without ConfigureAwait(false) inside the library the
    /// awaited continuations are posted back to that single thread, which is blocked waiting for
    /// the very task it must complete — a classic deadlock. With ConfigureAwait(false) the
    /// continuations run on the thread pool and the blocking calls return.
    /// </summary>
    [Fact]
    public async Task SyncOverAsync_OnSynchronizationContext_DoesNotDeadlock()
    {
        using var tcpHost = new TcpHost();
        var serviceHost = new ServiceHost(tcpHost);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService());
        serviceHost.Start();

        var (client, testService) = await CreateClientAsync(tcpHost);

        using var done = new ManualResetEventSlim(false);
        Exception? error = null;

        var thread = new Thread(() =>
        {
            var syncContext = new SingleThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(syncContext);
            try
            {
                // Sync-over-async on a UI-like thread: blocking on an awaited proxy call.
                var result = testService.Method4("string1", "string2", CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert.Equal("string1string2", result);

                // The original repro: disposing the proxy and the client from the UI thread.
                // Proxy.Dispose() releases the server instance, whose ServiceInstance.Dispose()
                // does DisposeAsync().AsTask().Wait().
                (testService as IDisposable)?.Dispose();
                client.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                syncContext.Complete();
                done.Set();
            }
        })
        { IsBackground = true };
        thread.Start();

        Assert.True(done.Wait(TimeSpan.FromSeconds(15)),
            "Sync-over-async on a SynchronizationContext deadlocked — library awaits must use ConfigureAwait(false).");
        Assert.Null(error);
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