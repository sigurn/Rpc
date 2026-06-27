using System.Net;
using System.Runtime.CompilerServices;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Tests;

// These tests reproduce the bug where session-scoped (and other adapter-owned)
// service instances are never disposed on session teardown.
//
// IMPORTANT: ProbeAdapter intentionally does NOT override Dispose(bool). This
// mirrors exactly what the Roslyn source generator emits for [RemoteInterface]
// adapters in production. The hand-written TestServiceAdapter DOES override it,
// which is why the existing Session tests pass and masked this bug.

public interface IProbe
{
    void Ping();
}

// A process-wide singleton with an event. The session service subscribes in its
// constructor and unsubscribes in Dispose, mirroring the real FirewallManager case.
public sealed class ProbeSingleton
{
    public event EventHandler? SomethingHappened;

    public int SubscriberCount => SomethingHappened?.GetInvocationList().Length ?? 0;
}

public sealed class ProbeState
{
    public int Created;
    public int Disposed;
    public int Attached;
    public int Detached;

    public readonly ManualResetEvent DisposedEvent = new(false);
    public readonly ManualResetEvent DetachedEvent = new(false);
}

public sealed class Probe : IProbe, IDisposable, ISessionsAware
{
    private readonly ProbeState _state;
    private readonly ProbeSingleton _singleton;

    public Probe(ProbeState state, ProbeSingleton singleton)
    {
        _state = state;
        _singleton = singleton;
        Interlocked.Increment(ref _state.Created);
        _singleton.SomethingHappened += OnSomethingHappened;
    }

    public void Ping() { }

    private void OnSomethingHappened(object? sender, EventArgs e) { }

    public void Dispose()
    {
        // Idempotent: a service may be disposed at most once by the framework.
        _singleton.SomethingHappened -= OnSomethingHappened;
        Interlocked.Increment(ref _state.Disposed);
        _state.DisposedEvent.Set();
    }

    void ISessionsAware.AttachSession(ISession session) => Interlocked.Increment(ref _state.Attached);

    void ISessionsAware.DetachSession(ISession session)
    {
        Interlocked.Increment(ref _state.Detached);
        _state.DetachedEvent.Set();
    }
}

// Hand-written adapter that mirrors generated output: it does NOT override Dispose(bool).
sealed class ProbeAdapter : InterfaceAdapter
{
    [ModuleInitializer]
    public static void Init() => RegisterAdapter<IProbe>(x => new ProbeAdapter(x));

    private readonly IProbe _instance;

    public ProbeAdapter(IProbe instance)
        : base(typeof(IProbe), instance)
    {
        _instance = instance;
    }

    public override Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        if (methodId == 1)
            _instance.Ping();

        return Task.FromResult<(byte[]?, IReadOnlyList<byte[]>?)>((null, null));
    }
}

public class SessionDisposalTests
{
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

    // Opens a channel, requests an IProbe instance (forcing its creation server-side)
    // and returns the opened channel.
    private static async Task<TcpChannel> ConnectAndCreateProbe(IPEndPoint endPoint)
    {
        var context = RpcPacket.DefaultSerializationContext;
        var client = new TcpChannel(endPoint);
        await client.OpenAsync(CancellationToken.None);

        var gip = new GetInstancePacket { InterfaceId = typeof(IProbe).GUID };
        await client.SendAsync(new Packet(await ToBytes<RpcPacket>(gip, context, CancellationToken.None)), CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var packet = await client.ReceiveAsync(cts.Token);
        var rpcp = await FromBytes<RpcPacket>(packet.Data, context, CancellationToken.None);
        Assert.IsType<ServiceInstancePacket>(rpcp);

        return client;
    }

    [Fact(Timeout = 15000)]
    public async Task SessionScopedService_IsDisposed_OnDisconnect()
    {
        var state = new ProbeState();
        var singleton = new ProbeSingleton();

        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<IProbe>(ShareWithin.Session, () => new Probe(state, singleton));
        sh.Start();

        var client = await ConnectAndCreateProbe(tcpHost.EndPoint);
        Assert.Equal(1, state.Created);

        await client.CloseAsync(CancellationToken.None);

        Assert.True(state.DisposedEvent.WaitOne(TimeSpan.FromSeconds(5)), "Session-scoped service was not disposed on disconnect");
        Assert.Equal(1, state.Disposed);
    }

    [Fact(Timeout = 15000)]
    public async Task SessionScopedService_GetsDetachSession_OnDisconnect()
    {
        var state = new ProbeState();
        var singleton = new ProbeSingleton();

        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<IProbe>(ShareWithin.Session, () => new Probe(state, singleton));
        sh.Start();

        var client = await ConnectAndCreateProbe(tcpHost.EndPoint);
        Assert.Equal(1, state.Attached);

        await client.CloseAsync(CancellationToken.None);

        Assert.True(state.DetachedEvent.WaitOne(TimeSpan.FromSeconds(5)), "Session-scoped service did not get DetachSession on disconnect");
        Assert.Equal(1, state.Detached);
    }

    [Fact(Timeout = 15000)]
    public async Task SessionScopedService_UnsubscribesFromSingletonEvent_OnDisconnect()
    {
        var state = new ProbeState();
        var singleton = new ProbeSingleton();

        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<IProbe>(ShareWithin.Session, () => new Probe(state, singleton));
        sh.Start();

        var client = await ConnectAndCreateProbe(tcpHost.EndPoint);
        Assert.Equal(1, singleton.SubscriberCount);

        await client.CloseAsync(CancellationToken.None);

        Assert.True(state.DisposedEvent.WaitOne(TimeSpan.FromSeconds(5)), "Session-scoped service was not disposed on disconnect");
        Assert.Equal(0, singleton.SubscriberCount);
    }

    [Fact(Timeout = 15000)]
    public async Task SharedInstance_NotDisposed_WhileAnotherSessionHoldsIt()
    {
        var state = new ProbeState();
        var singleton = new ProbeSingleton();

        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var sh = new ServiceHost(tcpHost);
        sh.RegisterSerive<IProbe>(ShareWithin.Host, () => new Probe(state, singleton));
        sh.Start();

        var client1 = await ConnectAndCreateProbe(tcpHost.EndPoint);
        var client2 = await ConnectAndCreateProbe(tcpHost.EndPoint);
        Assert.Equal(1, state.Created);

        await client1.CloseAsync(CancellationToken.None);
        // The other session still holds the shared instance; it must not be disposed yet.
        Assert.False(state.DisposedEvent.WaitOne(TimeSpan.FromSeconds(1)), "Shared instance was disposed while another session still holds it");
        Assert.Equal(0, state.Disposed);

        await client2.CloseAsync(CancellationToken.None);
        Assert.True(state.DisposedEvent.WaitOne(TimeSpan.FromSeconds(5)), "Shared instance was not disposed after the last session released it");
        Assert.Equal(1, state.Disposed);
    }
}
