using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Tests;

// Reproduces the event-subscription leaks on SHARED (Host/Process) services when a
// subscribing session disconnects without explicitly unsubscribing:
//
//   Layer A: the client's subscription (server does `_instance.Event += OnEvent`) is
//            never undone, so the shared instance's invocation list grows across
//            reconnects.
//   Layer B: the per-session lambda wired onto the adapter's EventTriggered (in
//            Session.RegisterInstance) is never removed, so the next event raise pushes
//            to the closed session's channel -> "The channel is not opened".
//
// EventProbeAdapter intentionally does NOT override Dispose(bool): it mirrors generated
// adapter output.

public interface IEventProbe
{
    event EventHandler Tick;
    void Ping();
}

public sealed class EventProbe : IEventProbe, IDisposable
{
    public readonly ManualResetEvent DisposedEvent = new(false);

    private EventHandler? _tick;

    public event EventHandler Tick
    {
        add => _tick += value;
        remove => _tick -= value;
    }

    public int TickSubscriberCount => _tick?.GetInvocationList().Length ?? 0;

    public void Raise() => _tick?.Invoke(this, EventArgs.Empty);

    public void Ping() { }

    public void Dispose() => DisposedEvent.Set();
}

sealed class EventProbeAdapter : InterfaceAdapter
{
    [ModuleInitializer]
    public static void Init() => RegisterAdapter<IEventProbe>(x => new EventProbeAdapter(x));

    private readonly IEventProbe _instance;

    public EventProbeAdapter(IEventProbe instance)
        : base(typeof(IEventProbe), instance)
    {
        _instance = instance;
    }

    public override Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        if (methodId == 1)
            _instance.Ping();

        return Task.FromResult<(byte[]?, IReadOnlyList<byte[]>?)>((null, null));
    }

    public override Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        if (eventId == 1)
        {
            _instance.Tick += OnTick;
            return Task.CompletedTask;
        }

        return Task.FromException(new ArgumentException("Unknown event", nameof(eventId)));
    }

    public override Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        if (eventId == 1)
        {
            _instance.Tick -= OnTick;
            return Task.CompletedTask;
        }

        return Task.FromException(new ArgumentException("Unknown event", nameof(eventId)));
    }

    private void OnTick(object? sender, EventArgs e) => SendEvent(1);
}

sealed class EventProbeProxy : InterfaceProxy, IEventProbe
{
    [ModuleInitializer]
    public static void Init() => RegisterProxy<IEventProbe>(x => new EventProbeProxy(x));

    public EventProbeProxy(Guid instanceId)
        : base(instanceId)
    {
    }

    public void Ping() => InvokeMethod(1, [], false);

    private EventHandler? _tick;
    public event EventHandler Tick
    {
        add
        {
            _tick += value;
            AttachEventHandler(1);
        }
        remove
        {
            _tick -= value;
            DetachEventHandler(1);
        }
    }

    protected override void OnEvent(int eventId, IReadOnlyList<byte[]> args)
    {
        if (eventId == 1)
            _tick?.Invoke(this, EventArgs.Empty);
    }
}

public class SharedEventSubscriptionTests
{
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

    private static RpcClient NewClient(IPEndPoint endPoint) => new(async ct =>
    {
        var ch = new TcpChannel(endPoint);
        await ch.OpenAsync(ct);
        return ch;
    });

    [Fact(Timeout = 15000)]
    public async Task SharedService_RemovesClientEventSubscription_OnDisconnect()
    {
        EventProbe? instance = null;

        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<IEventProbe>(ShareWithin.Host, () => instance = new EventProbe());
        sh.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var client = NewClient(tcpHost.EndPoint);
        await client.OpenAsync(cts.Token);
        var proxy = await client.GetService<IEventProbe>(cts.Token);

        proxy.Tick += (s, e) => { };
        Assert.NotNull(instance);
        Assert.Equal(1, instance.TickSubscriberCount);

        await client.CloseAsync(cts.Token);
        client.Dispose();

        Assert.True(instance.DisposedEvent.WaitOne(TimeSpan.FromSeconds(5)), "Shared instance was not disposed on last disconnect");
        // The adapter must undo the client's event subscription on teardown.
        Assert.True(WaitUntil(() => instance.TickSubscriberCount == 0, TimeSpan.FromSeconds(5)),
            "Client event subscription was not removed from the shared instance on disconnect");

        tcpHost.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task SharedServiceEvent_RaiseAfterSubscriberDisconnects_DoesNotThrow()
    {
        EventProbe? instance = null;

        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<IEventProbe>(ShareWithin.Host, () => instance = new EventProbe());
        sh.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var client = NewClient(tcpHost.EndPoint);
        await client.OpenAsync(cts.Token);
        var proxy = await client.GetService<IEventProbe>(cts.Token);

        proxy.Tick += (s, e) => { };
        Assert.NotNull(instance);

        await client.CloseAsync(cts.Token);
        client.Dispose();

        Assert.True(instance.DisposedEvent.WaitOne(TimeSpan.FromSeconds(5)), "Shared instance was not disposed on last disconnect");

        // Layer B: a leftover EventTriggered lambda would push to the closed channel and throw.
        var ex = Record.Exception(() => instance.Raise());
        Assert.Null(ex);

        tcpHost.Close();
    }

    [Fact(Timeout = 20000)]
    public async Task SharedServiceEvent_DeliveredOnlyToOpenSessions_AfterOneDisconnects()
    {
        EventProbe? instance = null;

        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<IEventProbe>(ShareWithin.Host, () => instance = new EventProbe());
        sh.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var client1 = NewClient(tcpHost.EndPoint);
        await client1.OpenAsync(cts.Token);
        var proxy1 = await client1.GetService<IEventProbe>(cts.Token);

        var client2 = NewClient(tcpHost.EndPoint);
        await client2.OpenAsync(cts.Token);
        var proxy2 = await client2.GetService<IEventProbe>(cts.Token);

        int c1 = 0, c2 = 0;
        proxy1.Tick += (s, e) => Interlocked.Increment(ref c1);
        // AttachEventHandler is a synchronous round-trip, so both sessions are subscribed
        // server-side once these returns.
        proxy2.Tick += (s, e) => Interlocked.Increment(ref c2);

        Assert.NotNull(instance);

        instance.Raise();
        Assert.True(WaitUntil(() => Volatile.Read(ref c1) >= 1 && Volatile.Read(ref c2) >= 1, TimeSpan.FromSeconds(5)), "Both clients should receive the first event");

        await client1.CloseAsync(cts.Token);
        client1.Dispose();

        // Sync on teardown: while session1's fan-out lambda is still attached, raising
        // pushes to its closed channel and throws. Once teardown removes it (layer B), the
        // raise succeeds.
        Assert.True(WaitUntil(() =>
        {
            try { instance.Raise(); return true; }
            catch { return false; }
        }, TimeSpan.FromSeconds(5)), "Raising kept throwing: closed session's fan-out was not removed on disconnect");

        // Settle any in-flight deliveries from earlier raises, then verify that further
        // events reach only the still-open session.
        Thread.Sleep(100);
        var c1Settled = Volatile.Read(ref c1);
        var c2Settled = Volatile.Read(ref c2);

        instance.Raise();

        Assert.True(WaitUntil(() => Volatile.Read(ref c2) > c2Settled, TimeSpan.FromSeconds(5)), "Open session stopped receiving events");
        Thread.Sleep(100);
        Assert.Equal(c1Settled, Volatile.Read(ref c1));

        await client2.CloseAsync(cts.Token);
        client2.Dispose();
        tcpHost.Close();
    }
}
