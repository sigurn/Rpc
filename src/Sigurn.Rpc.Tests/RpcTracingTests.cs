using System.Net;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Tests;

/// <summary>
/// Verifies that member access is traced with full interface and member names on both sides of the
/// wire, and that nothing is traced when the level is off.
/// </summary>
[Collection("RpcLogging")]
public class RpcTracingTests
{
    private const string InterfaceName = "Sigurn.Rpc.Tests.ITestService";

    private static bool IsProxyTrace(LogRecord record, RpcTraceOperation operation, string member, string prefix)
        => record.Category == typeof(InterfaceProxy).FullName
            && record.Level == LogLevel.Trace
            && record.Message.StartsWith(prefix)
            && record.HasField("Operation", operation)
            && record.HasField("Interface", InterfaceName)
            && record.HasField("Member", member);

    private static bool IsAdapterTrace(LogRecord record, RpcTraceOperation operation, string member, string prefix)
        => record.Category == typeof(InterfaceAdapter).FullName
            && record.Level == LogLevel.Trace
            && record.Message.StartsWith(prefix)
            && record.HasField("Operation", operation)
            && record.HasField("Interface", InterfaceName)
            && record.HasField("Member", member);

    private static async Task WithTracedClientAsync(CapturingLoggerFactory factory, Func<ITestService, Task> body)
    {
        using var scope = new RpcLoggingScope(factory);

        var log = new List<string>();

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log));
        serviceHost.Start();

        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        client.AutoReopen = false;
        await client.OpenAsync(cts.Token);

        try
        {
            var service = await client.GetService<ITestService>(cts.Token);
            Assert.NotNull(service);

            await body(service);
        }
        finally
        {
            await client.CloseAsync(CancellationToken.None);
            host.Close();
        }
    }

    [Fact(Timeout = 30000)]
    public async Task MethodCall_IsTraced_WithInterfaceAndMemberNames()
    {
        var factory = new CapturingLoggerFactory();

        await WithTracedClientAsync(factory, service =>
        {
            Assert.Equal(5, service.Add(2, 3));
            return Task.CompletedTask;
        });

        var records = factory.Records;

        Assert.Contains(records, x => IsProxyTrace(x, RpcTraceOperation.MethodCall, "Add(int, int)", "==>"));
        Assert.Contains(records, x => IsProxyTrace(x, RpcTraceOperation.MethodCall, "Add(int, int)", "<=="));
        Assert.Contains(records, x => IsAdapterTrace(x, RpcTraceOperation.MethodCall, "Add(int, int)", "==>"));
        Assert.Contains(records, x => IsAdapterTrace(x, RpcTraceOperation.MethodCall, "Add(int, int)", "<=="));
    }

    [Fact(Timeout = 30000)]
    public async Task PropertyAccess_IsTraced_WithSeparateGetAndSetOperations()
    {
        var factory = new CapturingLoggerFactory();

        await WithTracedClientAsync(factory, service =>
        {
            service.Property1 = 7;
            Assert.Equal(7, service.Property1);
            return Task.CompletedTask;
        });

        var records = factory.Records;

        Assert.Contains(records, x => IsProxyTrace(x, RpcTraceOperation.PropertySet, "Property1", "==>"));
        Assert.Contains(records, x => IsProxyTrace(x, RpcTraceOperation.PropertyGet, "Property1", "==>"));
        Assert.Contains(records, x => IsAdapterTrace(x, RpcTraceOperation.PropertySet, "Property1", "<=="));
        Assert.Contains(records, x => IsAdapterTrace(x, RpcTraceOperation.PropertyGet, "Property1", "<=="));
    }

    [Fact(Timeout = 30000)]
    public async Task EventSubscription_IsTraced_WithAttachAndDetachOperations()
    {
        var factory = new CapturingLoggerFactory();

        await WithTracedClientAsync(factory, service =>
        {
            void Handler(object? sender, EventArgs e) { }

            service.TestEvent += Handler;
            service.TestEvent -= Handler;
            return Task.CompletedTask;
        });

        var records = factory.Records;

        Assert.Contains(records, x => IsProxyTrace(x, RpcTraceOperation.EventAttach, "TestEvent", "==>"));
        Assert.Contains(records, x => IsProxyTrace(x, RpcTraceOperation.EventDetach, "TestEvent", "==>"));
        Assert.Contains(records, x => IsAdapterTrace(x, RpcTraceOperation.EventAttach, "TestEvent", "==>"));
        Assert.Contains(records, x => IsAdapterTrace(x, RpcTraceOperation.EventDetach, "TestEvent", "==>"));
    }

    [Fact(Timeout = 30000)]
    public async Task FailingCall_IsTraced_AsFailureWithTheException()
    {
        var factory = new CapturingLoggerFactory();

        await WithTracedClientAsync(factory, async service =>
        {
            await Assert.ThrowsAnyAsync<Exception>(() => service.MethodThrowAsync(CancellationToken.None));
        });

        var records = factory.Records;

        Assert.Contains(records, x =>
            IsAdapterTrace(x, RpcTraceOperation.MethodCall, "MethodThrowAsync(System.Threading.CancellationToken)", "<==")
            && x.Message.EndsWith("FAILED")
            && x.Exception is not null);

        Assert.Contains(records, x =>
            IsProxyTrace(x, RpcTraceOperation.MethodCall, "MethodThrowAsync(System.Threading.CancellationToken)", "<==")
            && x.Message.EndsWith("FAILED")
            && x.Exception is not null);
    }

    // An adapter can be shared by several sessions, so it has no instance id of its own; the id the
    // request is addressed to is what identifies the object in the log.
    [Fact(Timeout = 30000)]
    public async Task AdapterTrace_CarriesTheInstanceIdOfTheRequest()
    {
        var factory = new CapturingLoggerFactory();
        Guid instanceId = Guid.Empty;

        await WithTracedClientAsync(factory, service =>
        {
            instanceId = Assert.IsAssignableFrom<InterfaceProxy>(service).InstanceId;

            Assert.Equal(5, service.Add(2, 3));
            service.Property1 = 7;
            return Task.CompletedTask;
        });

        var records = factory.Records;

        Assert.Contains(records, x => IsAdapterTrace(x, RpcTraceOperation.MethodCall, "Add(int, int)", "==>")
            && x.HasField("InstanceId", instanceId));
        Assert.Contains(records, x => IsAdapterTrace(x, RpcTraceOperation.PropertySet, "Property1", "==>")
            && x.HasField("InstanceId", instanceId));
    }

    // The adapter fans an event out to every subscribed session, so the instance id belongs to the
    // per-session send. That line must name the event too, or the pair is unreadable.
    [Fact(Timeout = 30000)]
    public async Task EventSend_IsLogged_WithInstanceIdAndEventName()
    {
        var factory = new CapturingLoggerFactory();
        using var scope = new RpcLoggingScope(factory);

        var log = new List<string>();
        TestService? service = null;

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => service = new TestService(log));
        serviceHost.Start();

        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        client.AutoReopen = false;
        await client.OpenAsync(cts.Token);

        Guid instanceId;
        try
        {
            var proxy = await client.GetService<ITestService>(cts.Token);
            Assert.NotNull(proxy);
            instanceId = Assert.IsAssignableFrom<InterfaceProxy>(proxy).InstanceId;

            using var raised = new ManualResetEventSlim(false);
            void Handler(object? sender, EventArgs e) => raised.Set();

            proxy.TestEvent += Handler;
            try
            {
                Assert.NotNull(service);
                service.RaiseTestEvent();
                Assert.True(raised.Wait(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                proxy.TestEvent -= Handler;
            }
        }
        finally
        {
            await client.CloseAsync(CancellationToken.None);
            host.Close();
        }

        Assert.Contains(factory.Records, x =>
            x.Category == typeof(Session).FullName
            && x.Message.Contains("Sending event")
            && x.HasField("InstanceId", instanceId)
            && x.HasField("Interface", InterfaceName)
            && x.HasField("Member", "TestEvent"));
    }

    [Fact(Timeout = 30000)]
    public async Task NothingIsTraced_WhenTraceLevelIsDisabled()
    {
        var factory = new CapturingLoggerFactory { MinLevel = LogLevel.Information };

        await WithTracedClientAsync(factory, service =>
        {
            Assert.Equal(5, service.Add(2, 3));
            service.Property1 = 7;
            return Task.CompletedTask;
        });

        Assert.DoesNotContain(factory.Records, x => x.Level == LogLevel.Trace);
    }
}
