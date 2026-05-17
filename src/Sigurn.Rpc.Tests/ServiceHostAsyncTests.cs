using System.Net;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Tests;

public class ServiceHostAsyncTests
{
    private CancellationToken CurrentToken => TestContext.Current.CancellationToken;

    [Fact(Timeout = 15000)]
    public async Task CreateDestroyServiceInstance()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));            }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });
        
        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        IPacket packet = await client.ReceiveAsync(CurrentToken);
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());
        ReleaseInstancePacket rip = new ReleaseInstancePacket()
        {
            InstanceId = serviceInstance.InstanceId
        };

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(rip, context, CurrentToken)), CurrentToken);

        packet = await client.ReceiveAsync(CurrentToken);

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<SuccessPacket>(rpcp);
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());

        cts.Cancel();
        await runTask;
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

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));            }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });
        
        sh.RegisterSerive<ITestService>(share, () => new TestService(log, destroyEvent));

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        IPacket packet = await client.ReceiveAsync(CurrentToken);
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CurrentToken);
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());

        cts.Cancel();
        await runTask;
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

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();        
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));
                }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });

        sh.RegisterSerive<ITestService>(share, () => new TestService(log, destroyEvent));

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        IPacket packet = await client.ReceiveAsync(CurrentToken);
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var rip = new ReleaseInstancePacket()
        {
            InstanceId = serviceInstance.InstanceId
        };
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(rip, context, CurrentToken)), CurrentToken);
        
        packet = await client.ReceiveAsync(CurrentToken);

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<SuccessPacket>(rpcp);
    
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CurrentToken);

        cts.Cancel();
        await runTask;
    }

    [Fact(Timeout = 15000)]
    public async Task CreateAndAutoDestroyMultipleSessionSharedServiceInstances()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));
                }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });

        sh.RegisterSerive<ITestService>(ShareWithin.Session, () => new TestService(log, destroyEvent));

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        IPacket packet = await client.ReceiveAsync(CurrentToken);
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var firstInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, firstInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        packet = await client.ReceiveAsync(CurrentToken);
        
        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var secondInstance = (ServiceInstancePacket)rpcp;
        Assert.Equal(firstInstance.InstanceId, secondInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        packet = await client.ReceiveAsync(CurrentToken);
        
        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var thirdInstance = (ServiceInstancePacket)rpcp;
        Assert.Equal(firstInstance.InstanceId, thirdInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CurrentToken);
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());

        cts.Cancel();
        await runTask;
    }

    [Fact(Timeout = 15000)]
    public async Task CreateAndAutoDestroyMultipleHostSharedServiceInstances()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));
                }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });

        sh.RegisterSerive<ITestService>(ShareWithin.Host, () => new TestService(log, destroyEvent));

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client1 = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client1.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client1.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);

        IPacket packet = await client1.ReceiveAsync(cts.Token);

        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var firstInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, firstInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var client2 = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client2.OpenAsync(CurrentToken);

        await client2.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);

        packet = await client2.ReceiveAsync(CurrentToken);

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var secondInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(firstInstance.InstanceId, secondInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var client3 = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client3.OpenAsync(CurrentToken);

        await client3.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);

        packet = await client3.ReceiveAsync(CurrentToken);

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var thirdInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(firstInstance.InstanceId, thirdInstance.InstanceId);
        Assert.NotEqual(secondInstance.InstanceId, thirdInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client1.CloseAsync(CurrentToken);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client2.CloseAsync(CurrentToken);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client3.CloseAsync(CurrentToken);
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());

        cts.Cancel();
        await runTask;
    }

    [Fact(Timeout = 15000)]
    public async Task CreateAndAutoDestroyMultipleProcessSharedServiceInstances()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));            }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });
        
       sh.RegisterSerive<ITestService>(ShareWithin.Process, () => new TestService(log, destroyEvent));

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client1 = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client1.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client1.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);

        IPacket packet = await client1.ReceiveAsync(cts.Token);

        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var firstInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, firstInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var client2 = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client2.OpenAsync(CurrentToken);

        await client2.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);

        packet = await client2.ReceiveAsync(CurrentToken);

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var secondInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(firstInstance.InstanceId, secondInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var client3 = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client3.OpenAsync(CurrentToken);

        await client3.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);

        packet = await client3.ReceiveAsync(CurrentToken);

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var thirdInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(firstInstance.InstanceId, thirdInstance.InstanceId);
        Assert.NotEqual(secondInstance.InstanceId, thirdInstance.InstanceId);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client1.CloseAsync(CurrentToken);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client2.CloseAsync(CurrentToken);
        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        await client3.CloseAsync(CurrentToken);
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());

        cts.Cancel();
        await runTask;
    }

    [Fact(Timeout = 15000)]
    public async Task CallVoidMethodWithoutArgs()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));
                }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });
        
        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        IPacket packet = await client.ReceiveAsync(CurrentToken);
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
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

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(mcp, context, CurrentToken)), CurrentToken);
        
        packet = await client.ReceiveAsync(CurrentToken);

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.IsType<MethodResultPacket>(rpcp);
        var mrp = (MethodResultPacket)rpcp;
        Assert.Equal(mcp.RequestId, mrp.RequestId);
        Assert.Null(mrp.Result);
        Assert.Null(mrp.Args);
        Assert.Equal<IEnumerable<string>>(["Created", "Method1"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CurrentToken);

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Method1", "Disposed"], log.ToImmutableArrayWithLock());

        cts.Cancel();
        await runTask;
    }

    [Fact(Timeout = 15000)]
    public async Task CallIntMethodWithIntArgs()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));            }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });

        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        IPacket packet = await client.ReceiveAsync(CurrentToken);
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
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
            Args = [await ToBytes(3, context, CurrentToken), await ToBytes(5, context, CurrentToken)]
        };

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(mcp, context, CurrentToken)), CurrentToken);
        
        packet = await client.ReceiveAsync(CurrentToken);

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.IsType<MethodResultPacket>(rpcp);
        var mrp = (MethodResultPacket)rpcp;
        Assert.Equal(mcp.RequestId, mrp.RequestId);
        Assert.Equal(await ToBytes(8, context, CurrentToken), mrp.Result);
        Assert.Null(mrp.Args);
        Assert.Equal<IEnumerable<string>>(["Created", "Add 3, 5"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CurrentToken);

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "Add 3, 5", "Disposed"], log.ToImmutableArrayWithLock());

        cts.Cancel();
        await runTask;
    }

    [Fact(Timeout = 15000)]
    public async Task CallGetProperty()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));
                }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });

        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        IPacket packet = await client.ReceiveAsync(CurrentToken);
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
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

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gpp, context, CurrentToken)), CurrentToken);
        
        packet = await client.ReceiveAsync(CurrentToken);

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.IsType<PropertyValuePacket>(rpcp);
        var pvp = (PropertyValuePacket)rpcp;
        Assert.Equal(gpp.RequestId, pvp.RequestId);
        Assert.Equal(await ToBytes(0, context, CurrentToken), pvp.Value);
        Assert.Equal<IEnumerable<string>>(["Created", "GetProperty1"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CurrentToken);

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "GetProperty1", "Disposed"], log.ToImmutableArrayWithLock());

        cts.Cancel();
        await runTask;
    }

    [Fact(Timeout = 15000)]
    public async Task CallSetProperty()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));
                }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });

        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(ITestService).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        IPacket packet = await client.ReceiveAsync(CurrentToken);
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
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
            Value = await ToBytes<int>(-5, context, CurrentToken)
        };

        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(spp, context, CurrentToken)), CurrentToken);
        
        packet = await client.ReceiveAsync(CurrentToken);

        rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.IsType<SuccessPacket>(rpcp);
        var sp = (SuccessPacket)rpcp;
        Assert.Equal(spp.RequestId, sp.RequestId);
        Assert.Equal<IEnumerable<string>>(["Created", "SetProperty1 -5"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(CurrentToken);

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal<IEnumerable<string>>(["Created", "SetProperty1 -5", "Disposed"], log.ToImmutableArrayWithLock());

        cts.Cancel();
        await runTask;
    }

    [Fact(Timeout = 15000)]
    public async Task GetServicesCatalog()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));
                }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });

        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log));
        sh.PublishServicesCatalog = true;

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(IServiceCatalog).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        IPacket packet = await client.ReceiveAsync(CurrentToken);
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ServiceInstancePacket>(rpcp);
        var serviceInstance = (ServiceInstancePacket)rpcp;
        Assert.NotEqual(Guid.Empty, serviceInstance.InstanceId);

        cts.Cancel();
        await runTask;
    }

    [Fact(Timeout = 15000)]
    public async Task ServicesCatalogIsNotAvailable()
    {
        List<string> log = new ();
        using ManualResetEvent destroyEvent = new (false);

        var endPoint = new IPEndPoint(IPAddress.Loopback, 0);
        var endPointReady = new TaskCompletionSource<IPEndPoint?>();
        var sh = new ServiceHostAsync();
        sh.RegisterAcceptor(() =>
        {
            var acceptor = TcpHostAsync.Open(endPoint);
            try
            {
                if (acceptor is ILocalAddress la)
                {
                    endPointReady.SetResult(IPEndPoint.Parse(la.LocalAddress));
                }
                else
                {
                    endPointReady.SetResult(null);
                }
            }
            catch(Exception ex)
            {
                endPointReady.SetException(ex);
            }

            return acceptor;
        });

        sh.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log));
        sh.PublishServicesCatalog = false;

        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(CurrentToken);

        var runTask = sh.RunAsync(cts.Token);

        var client = new TcpChannel(await endPointReady.Task ?? throw new Exception("Address is not awailable"));

        await client.OpenAsync(CurrentToken);

        var gip = new GetInstancePacket()
        {
            InterfaceId = typeof(IServiceCatalog).GUID
        };
        var context = RpcPacket.DefaultSerializationContext;
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CurrentToken)), CurrentToken);
        
        IPacket packet = await client.ReceiveAsync(CurrentToken);
        
        RpcPacket? rpcp = await FromBytes<RpcPacket>(packet.Data, context, CurrentToken);
        Assert.NotNull(rpcp);
        Assert.IsType<ExceptionPacket>(rpcp);
        var ep = (ExceptionPacket)rpcp;
        Assert.Equal("Requested service is not available", ep.Message);
        Assert.Equal("System.Exception", ep.Type);

        cts.Cancel();
        await runTask;
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
