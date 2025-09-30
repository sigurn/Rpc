using System.Collections.Immutable;
using System.Net;

namespace Sigurn.Rpc.Tests;

public class RpcEventContextTests
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

        public event EventHandler? TestEvent;

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

        public void RaiseTestEventNoSession()
        {
            TestEvent?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseTestEventExcludeSessions(params ISession[] sessions)
        {
            using (sessions.ExcludeSessions())
            {
                TestEvent?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RaiseTestEventIncludeSessions(params ISession[] sessions)
        {
            using (sessions.IncludeSessions())
            {
                TestEvent?.Invoke(this, EventArgs.Empty);
            }
        }

        private readonly List<ISession> _sessions = [];

        public IReadOnlyList<ISession> Sessions
        {
            get
            {
                lock (_sessions)
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
    public async Task NoContextTest()
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

        using ManualResetEvent service1Event = new ManualResetEvent(false);
        testService1.TestEvent += (s, e) =>
        {
            service1Event.Set();
        };

        using ManualResetEvent service2Event = new ManualResetEvent(false);
        testService2.TestEvent += (s, e) =>
        {
            service2Event.Set();
        };

        service.RaiseTestEventNoSession();

        Assert.True(WaitHandle.WaitAll([service1Event, service2Event], TimeSpan.FromSeconds(1)));

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
    public async Task ExcludeContextTest()
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

        using ManualResetEvent service1Event = new ManualResetEvent(false);
        testService1.TestEvent += (s, e) =>
        {
            service1Event.Set();
        };

        using ManualResetEvent service2Event = new ManualResetEvent(false);
        testService2.TestEvent += (s, e) =>
        {
            service2Event.Set();
        };

        service.RaiseTestEventExcludeSessions(service.Sessions[0]);

        Assert.False(service1Event.WaitOne(TimeSpan.FromMilliseconds(200)));
        Assert.True(service2Event.WaitOne(TimeSpan.FromSeconds(1)));

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
    public async Task IncludeContextTest()
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

        using ManualResetEvent service1Event = new ManualResetEvent(false);
        testService1.TestEvent += (s, e) =>
        {
            service1Event.Set();
        };

        using ManualResetEvent service2Event = new ManualResetEvent(false);
        testService2.TestEvent += (s, e) =>
        {
            service2Event.Set();
        };

        service.RaiseTestEventIncludeSessions(service.Sessions[1]);

        Assert.False(service1Event.WaitOne(TimeSpan.FromMilliseconds(200)));
        Assert.True(service2Event.WaitOne(TimeSpan.FromSeconds(1)));

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
    public async Task ExcludeIncludeContextTest()
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

        using ManualResetEvent service1Event = new ManualResetEvent(false);
        testService1.TestEvent += (s, e) =>
        {
            service1Event.Set();
        };

        using ManualResetEvent service2Event = new ManualResetEvent(false);
        testService2.TestEvent += (s, e) =>
        {
            service2Event.Set();
        };

        service.RaiseTestEventExcludeSessions(service.Sessions[0], service.Sessions[1]);

        Assert.Equal(WaitHandle.WaitTimeout, WaitHandle.WaitAny([service1Event, service2Event], TimeSpan.FromMilliseconds(200)));

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
    public async Task MultipleIncludeContextTest()
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

        using ManualResetEvent service1Event = new ManualResetEvent(false);
        testService1.TestEvent += (s, e) =>
        {
            service1Event.Set();
        };

        using ManualResetEvent service2Event = new ManualResetEvent(false);
        testService2.TestEvent += (s, e) =>
        {
            service2Event.Set();
        };

        service.RaiseTestEventIncludeSessions(service.Sessions[0], service.Sessions[1]);

        Assert.True(service1Event.WaitOne(TimeSpan.FromSeconds(1)));
        Assert.True(service2Event.WaitOne(TimeSpan.FromSeconds(1)));

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
}