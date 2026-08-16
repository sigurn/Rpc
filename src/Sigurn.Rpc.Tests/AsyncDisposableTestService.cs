using System.Runtime.CompilerServices;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Tests;

// Support types for the IAsyncDisposable marshaling tests. Adapters and proxies are hand-written
// because the test project references the generator as a plain ProjectReference and therefore never
// runs it (same as TestService.cs).

/// <summary>An async-only resource: the shape objects marshaled as IAsyncDisposable usually have.</summary>
public sealed class TrackedAsyncResource : IAsyncDisposable
{
    private int _disposeAsyncCount;

    public int DisposeAsyncCount => Volatile.Read(ref _disposeAsyncCount);

    public ManualResetEventSlim Disposed { get; } = new(false);

    /// <summary>Lets a test make disposal slow or make it fail.</summary>
    public Func<ValueTask>? OnDisposeAsync { get; set; }

    public async ValueTask DisposeAsync()
    {
        var hook = OnDisposeAsync;
        if (hook is not null)
            await hook().ConfigureAwait(false);

        Interlocked.Increment(ref _disposeAsyncCount);
        Disposed.Set();
    }
}

/// <summary>Implements both disposal contracts: exactly one of them must ever be called.</summary>
public sealed class TrackedBothResource : IDisposable, IAsyncDisposable
{
    private int _disposeCount;
    private int _disposeAsyncCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);
    public int DisposeAsyncCount => Volatile.Read(ref _disposeAsyncCount);

    public ManualResetEventSlim Disposed { get; } = new(false);

    public void Dispose()
    {
        Interlocked.Increment(ref _disposeCount);
        Disposed.Set();
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeAsyncCount);
        Disposed.Set();
        return ValueTask.CompletedTask;
    }
}

/// <summary>One object exposed both as a [RemoteInterface] type and as IAsyncDisposable.</summary>
public sealed class DualRoleResource : ITestNotification, IAsyncDisposable
{
    private int _disposeAsyncCount;

    public int DisposeAsyncCount => Volatile.Read(ref _disposeAsyncCount);

    public List<string> Notifications { get; } = [];

    public void OnNotification(string data)
    {
        lock (Notifications)
            Notifications.Add(data);
    }

    public int NotificationCount
    {
        get { lock (Notifications) return Notifications.Count; }
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeAsyncCount);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Client-side callback used by the subscription scenario.</summary>
public sealed class RecordingNotification : ITestNotification
{
    private readonly List<string> _data = [];
    private readonly ManualResetEventSlim _received = new(false);

    public int Count
    {
        get { lock (_data) return _data.Count; }
    }

    public void OnNotification(string data)
    {
        lock (_data)
            _data.Add(data);

        _received.Set();
    }

    /// <summary>Waits until at least <paramref name="count"/> notifications have been received.</summary>
    public bool WaitFor(int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (Count >= count) return true;
            _received.Wait(TimeSpan.FromMilliseconds(50));
            _received.Reset();
        }

        return Count >= count;
    }
}

/// <summary>A DTO carrying an IAsyncDisposable in a field — hand-written serialization (see ServiceInfo).</summary>
public class ResourceBox : ISerializable
{
    public string Name { get; set; } = string.Empty;

    public IAsyncDisposable? Resource { get; set; }

    public async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        Name = await Serializer.FromStreamAsync<string>(stream, context, cancellationToken).ConfigureAwait(false) ?? string.Empty;
        Resource = await Serializer.FromStreamAsync<IAsyncDisposable>(stream, context, cancellationToken).ConfigureAwait(false);
    }

    public async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, Name, context, cancellationToken).ConfigureAwait(false);
        await Serializer.ToStreamAsync<IAsyncDisposable>(stream, Resource, context, cancellationToken).ConfigureAwait(false);
    }
}

public interface IAsyncResourceService
{
    // The target scenario: subscribe with a callback, get an unsubscribe handle back.
    IAsyncDisposable Subscribe(ITestNotification handler);
    void Notify(string data);
    bool IsSubscribed();

    // Return values.
    IAsyncDisposable GetResource();
    IAsyncDisposable? GetNullResource();
    IAsyncDisposable GetBorrowedResource();
    IAsyncDisposable GetSameResourceAgain();
    int ResourceDisposeCount();

    // Argument direction (client -> server).
    void TakeResource(IAsyncDisposable? resource);
    Task DisposeTakenResourceAsync(CancellationToken cancellationToken);

    // DTO field.
    ResourceBox GetResourceBox();

    // The same object under two interfaces.
    ITestNotification GetDualRole();
    IAsyncDisposable GetDualRoleAsAsyncDisposable();
}

/// <summary>The unsubscribe handle returned by <see cref="AsyncResourceService.Subscribe"/>.</summary>
public sealed class Subscription : IAsyncDisposable
{
    private readonly AsyncResourceService _service;
    private readonly ITestNotification _handler;

    private int _disposeAsyncCount;

    internal Subscription(AsyncResourceService service, ITestNotification handler)
    {
        _service = service;
        _handler = handler;
    }

    public int DisposeAsyncCount => Volatile.Read(ref _disposeAsyncCount);

    public ValueTask DisposeAsync()
    {
        _service.RemoveHandler(_handler);
        Interlocked.Increment(ref _disposeAsyncCount);
        return ValueTask.CompletedTask;
    }
}

public sealed class AsyncResourceService : IAsyncResourceService
{
    private readonly List<ITestNotification> _handlers = [];

    private IAsyncDisposable? _taken;

    public TrackedAsyncResource Resource { get; } = new();

    public TrackedAsyncResource BorrowedResource { get; } = new();

    public TrackedAsyncResource BoxResource { get; } = new();

    public DualRoleResource DualRole { get; } = new();

    public Subscription? LastSubscription { get; private set; }

    public IAsyncDisposable Subscribe(ITestNotification handler)
    {
        lock (_handlers)
            _handlers.Add(handler);

        var subscription = new Subscription(this, handler);
        LastSubscription = subscription;
        return subscription;
    }

    internal void RemoveHandler(ITestNotification handler)
    {
        lock (_handlers)
            _handlers.Remove(handler);
    }

    public void Notify(string data)
    {
        ITestNotification[] handlers;
        lock (_handlers)
            handlers = [.. _handlers];

        foreach (var handler in handlers)
            handler.OnNotification(data);
    }

    public bool IsSubscribed()
    {
        lock (_handlers)
            return _handlers.Count != 0;
    }

    public IAsyncDisposable GetResource() => Resource;

    public IAsyncDisposable? GetNullResource() => null;

    public IAsyncDisposable GetBorrowedResource() => RpcInterface.NoDispose<IAsyncDisposable>(BorrowedResource);

    public IAsyncDisposable GetSameResourceAgain() => Resource;

    public int ResourceDisposeCount() => Resource.DisposeAsyncCount;

    public void TakeResource(IAsyncDisposable? resource) => _taken = resource;

    public async Task DisposeTakenResourceAsync(CancellationToken cancellationToken)
    {
        var taken = _taken;
        _taken = null;
        if (taken is not null)
            await taken.DisposeAsync().ConfigureAwait(false);
    }

    public ResourceBox GetResourceBox() => new() { Name = "box", Resource = BoxResource };

    public ITestNotification GetDualRole() => DualRole;

    public IAsyncDisposable GetDualRoleAsAsyncDisposable() => DualRole;
}

class AsyncResourceServiceAdapter : InterfaceAdapter
{
    [ModuleInitializer]
    public static void MethodInit()
    {
        RegisterAdapter<IAsyncResourceService>(x => new AsyncResourceServiceAdapter(x));
    }

    private readonly IAsyncResourceService _instance;

    public AsyncResourceServiceAdapter(IAsyncResourceService instance)
        : base(typeof(IAsyncResourceService), instance)
    {
        _instance = instance;
    }

    public override async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        switch (methodId)
        {
            case 1:
            {
                var handler = await FromBytesAsync<ITestNotification>(args![0], cancellationToken)
                    ?? throw new ArgumentNullException("handler");
                var subscription = _instance.Subscribe(handler);
                return (await ToBytesAsync<IAsyncDisposable>(subscription, cancellationToken), null);
            }

            case 2:
            {
                var data = await FromBytesAsync<string>(args![0], cancellationToken) ?? string.Empty;
                _instance.Notify(data);
                return (null, null);
            }

            case 3:
                return (await ToBytesAsync(_instance.IsSubscribed(), cancellationToken), null);

            case 4:
                return (await ToBytesAsync<IAsyncDisposable>(_instance.GetResource(), cancellationToken), null);

            case 5:
                return (await ToBytesAsync<IAsyncDisposable>(_instance.GetNullResource(), cancellationToken), null);

            case 6:
                return (await ToBytesAsync<IAsyncDisposable>(_instance.GetBorrowedResource(), cancellationToken), null);

            case 7:
                return (await ToBytesAsync<IAsyncDisposable>(_instance.GetSameResourceAgain(), cancellationToken), null);

            case 8:
                return (await ToBytesAsync(_instance.ResourceDisposeCount(), cancellationToken), null);

            case 9:
            {
                var resource = await FromBytesAsync<IAsyncDisposable>(args![0], cancellationToken);
                _instance.TakeResource(resource);
                return (null, null);
            }

            case 10:
                await _instance.DisposeTakenResourceAsync(cancellationToken).ConfigureAwait(false);
                return (null, null);

            case 11:
                return (await ToBytesAsync(_instance.GetResourceBox(), cancellationToken), null);

            case 12:
                return (await ToBytesAsync<ITestNotification>(_instance.GetDualRole(), cancellationToken), null);

            case 13:
                return (await ToBytesAsync<IAsyncDisposable>(_instance.GetDualRoleAsAsyncDisposable(), cancellationToken), null);
        }

        return (null, null);
    }
}

class AsyncResourceServiceProxy : InterfaceProxy, IAsyncResourceService
{
    [ModuleInitializer]
    public static void MethodInit()
    {
        RegisterProxy<IAsyncResourceService>(x => new AsyncResourceServiceProxy(x));
    }

    public AsyncResourceServiceProxy(Guid instanceId)
        : base(instanceId)
    {
    }

    public IAsyncDisposable Subscribe(ITestNotification handler)
    {
        var (res, _) = InvokeMethod(1, [ToBytes(handler)], false);
        return FromBytes<IAsyncDisposable>(res) ?? throw new InvalidOperationException("Server returned null for Subscribe");
    }

    public void Notify(string data) => InvokeMethod(2, [ToBytes(data)], false);

    public bool IsSubscribed()
    {
        var (res, _) = InvokeMethod(3, [], false);
        return FromBytes<bool>(res);
    }

    public IAsyncDisposable GetResource()
    {
        var (res, _) = InvokeMethod(4, [], false);
        return FromBytes<IAsyncDisposable>(res) ?? throw new InvalidOperationException("Server returned null for GetResource");
    }

    public IAsyncDisposable? GetNullResource()
    {
        var (res, _) = InvokeMethod(5, [], false);
        return FromBytes<IAsyncDisposable>(res);
    }

    public IAsyncDisposable GetBorrowedResource()
    {
        var (res, _) = InvokeMethod(6, [], false);
        return FromBytes<IAsyncDisposable>(res) ?? throw new InvalidOperationException("Server returned null for GetBorrowedResource");
    }

    public IAsyncDisposable GetSameResourceAgain()
    {
        var (res, _) = InvokeMethod(7, [], false);
        return FromBytes<IAsyncDisposable>(res) ?? throw new InvalidOperationException("Server returned null for GetSameResourceAgain");
    }

    public int ResourceDisposeCount()
    {
        var (res, _) = InvokeMethod(8, [], false);
        return FromBytes<int>(res);
    }

    public void TakeResource(IAsyncDisposable? resource) => InvokeMethod(9, [ToBytes(resource)], false);

    public async Task DisposeTakenResourceAsync(CancellationToken cancellationToken)
    {
        await InvokeMethodAsync(10, [], false, cancellationToken);
    }

    public ResourceBox GetResourceBox()
    {
        var (res, _) = InvokeMethod(11, [], false);
        return FromBytes<ResourceBox>(res) ?? throw new InvalidOperationException("Server returned null for GetResourceBox");
    }

    public ITestNotification GetDualRole()
    {
        var (res, _) = InvokeMethod(12, [], false);
        return FromBytes<ITestNotification>(res) ?? throw new InvalidOperationException("Server returned null for GetDualRole");
    }

    public IAsyncDisposable GetDualRoleAsAsyncDisposable()
    {
        var (res, _) = InvokeMethod(13, [], false);
        return FromBytes<IAsyncDisposable>(res) ?? throw new InvalidOperationException("Server returned null for GetDualRoleAsAsyncDisposable");
    }
}

/// <summary>
/// Middleware channel that records every RPC packet crossing it. Sits directly above the transport,
/// so it observes whole RPC packets and can prove which packets a client operation did (not) produce.
/// </summary>
sealed class RecordingChannel : ProcessionChannel
{
    private readonly List<RpcPacket> _sent = [];
    private readonly List<RpcPacket> _received = [];

    public RecordingChannel(IChannel channel)
        : base(channel)
    {
    }

    public IReadOnlyList<RpcPacket> Sent
    {
        get { lock (_sent) return [.. _sent]; }
    }

    public IReadOnlyList<RpcPacket> Received
    {
        get { lock (_received) return [.. _received]; }
    }

    protected override async Task<IPacket> ProcessSendingPacket(IPacket packet, CancellationToken cancellationToken)
    {
        var rpcPacket = await TryParse(packet, cancellationToken).ConfigureAwait(false);
        if (rpcPacket is not null)
            lock (_sent)
                _sent.Add(rpcPacket);

        return packet;
    }

    protected override async Task<IPacket> ProcessReceivedPacket(IPacket packet, CancellationToken cancellationToken)
    {
        var rpcPacket = await TryParse(packet, cancellationToken).ConfigureAwait(false);
        if (rpcPacket is not null)
            lock (_received)
                _received.Add(rpcPacket);

        return packet;
    }

    private static async Task<RpcPacket?> TryParse(IPacket packet, CancellationToken cancellationToken)
    {
        try
        {
            return await RpcPacket.FromPacketAsync(packet, RpcPacket.DefaultSerializationContext, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Not an RPC packet (or a packet type this build cannot parse) — nothing to record.
            return null;
        }
    }
}
