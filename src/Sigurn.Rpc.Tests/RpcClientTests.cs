using System.Net;
using Moq;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Tests;

public class RpcClientTests
{
    public Task<IChannel> EmptyFactory(CancellationToken cancellationToken)
    {
        return Task.FromResult<IChannel>(new TcpChannel(new IPEndPoint(IPAddress.Loopback, 0)));
    }

    [Fact]
    public void CreateInstance()
    {
        using RpcClient client = new RpcClient(EmptyFactory);
        Assert.Equal(ChannelState.Created, client.State);
    }

    [Fact(Timeout = 15000)]
    public async Task ConnectToAndDisconnectFromServer()
    {
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.Start();

        var log = new List<string>();
        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        client.Opening += (s, e) => log.AddWithLock("Opening");
        client.Opened += (s, e) => log.AddWithLock("Opened");
        client.Closing += (s, e) => log.AddWithLock("Closing");
        client.Closed += (s, e) => log.AddWithLock("Closed");
        client.Faulted += (s, e) => log.AddWithLock("Faulted");

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));
        Assert.Equal(ChannelState.Created, client.State);
        await client.OpenAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Opened, client.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened"], log.ToImmutableArrayWithLock());
        await client.CloseAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Closed, client.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened", "Closing", "Closed"], log.ToImmutableArrayWithLock());

        serviceHost.Stop();
        host.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task ServerBreaksConnection()
    {
        using ManualResetEvent faultedEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.Start();

        var log = new List<string>();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        client.AutoReopen = false;

        client.Opening += (s, e) => log.AddWithLock("Opening");
        client.Opened += (s, e) => log.AddWithLock("Opened");
        client.Closing += (s, e) => log.AddWithLock("Closing");
        client.Closed += (s, e) => log.AddWithLock("Closed");
        client.Faulted += (s, e) =>
        {
            log.AddWithLock("Faulted");
            faultedEvent.Set();
        };

        Assert.Equal(ChannelState.Created, client.State);

        await client.OpenAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Opened, client.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened"], log.ToImmutableArrayWithLock());

        serviceHost.Stop();

        Assert.True(faultedEvent.WaitOne(TimeSpan.FromSeconds(5)));

        await client.CloseAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Closed, client.State);
        lock (log)
            Assert.Equal<IEnumerable<string>>(["Opening", "Opened", "Faulted", "Closing", "Closed"], log.ToImmutableArrayWithLock());

        host.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task AutoReconnectToServerConnection()
    {
        using ManualResetEvent faultedEvent = new ManualResetEvent(false);
        using ManualResetEvent openedEvent = new ManualResetEvent(false);

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.Start();

        var log = new List<string>();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        client.AutoReopen = true;
        client.ReopenInterval = TimeSpan.FromSeconds(1);

        client.Opening += (s, e) => log.AddWithLock("Opening");
        client.Opened += (s, e) =>
        {
            log.AddWithLock("Opened");
            openedEvent.Set();
        };
        client.Closing += (s, e) => log.AddWithLock("Closing");
        client.Closed += (s, e) => log.AddWithLock("Closed");
        client.Faulted += (s, e) =>
        {
            log.AddWithLock("Faulted");
            faultedEvent.Set();
        };

        Assert.Equal(ChannelState.Created, client.State);

        await client.OpenAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Opened, client.State);
        Assert.Equal<IEnumerable<string>>(["Opening", "Opened"], log.ToImmutableArrayWithLock());

        serviceHost.Stop();

        Assert.True(faultedEvent.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Contains("Faulted", log.ToImmutableArrayWithLock());

        openedEvent.Reset();
        serviceHost.Start();

        Assert.True(openedEvent.WaitOne(TimeSpan.FromSeconds(5)));

        await client.CloseAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Closed, client.State);
        lock (log)
            Assert.Equal<IEnumerable<string>>(["Opening", "Opened", "Faulted", "Opening", "Opened", "Closing", "Closed"], log.ToImmutableArrayWithLock());

        host.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task GetInstanceFromServerAndDestroyInstance()
    {
        var log = new List<string>();

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using ManualResetEvent faultedEvent = new ManualResetEvent(false);
        using ManualResetEvent openedEvent = new ManualResetEvent(false);

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        client.AutoReopen = false;

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        Assert.NotNull(testService);
        Assert.IsAssignableFrom<IDisposable>(testService);
        Assert.True(InterfaceProxy.IsInterfaceProxy(testService));
        Assert.NotNull(InterfaceProxy.GetChannel(testService));

        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        ((IDisposable)testService).Dispose();
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));

        Assert.Equal<IEnumerable<string>>(["Created", "Disposed"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Closed, client.State);

        host.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task InvokeVoidMethodWithoutArgs()
    {
        var log = new List<string>();

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        Assert.NotNull(testService);
        Assert.IsAssignableFrom<IDisposable>(testService);

        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        testService.Method1();

        Assert.Equal<IEnumerable<string>>(["Created", "Method1"], log.ToImmutableArrayWithLock());

        ((IDisposable)testService).Dispose();
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));

        Assert.Equal<IEnumerable<string>>(["Created", "Method1", "Disposed"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Closed, client.State);

        host.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task InvokeIntMethodWithArgs()
    {
        var log = new List<string>();

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        Assert.NotNull(testService);
        Assert.IsAssignableFrom<IDisposable>(testService);

        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var res = testService.Add(5, 18);

        Assert.Equal<IEnumerable<string>>(["Created", "Add 5, 18"], log.ToImmutableArrayWithLock());
        Assert.Equal(23, res);

        ((IDisposable)testService).Dispose();
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));

        Assert.Equal<IEnumerable<string>>(["Created", "Add 5, 18", "Disposed"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Closed, client.State);

        host.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task GetProperty()
    {
        var log = new List<string>();

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        Assert.NotNull(testService);
        Assert.IsAssignableFrom<IDisposable>(testService);

        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        var res = testService.Property1;

        Assert.Equal<IEnumerable<string>>(["Created", "GetProperty1"], log.ToImmutableArrayWithLock());
        Assert.Equal(0, res);

        ((IDisposable)testService).Dispose();
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));

        Assert.Equal<IEnumerable<string>>(["Created", "GetProperty1", "Disposed"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Closed, client.State);

        host.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task SetProperty()
    {
        var log = new List<string>();

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        Assert.NotNull(testService);
        Assert.IsAssignableFrom<IDisposable>(testService);

        Assert.Equal<IEnumerable<string>>(["Created"], log.ToImmutableArrayWithLock());

        testService.Property1 = 98;

        Assert.Equal<IEnumerable<string>>(["Created", "SetProperty1 98"], log.ToImmutableArrayWithLock());

        ((IDisposable)testService).Dispose();
        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(5)));

        Assert.Equal<IEnumerable<string>>(["Created", "SetProperty1 98", "Disposed"], log.ToImmutableArrayWithLock());

        await client.CloseAsync(cancellationTokenSource.Token);
        Assert.Equal(ChannelState.Closed, client.State);

        host.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task MethodThrowsExcepton()
    {
        var mock = new Mock<ITestService>();

        mock.Setup(x => x.Method1())
            .Throws(new Exception("Test exception"));

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => mock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        var ex = Assert.Throws<RpcServerException>(() => testService.Method1());
        Assert.Equal("System.Exception", ex.ServerExceptionType);
        Assert.Equal("Test exception", ex.ServerExceptionMessage);
        Assert.NotNull(ex.ServerExceptionStack);
    }

    [Fact(Timeout = 15000)]
    public async Task GetPropertyExcepton()
    {
        var mock = new Mock<ITestService>();

        mock.Setup(x => x.Property1)
            .Throws(new Exception("Test exception"));

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => mock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        var ex = Assert.Throws<RpcServerException>(() => testService.Property1);
        Assert.Equal("System.Exception", ex.ServerExceptionType);
        Assert.Equal("Test exception", ex.ServerExceptionMessage);
        Assert.NotNull(ex.ServerExceptionStack);
    }

    [Fact(Timeout = 15000)]
    public async Task SetPropertyExcepton()
    {
        var mock = new Mock<ITestService>();

        mock.SetupSet(x => x.Property1 = 15)
            .Throws(new Exception("Test exception"));

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => mock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        var ex = Assert.Throws<RpcServerException>(() => testService.Property1 = 15);
        Assert.Equal("System.Exception", ex.ServerExceptionType);
        Assert.Equal("Test exception", ex.ServerExceptionMessage);
        Assert.NotNull(ex.ServerExceptionStack);
    }

    [Fact(Timeout = 15000)]
    public async Task CallMethodWithOutputArg()
    {
        var mock = new Mock<ITestService>();

        string testString = "Test string";
        mock.Setup(x => x.GetString(out testString));

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => mock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        testService.GetString(out string outString);
        Assert.Equal(testString, outString);
    }

    [Fact(Timeout = 15000)]
    public async Task CallMethodWithRefArg()
    {
        var mock = new Mock<ITestService>();

        string testString = "New string";
        mock.Setup(x => x.ModifyString(ref It.Ref<string>.IsAny))
            .Callback((ref string text) =>
            {
                text = testString;
            })
            .Returns(true);

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => mock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        string text = testString;
        Assert.True(testService.ModifyString(ref text));
        Assert.Equal(testString, text);
    }

    [Fact(Timeout = 15000)]
    public async Task InvokeAsyncVoidMethodWithoutArgs()
    {
        var mock = new Mock<ITestService>();
        mock.Setup(x => x.Method1Async(It.IsAny<CancellationToken>()));

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => mock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        await testService.Method1Async(CancellationToken.None);

        mock.Verify(x => x.Method1Async(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 15000)]
    public async Task InvokeAsyncIntMethodWithArgs()
    {
        var mock = new Mock<ITestService>();
        mock.Setup(x => x.AddAsync(5, 18, It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(23));

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => mock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        var res = await testService.AddAsync(5, 18, CancellationToken.None);

        Assert.Equal(23, res);

        mock.Verify(x => x.AddAsync(5, 18, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(Timeout = 15000)]
    public async Task SubscribeForAndUnsubscribeFromServiceEvent()
    {
        TaskCompletionSource addTcs = new TaskCompletionSource();
        TaskCompletionSource removeTcs = new TaskCompletionSource();

        var mock = new Mock<ITestService>();
        mock.SetupAdd(x =>
        {
            x.TestEvent += It.IsAny<EventHandler>();
            addTcs.SetResult();
        });
        mock.SetupRemove(x =>
        {
            x.TestEvent -= It.IsAny<EventHandler>();
            removeTcs.SetResult();
        });

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => mock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        EventHandler handler = (object? sender, EventArgs args) => { };

        testService.TestEvent += handler;
        testService.TestEvent += handler;
        await addTcs.Task.WaitAsync(cancellationTokenSource.Token);

        testService.TestEvent -= handler;
        testService.TestEvent -= handler;
        await removeTcs.Task.WaitAsync(cancellationTokenSource.Token);

        mock.VerifyAdd(x => x.TestEvent += It.IsAny<EventHandler>(), Times.Once);
        mock.VerifyRemove(x => x.TestEvent -= It.IsAny<EventHandler>(), Times.Once);
    }

    [Fact(Timeout = 15000)]
    public async Task GetEventFromService()
    {
        List<string> log = new();

        AutoResetEvent receivedEvent = new AutoResetEvent(false);

        var mock = new Mock<ITestService>();

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => mock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        EventHandler handler = (object? sender, EventArgs args) =>
        {
            log.AddWithLock("TestEvent");
            receivedEvent.Set();
        };

        testService.TestEvent += handler;

        mock.Raise(x => x.TestEvent += null, EventArgs.Empty);
        Assert.True(receivedEvent.WaitOne(TimeSpan.FromSeconds(2)));

        mock.Raise(x => x.TestEvent += null, EventArgs.Empty);
        Assert.True(receivedEvent.WaitOne(TimeSpan.FromSeconds(2)));

        testService.TestEvent -= handler;

        mock.Raise(x => x.TestEvent += null, EventArgs.Empty);

        Assert.Equal("TestEvent, TestEvent", string.Join(", ", log.ToImmutableArrayWithLock()));
    }

    [Fact(Timeout = 15000)]
    public async Task PassInterfaceToService()
    {
        List<string> log = new();

        AutoResetEvent receivedEvent = new AutoResetEvent(false);

        var nmock = new Mock<ITestNotification>();

        nmock.Setup(x => x.OnNotification(It.IsAny<string>()));

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        testService.Subscribe(nmock.Object);

        testService.NotifySubscribers("Test1");

        testService.Unsubscribe(nmock.Object);

        nmock.Verify(x => x.OnNotification("Test1"), Times.Once);
    }

    [Fact(Timeout = 15000)]
    public async Task PassNullInterfaceInstanceToService()
    {
        List<string> log = new();

        AutoResetEvent receivedEvent = new AutoResetEvent(false);

        List<ITestNotification> subscriptons = new List<ITestNotification>();

        var smock = new Mock<ITestService>();
        smock.Setup(x => x.Subscribe(null));

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive(ShareWithin.None, () => smock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        testService.Subscribe(null);

        smock.Verify(x => x.Subscribe(null), Times.Once);
    }

    [Fact(Timeout = 15000)]
    public async Task PassSameInterfaceInstanceToService()
    {
        List<string> log = new();

        AutoResetEvent receivedEvent = new AutoResetEvent(false);

        List<ITestNotification> subscriptons = new List<ITestNotification>();

        var smock = new Mock<ITestService>();

        smock.Setup(x => x.Subscribe(It.IsAny<ITestNotification>()))
            .Callback((ITestNotification x) => subscriptons.Add(x));

        smock.Setup(x => x.Unsubscribe(It.IsAny<ITestNotification>()))
            .Callback((ITestNotification x) =>
            {
                subscriptons.Remove(x);
            });

        var nmock = new Mock<ITestNotification>();
        nmock.Setup(x => x.OnNotification(It.IsAny<string>()));

        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive(ShareWithin.None, () => smock.Object);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        testService.Subscribe(nmock.Object);
        testService.Subscribe(nmock.Object);

        Assert.Equal(2, subscriptons.Count);
        Assert.Equal(subscriptons[0], subscriptons[1]);

        foreach (var s in subscriptons)
            s.OnNotification("First call");

        nmock.Verify(x => x.OnNotification("First call"), Times.Exactly(2));

        testService.Unsubscribe(nmock.Object);

        foreach (var s in subscriptons)
            s.OnNotification("Second call");

        testService.Unsubscribe(nmock.Object);

        Assert.Empty(subscriptons);

        nmock.Verify(x => x.OnNotification("First call"), Times.Exactly(2));
        nmock.Verify(x => x.OnNotification("Second call"), Times.Once);
    }

    [Fact(Timeout = 15000)]
    public async Task ServiceIsDestroyedWhenInterfaceIsNotUsedOnClient()
    {
        List<string> log = new();
        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var fun = () =>
        {
            var testService = client.GetService<ITestService>(cancellationTokenSource.Token).Result;
            Assert.Equal(8, testService.Add(3, 5));
        };

        fun();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.Collect();

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(2)));
        Assert.Equal("Created, Add 3, 5, Disposed", string.Join(", ", log.ToImmutableArrayWithLock()));
    }

    [Fact(Timeout = 15000)]
    public async Task ServiceIsDestroyedWhenInterfaceIsReleasedOnClient()
    {
        List<string> log = new();
        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);
        Assert.Equal(8, testService.Add(3, 5));

        if (testService is IDisposable d)
            d.Dispose();

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(2)));
        Assert.Equal("Created, Add 3, 5, Disposed", string.Join(", ", log.ToImmutableArrayWithLock()));
    }

    [Fact(Timeout = 15000)]
    public async Task SameSessionServiceIsDestroyedWhenAllClientsAreReleased()
    {
        List<string> log = new();
        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.Session, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService1 = await client.GetService<ITestService>(cancellationTokenSource.Token);
        var testService2 = await client.GetService<ITestService>(cancellationTokenSource.Token);

        Assert.Equal(testService1, testService2);
        Assert.Equal(testService1, testService2);

        Assert.Equal(8, testService1.Add(3, 5));

        Assert.Equal(12, testService2.Add(5, 7));

        if (testService1 is IDisposable d1)
            d1.Dispose();

        Assert.Equal(25, testService2.Add(15, 10));

        if (testService2 is IDisposable d2)
            d2.Dispose();

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(2)));
        Assert.Equal("Created, Add 3, 5, Add 5, 7, Add 15, 10, Disposed", string.Join(", ", log.ToImmutableArrayWithLock()));
    }

    [Fact(Timeout = 15000)]
    public async Task SameHostServiceIsDestroyedWhenAllClientsAreReleased()
    {
        List<string> log = new();
        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.Host, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client1 = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client1.OpenAsync(cancellationTokenSource.Token);

        var testService1 = await client1.GetService<ITestService>(cancellationTokenSource.Token);

        using RpcClient client2 = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client2.OpenAsync(cancellationTokenSource.Token);

        var testService2 = await client2.GetService<ITestService>(cancellationTokenSource.Token);

        Assert.NotEqual(testService1, testService2);
        Assert.NotSame(testService1, testService2);

        Assert.Equal(8, testService1.Add(3, 5));

        Assert.Equal(12, testService2.Add(5, 7));

        if (testService1 is IDisposable d1)
            d1.Dispose();

        Assert.Equal(25, testService2.Add(15, 10));

        if (testService2 is IDisposable d2)
            d2.Dispose();

        Assert.True(destroyEvent.WaitOne(TimeSpan.FromSeconds(2)));
        Assert.Equal("Created, Add 3, 5, Add 5, 7, Add 15, 10, Disposed", string.Join(", ", log.ToImmutableArrayWithLock()));
    }

    [Fact(Timeout = 15000)]
    public async Task CloseClientAfterExceptionOnServer()
    {
        List<string> log = new();
        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.Host, () => new TestService(log, destroyEvent));
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var testService = await client.GetService<ITestService>(cancellationTokenSource.Token);

        await Assert.ThrowsAsync<RpcServerException>(() => testService.MethodThrowAsync(cancellationTokenSource.Token));

        client.Dispose();
    }

    [Fact(Timeout = 15000)]
    public async Task GetListOfService()
    {
        List<string> log = new();
        using ManualResetEvent destroyEvent = new ManualResetEvent(false);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.Host, () => new TestService(log, destroyEvent));
        serviceHost.PublishServicesCatalog = true;
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client.OpenAsync(cancellationTokenSource.Token);

        var catalog = await client.GetService<IServiceCatalog>(cancellationTokenSource.Token);
        Assert.NotNull(catalog);
        var services = await catalog.GetServicesAsync(CancellationToken.None);

        Assert.NotNull(services);
        Assert.Single(services);
        Assert.Equal(typeof(ITestService), services[0].InterfaceType);
        Assert.Equal(ShareWithin.Host, services[0].ShareType);
        Assert.Equal(typeof(ITestService).FullName, services[0].InterfaceName);

        client.Dispose();
    }
}