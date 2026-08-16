using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Tests;

// A session starts handling packets from inside its own constructor (RpcHandler begins receiving as
// soon as it is created). Anything the request path needs must therefore be ready BEFORE the handler
// exists — most importantly the serialization context: when it is missing, Sigurn.Serialize silently
// falls back to the default context, which cannot marshal interfaces or streams, and the request fails
// with "Cannot find serializer for type ...".
public class SessionConstructionTests
{
    [Fact(Timeout = 15000)]
    public async Task SerializationContext_IsAvailable_ToRequestsArrivingDuringConstruction()
    {
        var ct = TestContext.Current.CancellationToken;

        var packet = await RpcPacket.ToBytesAsync<RpcPacket>(
            new GetInstancePacket { InterfaceId = typeof(ITestNotification).GUID },
            RpcPacket.DefaultSerializationContext,
            ct);

        var channel = new ImmediatePacketChannel(new Packet(packet));
        var serviceHost = new SingleServiceHost(typeof(ITestNotification), () => new NoopNotification());

        using var session = new Session(channel, serviceHost);

        Assert.True(serviceHost.FactoryCalled.Wait(TimeSpan.FromSeconds(5), ct),
            "The request was never dispatched — the test cannot detect anything");

        Assert.NotNull(serviceHost.ContextWhenServiceWasCreated);
        Assert.IsType<RpcSerializationContext>(serviceHost.ContextWhenServiceWasCreated);
    }

    private sealed class NoopNotification : ITestNotification
    {
        public void OnNotification(string data) { }
    }

    // Hands the session one packet the moment it starts receiving, then never returns another.
    private sealed class ImmediatePacketChannel : IChannel, IDisposable
    {
        private readonly IPacket _packet;
        private int _delivered;

        public ImmediatePacketChannel(IPacket packet) => _packet = packet;

        public ChannelState State => ChannelState.Opened;

        public object? BoundObject { get; set; }

#pragma warning disable CS0067 // interface-mandated events this test double never raises
        public event EventHandler? Opening;
        public event EventHandler? Opened;
        public event EventHandler? Closing;
        public event EventHandler? Closed;
        public event EventHandler? Faulted;
#pragma warning restore CS0067

        public Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IPacket> SendAsync(IPacket packet, CancellationToken cancellationToken) => Task.FromResult(packet);

        // The first call completes synchronously, so the packet is picked up while the session
        // constructor is still inside `new RpcHandler(...)`. The second call — which runs on the
        // constructor's own thread — holds that thread for a moment, so the request is guaranteed to
        // be handled before the constructor gets to finish initializing the session. That ordering is
        // exactly what a real connection produces when a request arrives immediately after connecting;
        // here it is made deterministic instead of accidental.
        public Task<IPacket> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _delivered, 1) == 0)
                return Task.FromResult(_packet);

            Thread.Sleep(TimeSpan.FromMilliseconds(300));

            return Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => (IPacket)null!, TaskContinuationOptions.ExecuteSynchronously);
        }

        public void Dispose() { }
    }

    private sealed class SingleServiceHost : IServiceHost
    {
        private readonly Type _type;
        private readonly Func<object> _factory;

        public SingleServiceHost(Type type, Func<object> factory)
        {
            _type = type;
            _factory = factory;
        }

        public readonly ManualResetEventSlim FactoryCalled = new(false);

        public SerializationContext? ContextWhenServiceWasCreated { get; private set; }

        public Type? FindTypeById(Guid id) => id == _type.GUID ? _type : null;

        public (ShareWithin Shared, Func<object> Factory) GetServiceInfo(Type type)
            => (ShareWithin.None, () =>
            {
                // Runs on the request path: the session must already be able to serialize by now.
                ContextWhenServiceWasCreated = (Session.Current as Session)?.SerializationContext;
                var instance = _factory();
                FactoryCalled.Set();
                return instance;
            });

        public RefCounter<ICallTarget> CreateGlobalInstance(Type type, Func<ICallTarget> factory)
            => throw new NotSupportedException();

        public RefCounter<ICallTarget> CreateHostInstance(Type type, Func<ICallTarget> factory)
            => throw new NotSupportedException();
    }
}
