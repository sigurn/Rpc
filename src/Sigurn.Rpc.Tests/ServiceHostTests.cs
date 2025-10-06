using System.Net;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Tests;

public class ServiceHostTests
{
    [Fact(Timeout = 15000)]
    public async Task CreateDestroyServiceInstance()
    {
        List<string> log = new ();

        var tcpHost = new TcpHost();
        tcpHost.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log));

        sh.Start();

        var client = new TcpChannel(tcpHost.EndPoint);

        await client.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);
        
        IPacket packet;
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());
        ReleaseInstancePacket rip = new ReleaseInstancePacket()
        {
            InstanceId = serviceInstance.InstanceId
        };

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(rip, context, CancellationToken.None)), CancellationToken.None);

        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<SuccessPacket>(rpcp);
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());
    }

    [Theory(Timeout = 15000)]
    [InlineData(ShareWithin.None)]
    [InlineData(ShareWithin.Session)]
    [InlineData(ShareWithin.Host)]
    //[InlineData(ShareWithin.Process)]
    public async Task CreateAndAutoDestroySingleServiceInstance(ShareWithin share)
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var tcpHost = new TcpHost();
        tcpHost.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(share, () => new TestService(log, destroyEvent));

        sh.Start();

        var client = new TcpChannel(tcpHost.EndPoint);

        await client.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);
        
        IPacket packet;
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CancellationToken.None);
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());
    }

    [Theory(Timeout = 15000)]
    [InlineData(ShareWithin.None)]
    [InlineData(ShareWithin.Session)]
    [InlineData(ShareWithin.Host)]
    //[InlineData(ShareWithin.Process)]
    public async Task CreateAndDestroySingleServiceInstance(ShareWithin share)
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var tcpHost = new TcpHost();
        tcpHost.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(share, () => new TestService(log, destroyEvent));

        sh.Start();

        var client = new TcpChannel(tcpHost.EndPoint);

        await client.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);
        
        IPacket packet;
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var rip = new ReleaseInstancePacket()
        {
            InstanceId = serviceInstance.InstanceId
        };
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(rip, context, CancellationToken.None)), CancellationToken.None);
        
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<SuccessPacket>(rpcp);
    
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CancellationToken.None);
    }

    [Fact(Timeout = 15000)]
    public async Task CreateAndAutoDestroyMultipleSessionSharedServiceInstances()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var tcpHost = new TcpHost();
        tcpHost.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.Session, () => new TestService(log, destroyEvent));

        sh.Start();

        var client = new TcpChannel(tcpHost.EndPoint);

        await client.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);
        
        IPacket packet;
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var firstInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, firstInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);
        
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }
        
        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var secondInstance = (ServiceInstancePacket)rpcp;
        Assert.Equal(firstInstance.InstanceId, secondInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);
        
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }
        
        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var thirdInstance = (ServiceInstancePacket)rpcp;
        Assert.Equal(firstInstance.InstanceId, thirdInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CancellationToken.None);
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());
    }

    [Fact(Timeout = 15000)]
    public async Task CreateAndAutoDestroyMultipleHostSharedServiceInstances()
    {
        List<string> log = new();
        using ManualResetEvent destroyEvent = new(false);

        var tcpHost = new TcpHost();
        tcpHost.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.Host, () => new TestService(log, destroyEvent));

        sh.Start();

        var client1 = new TcpChannel(tcpHost.EndPoint);

        await client1.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client1.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);

        IPacket packet;
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            packet = await client1.ReceiveAsync(cts.Token);
        }

        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var firstInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, firstInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var client2 = new TcpChannel(tcpHost.EndPoint);

        await client2.OpenAsync(CancellationToken.None);

        await client2.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);

        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            packet = await client2.ReceiveAsync(cts.Token);
        }

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var secondInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(firstInstance.InstanceId, secondInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var client3 = new TcpChannel(tcpHost.EndPoint);

        await client3.OpenAsync(CancellationToken.None);

        await client3.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);

        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            packet = await client3.ReceiveAsync(cts.Token);
        }

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var thirdInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(firstInstance.InstanceId, thirdInstance.InstanceId);
        Assert.NotEqual(secondInstance.InstanceId, thirdInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client1.CloseAsync(CancellationToken.None);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client2.CloseAsync(CancellationToken.None);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client3.CloseAsync(CancellationToken.None);
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());
    }

    [Fact(Timeout = 15000)]
    public async Task CreateAndAutoDestroyMultipleProcessSharedServiceInstances()
    {
        List<string> log = new();
        using ManualResetEvent destroyEvent = new(false);

        var tcpHost = new TcpHost();
        tcpHost.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.Process, () => new TestService(log, destroyEvent));

        sh.Start();

        var client1 = new TcpChannel(tcpHost.EndPoint);

        await client1.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client1.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);

        IPacket packet;
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client1.ReceiveAsync(cts.Token);
        }

        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var firstInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, firstInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var client2 = new TcpChannel(tcpHost.EndPoint);

        await client2.OpenAsync(CancellationToken.None);

        await client2.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);

        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client2.ReceiveAsync(cts.Token);
        }

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var secondInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(firstInstance.InstanceId, secondInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var client3 = new TcpChannel(tcpHost.EndPoint);

        await client3.OpenAsync(CancellationToken.None);

        await client3.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);

        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client3.ReceiveAsync(cts.Token);
        }

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var thirdInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(firstInstance.InstanceId, thirdInstance.InstanceId);
        Assert.NotEqual(secondInstance.InstanceId, thirdInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client1.CloseAsync(CancellationToken.None);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client2.CloseAsync(CancellationToken.None);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client3.CloseAsync(CancellationToken.None);
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());
    }

    [Fact(Timeout = 15000)]
    public async Task CallVoidMethodWithoutArgs()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var tcpHost = new TcpHost();
        tcpHost.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));

        sh.Start();

        var client = new TcpChannel(tcpHost.EndPoint);

        await client.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);
        
        IPacket packet;
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);
        Assert.Equal(gip.RequestId, serviceInstance.RequestId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var mcp = new MethodCallPacket()
        {
            InstanceId = serviceInstance.InstanceId,
            MethodId = 1,
            Args = []
        };

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(mcp, context, CancellationToken.None)), CancellationToken.None);
        
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.IsType<MethodResultPacket>(rpcp);
        var mrp = (MethodResultPacket)rpcp;
        Assert.Equal(mcp.RequestId, mrp.RequestId);
        Assert.Null(mrp.Result);
        Assert.Null(mrp.Args);
        Assert.Equal<IEnumerable<string>>(["Created", "Method1"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CancellationToken.None);

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Method1", "Disposed"], log.ToImmutableArrayWithLock());
    }

    [Fact(Timeout = 15000)]
    public async Task CallIntMethodWithIntArgs()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var tcpHost = new TcpHost();
        tcpHost.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));

        sh.Start();

        var client = new TcpChannel(tcpHost.EndPoint);

        await client.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);
        
        IPacket packet;
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);
        Assert.Equal(gip.RequestId, serviceInstance.RequestId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var mcp = new MethodCallPacket()
        {
            InstanceId = serviceInstance.InstanceId,
            MethodId = 2,
            Args = [await ToBytes(3, context, CancellationToken.None), await ToBytes(5, context, CancellationToken.None)]
        };

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(mcp, context, CancellationToken.None)), CancellationToken.None);
        
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            packet = await client.ReceiveAsync(cts.Token);
        }

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.IsType<MethodResultPacket>(rpcp);
        var mrp = (MethodResultPacket)rpcp;
        Assert.Equal(mcp.RequestId, mrp.RequestId);
        Assert.Equal(await ToBytes(8, context, CancellationToken.None), mrp.Result);
        Assert.Null(mrp.Args);
        Assert.Equal<IEnumerable<string>>(["Created", "Add 3, 5"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CancellationToken.None);

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Add 3, 5", "Disposed"], log.ToImmutableArrayWithLock());
    }

    [Fact(Timeout = 15000)]
    public async Task CallGetProperty()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var tcpHost = new TcpHost();
        tcpHost.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));

        sh.Start();

        var client = new TcpChannel(tcpHost.EndPoint);

        await client.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);
        
        IPacket packet;
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            packet = await client.ReceiveAsync(cts.Token);
        }
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);
        Assert.Equal(gip.RequestId, serviceInstance.RequestId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var gpp = new GetPropertyPacket()
        {
            InstanceId = serviceInstance.InstanceId,
            PropertyId = 1
        };

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gpp, context, CancellationToken.None)), CancellationToken.None);
        
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            packet = await client.ReceiveAsync(cts.Token);
        }

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.IsType<PropertyValuePacket>(rpcp);
        var pvp = (PropertyValuePacket)rpcp;
        Assert.Equal(gpp.RequestId, pvp.RequestId);
        Assert.Equal(await ToBytes(0, context, CancellationToken.None), pvp.Value);
        Assert.Equal<IEnumerable<string>>(["Created", "GetProperty1"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CancellationToken.None);

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "GetProperty1", "Disposed"], log.ToImmutableArrayWithLock());
    }

    [Fact(Timeout = 15000)]
    public async Task CallSetProperty()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var tcpHost = new TcpHost();
        tcpHost.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));

        sh.Start();

        var client = new TcpChannel(tcpHost.EndPoint);

        await client.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);
        
        IPacket packet;
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            packet = await client.ReceiveAsync(cts.Token);
        }
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);
        Assert.Equal(gip.RequestId, serviceInstance.RequestId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var spp = new SetPropertyPacket()
        {
            InstanceId = serviceInstance.InstanceId,
            PropertyId = 1,
            Value = await ToBytes<int>(-5, context, CancellationToken.None)
        };

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(spp, context, CancellationToken.None)), CancellationToken.None);
        
        using (CancellationTokenSource cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            packet = await client.ReceiveAsync(cts.Token);
        }

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.IsType<SuccessPacket>(rpcp);
        var sp = (SuccessPacket)rpcp;
        Assert.Equal(spp.RequestId, sp.RequestId);
        Assert.Equal<IEnumerable<string>>(["Created", "SetProperty1 -5"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CancellationToken.None);

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "SetProperty1 -5", "Disposed"], log.ToImmutableArrayWithLock());
    }

    private static async Task<byte[]> ToBytes<T>(T value, SerializationContext context, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await Serializer.ToStreamAsync(stream, value, context, cancellationToken);
        return stream.ToArray();
    }

    private static async Task<T?> FromBytes<T>(byte[] data, SerializationContext context, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(data);
        return await Serializer.FromStreamAsync<T>(stream, context, cancellationToken);
    }
}
