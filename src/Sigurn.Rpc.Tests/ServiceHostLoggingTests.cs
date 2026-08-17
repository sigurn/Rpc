using System.Net;

namespace Sigurn.Rpc.Tests;

[Collection("RpcLogging")]
public class ServiceHostLoggingTests
{
    [Fact]
    public void ServiceRegistration_IsLoggedByTheHostThatPerformsIt_AndSaysNothingElse()
    {
        var factory = new CapturingLoggerFactory();
        using var scope = new RpcLoggingScope(factory);

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);

        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService([]));

        // ServiceHost only forwards; the registration itself happens in ServiceHostAsync, and the
        // category must say so.
        var registered = Assert.Single(factory.Records, x => x.Message.Contains("Registered service"));
        Assert.Equal(typeof(ServiceHostAsync).FullName, registered.Category);

        // Entry/exit noise around a local method says nothing, and its arrows now mean a remote
        // member access everywhere else in the log.
        Assert.DoesNotContain(factory.Records, x => x.Message.Contains("RegisterSerive"));
    }
}
