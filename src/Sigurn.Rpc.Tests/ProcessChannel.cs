using System.Diagnostics;
using System.Net;
using System.Reflection;
using Moq;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.TestProcess;

namespace Sigurn.Rpc.Tests;

public class ProcessChannelTests
{
    private readonly string _directory;
    public ProcessChannelTests()
    {
        _directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
    }

    [Fact]
    public async Task StartStopProcess()
    {
        ProcessChannel? channel = null;
        var fileName = Path.Combine(_directory, "Sigurn.Rpc.TestProcess");
        using RpcClient client = new RpcClient(async ct =>
        {
            channel = new ProcessChannel(fileName);
            await channel.OpenAsync(ct);
            return channel;
        });
        await client.OpenAsync(CancellationToken.None);
        Assert.NotNull(channel);
        var process = Process.GetProcessById(channel.ProcessId);
        Assert.False(process.HasExited);
        await client.CloseAsync(CancellationToken.None);
        process.Refresh();
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task GetServicesCatalog()
    {
        ProcessChannel? channel = null;
        var fileName = Path.Combine(_directory, "Sigurn.Rpc.TestProcess");
        using RpcClient client = new RpcClient(async ct =>
        {
            channel = new ProcessChannel(fileName);
            await channel.OpenAsync(ct);
            return channel;
        });
        await client.OpenAsync(CancellationToken.None);
        var catalog = await client.GetService<IServiceCatalog>(CancellationToken.None);
        Assert.NotNull(catalog);
        var services = await catalog.GetServicesAsync(CancellationToken.None);
        Assert.NotNull(services);
        Assert.Single(services);
        Assert.Equal(typeof(ITestProcess), services[0].InterfaceType);
    }

    [Fact]
    public async Task UseProcessService()
    {
        ProcessChannel? channel = null;
        var fileName = Path.Combine(_directory, "Sigurn.Rpc.TestProcess");
        using RpcClient client = new RpcClient(async ct =>
        {
            channel = new ProcessChannel(fileName);
            await channel.OpenAsync(ct);
            return channel;
        });
        await client.OpenAsync(CancellationToken.None);
        var service = await client.GetService<ITestProcess>(CancellationToken.None);
        Assert.NotNull(service);
        Assert.Equal(fileName + ".dll", service.ProcessPath);
        Assert.Equal(15, service.TestMathod(5));
    }

    [Fact]
    public async Task FinishProcess()
    {
        ProcessChannel? channel = null;
        var fileName = Path.Combine(_directory, "Sigurn.Rpc.TestProcess");
        using RpcClient client = new RpcClient(async ct =>
        {
            channel = new ProcessChannel(fileName);
            await channel.OpenAsync(ct);
            return channel;
        });
        client.AutoReopen = false;
        await client.OpenAsync(CancellationToken.None);
        var service = await client.GetService<ITestProcess>(CancellationToken.None);
        Assert.NotNull(service);
        Assert.NotNull(channel);
        var process = Process.GetProcessById(channel.ProcessId);
        Assert.False(process.HasExited);
        service.Exit();
        for(int i=0; i<10; i++)
        {
            process.Refresh();
            if (process.HasExited) break;
            await Task.Delay(10);
        }
        Assert.True(process.HasExited);
        for(int i=0; i<10; i++)
        {
            if (channel.State != ChannelState.Opened) break;
            await Task.Delay(10);
        }
        Assert.Equal(ChannelState.Faulted, channel?.State);
    }
}
