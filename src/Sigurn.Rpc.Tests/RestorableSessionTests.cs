using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.Infrastructure.Packets;

namespace Sigurn.Rpc.Tests;

// A two-event probe used to exercise the batch re-subscription path on reopen. Hand-written
// Adapter/Proxy mirror what the generator emits (see EventProbe in SharedEventSubscriptionTests).
public interface IDualProbe
{
    event EventHandler First;
    event EventHandler Second;
}

public sealed class DualProbe : IDualProbe
{
    private EventHandler? _first;
    private EventHandler? _second;

    public event EventHandler First { add => _first += value; remove => _first -= value; }
    public event EventHandler Second { add => _second += value; remove => _second -= value; }

    public int FirstSubscribers => _first?.GetInvocationList().Length ?? 0;
    public int SecondSubscribers => _second?.GetInvocationList().Length ?? 0;

    public void RaiseFirst() => _first?.Invoke(this, EventArgs.Empty);
    public void RaiseSecond() => _second?.Invoke(this, EventArgs.Empty);
}

sealed class DualProbeAdapter : InterfaceAdapter
{
    [ModuleInitializer]
    public static void Init() => RegisterAdapter<IDualProbe>(x => new DualProbeAdapter(x));

    private readonly IDualProbe _instance;

    public DualProbeAdapter(IDualProbe instance)
        : base(typeof(IDualProbe), instance)
    {
        _instance = instance;
    }

    public override Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        if (eventId == 1) { _instance.First += OnFirst; return Task.CompletedTask; }
        if (eventId == 2) { _instance.Second += OnSecond; return Task.CompletedTask; }
        return Task.FromException(new ArgumentException("Unknown event", nameof(eventId)));
    }

    public override Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        if (eventId == 1) { _instance.First -= OnFirst; return Task.CompletedTask; }
        if (eventId == 2) { _instance.Second -= OnSecond; return Task.CompletedTask; }
        return Task.FromException(new ArgumentException("Unknown event", nameof(eventId)));
    }

    private void OnFirst(object? sender, EventArgs e) => SendEvent(1);
    private void OnSecond(object? sender, EventArgs e) => SendEvent(2);
}

sealed class DualProbeProxy : InterfaceProxy, IDualProbe
{
    [ModuleInitializer]
    public static void Init() => RegisterProxy<IDualProbe>(x => new DualProbeProxy(x));

    public DualProbeProxy(Guid instanceId)
        : base(instanceId)
    {
    }

    private EventHandler? _first;
    private EventHandler? _second;

    public event EventHandler First
    {
        add { _first += value; AttachEventHandler(1); }
        remove { _first -= value; DetachEventHandler(1); }
    }

    public event EventHandler Second
    {
        add { _second += value; AttachEventHandler(2); }
        remove { _second -= value; DetachEventHandler(2); }
    }

    protected override void OnEvent(int eventId, IReadOnlyList<byte[]> args)
    {
        if (eventId == 1) _first?.Invoke(this, EventArgs.Empty);
        else if (eventId == 2) _second?.Invoke(this, EventArgs.Empty);
    }
}

// A client-side ITestNotification implementation that records disposal — used to observe what
// happens to instances passed to the server across a reopen.
public sealed class DisposableNotification : ITestNotification, IDisposable
{
    public readonly ManualResetEventSlim Disposed = new(false);
    public void OnNotification(string data) { }
    public void Dispose() => Disposed.Set();
}

// A server-side ITestNotification implementation returned from ITestService.GetSubService.
sealed class NoopNotification : ITestNotification
{
    public void OnNotification(string data) { }
}

public class RestorableSessionTests
{
    private static CancellationToken CurrentToken => TestContext.Current.CancellationToken;

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    // The reconnect factory reads host.EndPoint INSIDE the lambda because Stop()/Start() rebinds to a
    // fresh ephemeral port (TcpHost.Close nulls the listening endpoint).
    private static RpcClient NewClient(TcpHost host) => new(async ct =>
    {
        var ch = new TcpChannel(host.EndPoint);
        await ch.OpenAsync(ct);
        return ch;
    })
    {
        AutoReopen = true,
        ReopenInterval = TimeSpan.FromSeconds(1),
    };

    // Same as NewClient but with a session-initialize hook wired through the constructor.
    private static RpcClient NewClient(TcpHost host, Func<ISessionInitializer, CancellationToken, Task> hook) =>
        new(hook, async ct =>
        {
            var ch = new TcpChannel(host.EndPoint);
            await ch.OpenAsync(ct);
            return ch;
        })
        {
            AutoReopen = true,
            ReopenInterval = TimeSpan.FromSeconds(1),
        };

    [Fact(Timeout = 30000)]
    public async Task SessionInitializeHook_GatesRestore_MakesRpcCalls_AndReleasesTransientProxies()
    {
        CounterService.Live = 0;

        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.Session, () => new TestService(new List<string>()));
        sh.RegisterSerive<ICounterService>(ShareWithin.None, () => new CounterService());
        sh.Start();

        int hookCalls = 0;
        bool failHook = false;
        int hookPing = 0;

        await using var client = NewClient(tcpHost, async (ctx, ct) =>
        {
            Interlocked.Increment(ref hookCalls);
            Assert.NotNull(ctx.Channel);                          // (a) channel access
            var svc = await ctx.GetService<ICounterService>(ct);  // (b) full RPC: obtain a proxy...
            Volatile.Write(ref hookPing, svc.Ping());             //     ...and call a method on it
            if (Volatile.Read(ref failHook)) throw new InvalidOperationException("hook boom");
        });

        // Phase 1: the hook runs on the FIRST/primary connect, with a working RPC call.
        await client.OpenAsync(CurrentToken);
        Assert.Equal(1, Volatile.Read(ref hookCalls));
        Assert.Equal(42, Volatile.Read(ref hookPing));
        // The transient proxy obtained in the hook is released once the hook returns -> the server-side
        // ShareWithin.None instance is disposed. Nothing keeps it alive across the hook boundary.
        Assert.True(WaitUntil(() => CounterService.Live == 0, TimeSpan.FromSeconds(5)),
            "Transient hook proxy was not released after the initial hook");

        var service = await client.GetService<ITestService>(CurrentToken);
        Assert.Equal(5, service.Add(2, 3));

        // Phase 2: the hook FAILS on reconnect -> restore must be gated, the root proxy must not recover,
        // and the reopen must keep retrying (calling the hook again each attempt).
        Volatile.Write(ref failHook, true);
        var callsBefore = Volatile.Read(ref hookCalls);
        sh.Stop();
        sh.Start();

        Assert.True(WaitUntil(() => Volatile.Read(ref hookCalls) >= callsBefore + 2, TimeSpan.FromSeconds(15)),
            "Hook was not retried across repeated failed reopens");
        Assert.False(WaitUntil(() =>
        {
            try { return service.Add(4, 5) == 9; }
            catch { return false; }
        }, TimeSpan.FromSeconds(3)), "Restore ran even though the hook was failing");
        // Proxies acquired by a failing hook are released each attempt, so they never accumulate: at most one
        // (the in-flight attempt) is alive at any instant despite many failed reconnects.
        Assert.True(CounterService.Live <= 1, $"Failing-hook transient proxies accumulated: {CounterService.Live}");

        // Phase 3: let the hook succeed -> restore runs and the SAME proxy recovers (hook-before-restore),
        // and restore never re-requests the transient service.
        Volatile.Write(ref failHook, false);
        Assert.True(WaitUntil(() =>
        {
            try { return service.Add(4, 5) == 9; }
            catch { return false; }
        }, TimeSpan.FromSeconds(15)), "Root proxy did not recover after the hook started succeeding");
        // Once quiescent, the last successful hook has released its transient proxy and restore only
        // re-requested the restorable ITestService — never the transient ICounterService.
        Assert.True(WaitUntil(() => CounterService.Live == 0, TimeSpan.FromSeconds(5)),
            "Transient hook proxies were not all released after recovery");

        sh.Stop();
        tcpHost.Close();
    }

    [Fact(Timeout = 20000)]
    public async Task Reopen_RestoresMethodCalls()
    {
        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.Session, () => new TestService(new List<string>()));
        sh.Start();

        await using var client = NewClient(tcpHost);
        await client.OpenAsync(CurrentToken);
        var service = await client.GetService<ITestService>(CurrentToken);

        Assert.Equal(5, service.Add(2, 3));

        // Fault, then bring the server back for auto-reopen.
        sh.Stop();
        Assert.True(WaitUntil(() => client.State == ChannelState.Faulted || client.State == ChannelState.Opening,
            TimeSpan.FromSeconds(5)), "Client never faulted after server stop");
        sh.Start();

        // The SAME proxy must work again after the session is restored.
        Assert.True(WaitUntil(() =>
        {
            try { return service.Add(4, 5) == 9; }
            catch { return false; }
        }, TimeSpan.FromSeconds(10)), "Method call did not recover after reopen");

        sh.Stop();
        tcpHost.Close();
    }

    [Fact(Timeout = 20000)]
    public async Task Reopen_ResubscribesEvents()
    {
        DualProbe? probe = null;
        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<IDualProbe>(ShareWithin.Session, () => probe = new DualProbe());
        sh.Start();

        await using var client = NewClient(tcpHost);
        await client.OpenAsync(CurrentToken);
        var dp = await client.GetService<IDualProbe>(CurrentToken);

        int first = 0;
        dp.First += (s, e) => Interlocked.Increment(ref first);
        Assert.NotNull(probe);
        Assert.Equal(1, probe!.FirstSubscribers);

        sh.Stop();
        sh.Start();

        // After reopen the new server instance must be re-subscribed exactly once.
        Assert.True(WaitUntil(() => probe is not null && probe.FirstSubscribers == 1, TimeSpan.FromSeconds(10)),
            "Event was not re-subscribed (exactly once) on the new instance after reopen");

        var before = Volatile.Read(ref first);
        Assert.True(WaitUntil(() =>
        {
            probe!.RaiseFirst();
            return Volatile.Read(ref first) > before;
        }, TimeSpan.FromSeconds(5)), "Event not delivered to the existing handler after reopen");

        sh.Stop();
        tcpHost.Close();
    }

    [Fact(Timeout = 20000)]
    public async Task Reopen_ResubscribesMultipleEventsInOneRoundTrip()
    {
        DualProbe? probe = null;
        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<IDualProbe>(ShareWithin.Session, () => probe = new DualProbe());
        sh.Start();

        await using var client = NewClient(tcpHost);
        await client.OpenAsync(CurrentToken);
        var dp = await client.GetService<IDualProbe>(CurrentToken);

        int a = 0, b = 0;
        dp.First += (s, e) => Interlocked.Increment(ref a);
        dp.Second += (s, e) => Interlocked.Increment(ref b);
        Assert.NotNull(probe);
        Assert.Equal(1, probe!.FirstSubscribers);
        Assert.Equal(1, probe!.SecondSubscribers);

        sh.Stop();
        sh.Start();

        // Both events must be restored on the new instance, each exactly once (no duplicates).
        Assert.True(WaitUntil(() => probe is not null && probe.FirstSubscribers == 1 && probe.SecondSubscribers == 1,
            TimeSpan.FromSeconds(10)), "Both events were not re-subscribed exactly once after reopen");

        var a0 = Volatile.Read(ref a);
        var b0 = Volatile.Read(ref b);
        Assert.True(WaitUntil(() =>
        {
            probe!.RaiseFirst();
            probe!.RaiseSecond();
            return Volatile.Read(ref a) > a0 && Volatile.Read(ref b) > b0;
        }, TimeSpan.FromSeconds(5)), "Both events not delivered after reopen");

        sh.Stop();
        tcpHost.Close();
    }

    [Fact]
    public async Task SubscribeForEventsPacket_RoundTrips()
    {
        var ctx = RpcPacket.DefaultSerializationContext;
        var id = Guid.NewGuid();
        var packet = new SubscribeForEventsPacket
        {
            InstanceId = id,
            EventIds = [3, 7, 11],
        };

        var bytes = await packet.ToBytesAsync(ctx, CurrentToken);
        var roundtrip = await RpcPacket.FromPacketAsync(IPacket.Create(bytes), ctx, CurrentToken);

        var typed = Assert.IsType<SubscribeForEventsPacket>(roundtrip);
        Assert.Equal(id, typed.InstanceId);
        Assert.Equal(new[] { 3, 7, 11 }, typed.EventIds);
    }

    [Fact(Timeout = 20000)]
    public async Task Reopen_InvalidatesDerivedProxy()
    {
        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.Session, () =>
        {
            var s = new TestService(new List<string>());
            s.SetSubServiceFactory(() => new NoopNotification());
            return s;
        });
        sh.Start();

        await using var client = NewClient(tcpHost);
        await client.OpenAsync(CurrentToken);
        var service = await client.GetService<ITestService>(CurrentToken);

        var sub = service.GetSubService();
        sub.OnNotification("before"); // works before the fault
        Assert.True(RpcInterface.IsValid(sub));

        sh.Stop();
        sh.Start();

        // Wait until the root proxy has recovered, so the reopen/restore has run.
        Assert.True(WaitUntil(() =>
        {
            try { service.Add(1, 1); return true; }
            catch { return false; }
        }, TimeSpan.FromSeconds(10)), "Root proxy did not recover after reopen");

        // The derived proxy cannot be restored — it must fail fast with a clear exception.
        Assert.False(RpcInterface.IsValid(sub));
        Assert.Throws<RpcInvalidInstanceException>(() => sub.OnNotification("after"));

        sh.Stop();
        tcpHost.Close();
    }

    [Fact(Timeout = 20000)]
    public async Task IsValid_ReflectsProxyState()
    {
        Assert.False(RpcInterface.IsValid("not a proxy"));
        Assert.False(RpcInterface.IsValid<object?>(null));

        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.Session, () => new TestService(new List<string>()));
        sh.Start();

        await using var client = NewClient(tcpHost);
        await client.OpenAsync(CurrentToken);
        var service = await client.GetService<ITestService>(CurrentToken);

        Assert.True(RpcInterface.IsValid(service));

        sh.Stop();
        sh.Start();

        Assert.True(WaitUntil(() =>
        {
            try { service.Add(1, 1); return true; }
            catch { return false; }
        }, TimeSpan.FromSeconds(10)), "Root proxy did not recover after reopen");

        // A restored root proxy stays valid.
        Assert.True(RpcInterface.IsValid(service));

        sh.Stop();
        tcpHost.Close();
    }

    [Fact(Timeout = 20000)]
    public async Task Reopen_DisposesOwnedInterfacePassedToServer()
    {
        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.Session, () => new TestService(new List<string>()));
        sh.Start();

        await using var client = NewClient(tcpHost);
        await client.OpenAsync(CurrentToken);
        var service = await client.GetService<ITestService>(CurrentToken);

        var callback = new DisposableNotification();
        service.Subscribe(callback); // server retains it in _subscriptions

        sh.Stop();
        sh.Start();

        // On reopen the client's exposed adapter is torn down; an owned instance is disposed.
        Assert.True(callback.Disposed.Wait(TimeSpan.FromSeconds(10)),
            "Owned interface passed to the server was not disposed on reopen");

        sh.Stop();
        tcpHost.Close();
    }

    [Fact(Timeout = 20000)]
    public async Task Reopen_DoesNotDisposeNoDisposeInterfacePassedToServer()
    {
        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<ITestService>(ShareWithin.Session, () => new TestService(new List<string>()));
        sh.Start();

        await using var client = NewClient(tcpHost);
        await client.OpenAsync(CurrentToken);
        var service = await client.GetService<ITestService>(CurrentToken);

        var callback = new DisposableNotification();
        service.Subscribe(RpcInterface.NoDispose<ITestNotification>(callback)); // borrowed — keep ownership

        // Ensure the reopen has happened and torn down exposed adapters.
        sh.Stop();
        sh.Start();
        Assert.True(WaitUntil(() =>
        {
            try { service.Add(1, 1); return true; }
            catch { return false; }
        }, TimeSpan.FromSeconds(10)), "Root proxy did not recover after reopen");

        // Give the teardown a moment; a borrowed instance must NOT be disposed.
        Thread.Sleep(200);
        Assert.False(callback.Disposed.IsSet, "Borrowed interface passed to the server was disposed on reopen");

        sh.Stop();
        tcpHost.Close();
    }

    [Fact]
    public void RpcInterface_NoDispose_ReturnsSameInstance_AndConsumesOnce()
    {
        var obj = new DisposableNotification();

        Assert.Same(obj, RpcInterface.NoDispose<ITestNotification>(obj));

        Assert.True(RpcInterface.ConsumeBorrowed(obj));   // consumed
        Assert.False(RpcInterface.ConsumeBorrowed(obj));  // already consumed
        Assert.False(RpcInterface.ConsumeBorrowed(new DisposableNotification())); // never marked
    }
}
