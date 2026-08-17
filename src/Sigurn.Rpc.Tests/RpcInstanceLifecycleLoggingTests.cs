using System.Net;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Tests;

/// <summary>
/// Creating and releasing an instance must be identifiable in the log: the instance id alone says
/// nothing about what the instance was.
/// </summary>
[Collection("RpcLogging")]
public class RpcInstanceLifecycleLoggingTests
{
    private const string InterfaceName = "Sigurn.Rpc.Tests.ITestService";
    private const string ServiceTypeName = "Sigurn.Rpc.Tests.TestService";

    [Fact(Timeout = 30000)]
    public async Task InstanceRegistrationAndRelease_AreLogged_WithFullTypeNameAndInstanceId()
    {
        var factory = new CapturingLoggerFactory();
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

        Guid instanceId;
        try
        {
            var service = await client.GetService<ITestService>(cts.Token);
            Assert.NotNull(service);

            instanceId = Assert.IsAssignableFrom<InterfaceProxy>(service).InstanceId;

            // Server side registration.
            var registered = factory.Records.Where(x =>
                x.Category == typeof(Session).FullName
                && x.Level == LogLevel.Information
                && x.Message.Contains("registered")
                && x.HasField("InstanceId", instanceId)).ToList();

            Assert.NotEmpty(registered);
            Assert.All(registered, x =>
            {
                Assert.True(x.HasField("Interface", InterfaceName), $"Interface field was '{x.Field("Interface")}'");
                Assert.True(x.HasField("InstanceType", ServiceTypeName), $"InstanceType field was '{x.Field("InstanceType")}'");
            });

            // Client side proxy creation.
            Assert.Contains(factory.Records, x =>
                x.Category == typeof(Session).FullName
                && x.Message.Contains("Proxy created")
                && x.HasField("InstanceId", instanceId)
                && x.HasField("Interface", InterfaceName));

            ((IDisposable)service).Dispose();
        }
        finally
        {
            await client.CloseAsync(CancellationToken.None);
            host.Close();
        }

        // Server side release.
        var released = factory.Records.Where(x =>
            x.Category == typeof(Session).FullName
            && x.Level == LogLevel.Information
            && x.Message.Contains("released")
            && x.HasField("InstanceId", instanceId)).ToList();

        Assert.NotEmpty(released);
        Assert.All(released, x =>
        {
            Assert.True(x.HasField("Interface", InterfaceName), $"Interface field was '{x.Field("Interface")}'");
            Assert.True(x.HasField("InstanceType", ServiceTypeName), $"InstanceType field was '{x.Field("InstanceType")}'");
        });

        // Client side proxy release.
        Assert.Contains(factory.Records, x =>
            x.Category == typeof(InterfaceProxy).FullName
            && x.Message.Contains("Proxy released")
            && x.HasField("InstanceId", instanceId)
            && x.HasField("Interface", InterfaceName));
    }

    // Registration in a session is not creation: a shared adapter is registered once per session
    // while the object behind it is built once. The object's own lifetime is logged by the adapter,
    // and an adapter number ties the two together.
    [Fact(Timeout = 30000)]
    public async Task ServiceObject_LogsItsOwnCreationAndDisposal()
    {
        var factory = new CapturingLoggerFactory();
        using var scope = new RpcLoggingScope(factory);

        var log = new List<string>();
        using ManualResetEvent destroyed = new ManualResetEvent(false);

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyed));
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

            ((IDisposable)service).Dispose();
            Assert.True(destroyed.WaitOne(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await client.CloseAsync(CancellationToken.None);
            host.Close();
        }

        var created = Assert.Single(factory.Records, x =>
            x.Category == typeof(InterfaceAdapter).FullName
            && x.Level == LogLevel.Information
            && x.Message.Contains("Service instance created")
            && x.HasField("InstanceType", ServiceTypeName));

        Assert.True(created.HasField("Interface", InterfaceName));

        var adapterId = created.Field("AdapterId");
        Assert.NotNull(adapterId);

        // The wrapped object is disposed once, and the library says whether it owns it.
        var disposed = Assert.Single(factory.Records, x =>
            x.Category == typeof(InterfaceAdapter).FullName
            && x.Message.Contains("Service instance disposed")
            && Equals(x.Field("AdapterId"), adapterId));

        Assert.True(disposed.HasField("InstanceType", ServiceTypeName));
        Assert.True(disposed.HasField("Owned", true));

        // The session's registration line points back at the object it exposed.
        Assert.Contains(factory.Records, x =>
            x.Category == typeof(Session).FullName
            && x.Message.Contains("registered")
            && Equals(x.Field("AdapterId"), adapterId));
    }
}
