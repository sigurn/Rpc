using System.Collections.Immutable;
using System.Net;
using Moq;

namespace Sigurn.Rpc.Tests;

public class RpcSessionTests
{
    private class TestServiceInstance : ITestService, ISessionsAware
    {
        private readonly EventWaitHandle _sessionChangedEvent;

        public TestServiceInstance(EventWaitHandle sessionChnagedEvent)
        {
            _sessionChangedEvent = sessionChnagedEvent;
        }

        public int Property1
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

#pragma warning disable CS0067
        public event EventHandler? TestEvent;
#pragma warning restore CS0067

        public int Add(int a, int b)
        {
            throw new NotImplementedException();
        }

        public Task<int> AddAsync(int a, int b, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public void GetString(out string text)
        {
            throw new NotImplementedException();
        }

        public void Method1()
        {
            throw new NotImplementedException();
        }

        public Task Method1Async(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public bool ModifyString(ref string text)
        {
            throw new NotImplementedException();
        }

        public void NotifySubscribers(string data)
        {
            throw new NotImplementedException();
        }

        public void Subscribe(ITestNotification? handler)
        {
            throw new NotImplementedException();
        }

        public void Unsubscribe(ITestNotification handler)
        {
            throw new NotImplementedException();
        }

        private readonly List<ISession> _sessions = [];

        public IReadOnlyList<ISession> Sessions
        {
            get
            {
                lock(_sessions)
                    return _sessions.ToImmutableArray();
            }
        }

        public void AttachSession(ISession session)
        {
            lock (_sessions)
                _sessions.Add(session);
            _sessionChangedEvent.Set();
        }

        public void DetachSession(ISession session)
        {
            lock (_sessions)
                _sessions.Remove(session);
            _sessionChangedEvent.Set();
        }
    }

    public Task<IChannel> EmptyFactory(CancellationToken cancellationToken)
    {
        return Task.FromResult<IChannel>(new TcpChannel(new IPEndPoint(IPAddress.Loopback, 0)));
    }

    [Fact(Timeout = 15000)]
    public async Task SessionNotificationsTest()
    {
        AutoResetEvent sessionChangedEvent = new AutoResetEvent(false);
        var service = new TestServiceInstance(sessionChangedEvent);

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.Host, () => service);
        serviceHost.Start();

        using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(5));

        using RpcClient client1 = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        using RpcClient client2 = new RpcClient(async cancellationToken =>
        {
            var channel = new TcpChannel(host.EndPoint);
            await channel.OpenAsync(cancellationToken);
            return channel;
        });

        await client1.OpenAsync(cancellationTokenSource.Token);
        await client2.OpenAsync(cancellationTokenSource.Token);

        Assert.Empty(service.Sessions);

        var testService1 = await client1.GetService<ITestService>(cancellationTokenSource.Token);
        Assert.True(sessionChangedEvent.WaitOne(TimeSpan.FromSeconds(1)));

        Assert.Single(service.Sessions);

        var testService2 = await client2.GetService<ITestService>(cancellationTokenSource.Token);
        Assert.True(sessionChangedEvent.WaitOne(TimeSpan.FromSeconds(1)));

        Assert.Equal(2, service.Sessions.Count);

        ((IDisposable)testService1).Dispose();
        Assert.True(sessionChangedEvent.WaitOne(TimeSpan.FromSeconds(1)));

        Assert.Single(service.Sessions);

        ((IDisposable)testService2).Dispose();
        Assert.True(sessionChangedEvent.WaitOne(TimeSpan.FromSeconds(1)));

        Assert.Empty(service.Sessions);

        await client1.CloseAsync(cancellationTokenSource.Token);
        await client2.CloseAsync(cancellationTokenSource.Token);

        host.Close();
    }


    [Fact(Timeout = 15000)]
    public async Task MethodCallsHaveSessionAtMethodBeginning()
    {
        Mock<ITestService> service = new Mock<ITestService>();

        service.Setup(x => x.Method1())
            .Callback(() =>
            {
                var session = Session.Current;
                Assert.NotNull(session);
            });

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => service.Object);
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

        testService.Method1();

        ((IDisposable)testService).Dispose();

        await client.CloseAsync(cancellationTokenSource.Token);

        host.Close();

        service.Verify();
    }

    [Fact(Timeout = 15000)]
    public async Task MethodCallsHaveSessionAtMethodEnd()
    {
        Mock<ITestService> service = new Mock<ITestService>();

        service.Setup(x => x.Method1Async(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(async (ct) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
                var session = Session.Current;
                Assert.NotNull(session);
            });

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => service.Object);
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

        await testService.Method1Async(cancellationTokenSource.Token);

        ((IDisposable)testService).Dispose();

        await client.CloseAsync(cancellationTokenSource.Token);

        host.Close();

        service.Verify();
    }

    [Fact(Timeout = 15000)]
    public async Task PropertyGettersHaveSession()
    {
        Mock<ITestService> service = new Mock<ITestService>();

        service.SetupGet(x => x.Property1)
            .Callback(() =>
            {
                var session = Session.Current;
                Assert.NotNull(session);
            })
            .Returns(5);
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => service.Object);
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

        Assert.Equal(5, testService.Property1);

        ((IDisposable)testService).Dispose();

        await client.CloseAsync(cancellationTokenSource.Token);

        host.Close();

        service.Verify();
    }

    [Fact(Timeout = 15000)]
    public async Task PropertySettersHaveSession()
    {
        Mock<ITestService> service = new Mock<ITestService>();

        service.SetupSet(x => x.Property1 = 15)
            .Callback(() =>
            {
                var session = Session.Current;
                Assert.NotNull(session);
            });
        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => service.Object);
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

        testService.Property1 = 15;

        ((IDisposable)testService).Dispose();

        await client.CloseAsync(cancellationTokenSource.Token);

        host.Close();

        service.Verify();
    }

    [Fact(Timeout = 15000)]
    public async Task EventAdderHaveSession()
    {
        Mock<ITestService> service = new Mock<ITestService>();

        service.SetupAdd(x => x.TestEvent += It.IsAny<EventHandler>())
            .Callback(() =>
            {
                var session = Session.Current;
                Assert.NotNull(session);
            });

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => service.Object);
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

        testService.TestEvent += (s, e) => { };

        ((IDisposable)testService).Dispose();

        await client.CloseAsync(cancellationTokenSource.Token);

        host.Close();

        service.Verify();
    }

    [Fact(Timeout = 15000)]
    public async Task EventRemoverHaveSession()
    {
        Mock<ITestService> service = new Mock<ITestService>();

        service.SetupRemove(x => x.TestEvent -= It.IsAny<EventHandler>())
            .Callback(() =>
            {
                var session = Session.Current;
                Assert.NotNull(session);
            });

        using TcpHost host = new TcpHost();
        host.EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
        ServiceHost serviceHost = new ServiceHost(host);
        serviceHost.RegisterSerive<ITestService>(ShareWithin.None, () => service.Object);
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

        testService.TestEvent -= (s, e) => { };

        ((IDisposable)testService).Dispose();

        await client.CloseAsync(cancellationTokenSource.Token);

        host.Close();

        service.Verify();
    }
}