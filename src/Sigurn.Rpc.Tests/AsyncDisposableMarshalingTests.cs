using System.Net;
using System.Runtime.CompilerServices;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Rpc.Infrastructure.Packets;

namespace Sigurn.Rpc.Tests;

public class AsyncDisposableMarshalingTests
{
    // --- Harness ------------------------------------------------------------------------

    private sealed class Harness : IAsyncDisposable
    {
        public required TcpHost TcpHost { get; init; }
        public required RpcClient Client { get; init; }
        public required RecordingChannel Recorder { get; init; }
        public required AsyncResourceService Service { get; init; }
        public required IAsyncResourceService Proxy { get; init; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Client.DisposeAsync();
            }
            catch
            {
                // The test may have closed the client already.
            }

            TcpHost.Close();
        }
    }

    private static async Task<Harness> StartAsync(AsyncResourceService service, CancellationToken cancellationToken)
    {
        var tcpHost = new TcpHost { EndPoint = new IPEndPoint(IPAddress.Loopback, 0) };
        var serviceHost = new ServiceHost(tcpHost);
        serviceHost.RegisterSerive<IAsyncResourceService>(ShareWithin.None, () => service);
        serviceHost.Start();

        RecordingChannel? recorder = null;
        var client = new RpcClient(async ct =>
        {
            var channel = new RecordingChannel(new TcpChannel(tcpHost.EndPoint));
            recorder = channel;
            await channel.OpenAsync(ct);
            return channel;
        });

        await client.OpenAsync(cancellationToken);
        var proxy = await client.GetService<IAsyncResourceService>(cancellationToken);

        return new Harness
        {
            TcpHost = tcpHost,
            Client = client,
            Recorder = recorder!,
            Service = service,
            Proxy = proxy,
        };
    }

    private static IReadOnlyList<RpcPacket> SentSince(Harness harness, int mark)
        => [.. harness.Recorder.Sent.Skip(mark)];

    // --- A. Registration and serializer -------------------------------------------------

    [Fact]
    public void InterfaceSerializer_SupportsIAsyncDisposable()
    {
        Assert.True(new InterfaceSerializer().IsTypeSupported(typeof(IAsyncDisposable)));
    }

    [Fact]
    public void AdapterAndProxy_AreRegistered_ForIAsyncDisposable()
    {
        Assert.True(InterfaceAdapter.IsThereAdapterFor<IAsyncDisposable>());
        Assert.True(InterfaceProxy.IsThereProxyFor<IAsyncDisposable>());
    }

    // Guards that IAsyncDisposable is a narrow special case: every other interface still needs
    // [RemoteInterface], even when an adapter and a proxy are registered for it (ITestService has both).
    [Fact]
    public void InterfaceSerializer_StillRejects_InterfaceWithoutRemoteInterfaceAttribute()
    {
        Assert.True(InterfaceAdapter.IsThereAdapterFor<ITestService>());
        Assert.True(InterfaceProxy.IsThereProxyFor<ITestService>());

        Assert.False(new InterfaceSerializer().IsTypeSupported(typeof(ITestService)));
    }

    [Fact]
    public async Task AsyncDisposableAdapter_RejectsMethodPropertyAndEventDispatch()
    {
        var resource = new TrackedAsyncResource();
        var adapter = InterfaceAdapter.CreateAdapter(typeof(IAsyncDisposable), resource, RpcPacket.DefaultSerializationContext);

        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => adapter.InvokeMethodAsync(0, [], false, ct));
        Assert.Contains("IAsyncDisposable", ex.Message);

        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.GetPropertyValueAsync(0, ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.SetPropertyValueAsync(0, null, ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.AttachEventHandlerAsync(0, ct));
        await Assert.ThrowsAsync<NotSupportedException>(() => adapter.DetachEventHandlerAsync(0, ct));

        // Nothing was disposed by a failed dispatch.
        Assert.Equal(0, resource.DisposeAsyncCount);
    }

    // --- B. The subscription scenario ---------------------------------------------------

    [Fact(Timeout = 15000)]
    public async Task Subscription_IsRemovedOnServer_WhenClientDisposesTheHandle()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var callback = new RecordingNotification();
        var subscription = harness.Proxy.Subscribe(callback);

        harness.Proxy.Notify("first");
        Assert.True(callback.WaitFor(1, TimeSpan.FromSeconds(5)), "The callback was not invoked while subscribed");
        Assert.True(harness.Proxy.IsSubscribed());

        await subscription.DisposeAsync();

        // The release is confirmed by the server, so by the time the await returns the unsubscribe
        // has already happened — no polling, no sleeps.
        Assert.False(harness.Proxy.IsSubscribed());
        Assert.Equal(1, service.LastSubscription!.DisposeAsyncCount);

        harness.Proxy.Notify("second");
        Assert.False(callback.WaitFor(2, TimeSpan.FromMilliseconds(500)), "The callback was invoked after unsubscribing");
        Assert.Equal(1, callback.Count);
    }

    [Fact(Timeout = 15000)]
    public async Task SubscriptionHandle_DisposeAsync_SendsReleaseInstance_AndNeverAMethodCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var subscription = harness.Proxy.Subscribe(new RecordingNotification());
        var instanceId = ((InterfaceProxy)subscription).InstanceId;

        var mark = harness.Recorder.Sent.Count;
        await subscription.DisposeAsync();
        var sent = SentSince(harness, mark);

        var releases = sent.OfType<ReleaseInstancePacket>().ToList();
        Assert.Single(releases);
        Assert.Equal(instanceId, releases[0].InstanceId);

        // The whole point: disposing the handle is a release, not a remote DisposeAsync call.
        Assert.Empty(sent.OfType<MethodCallPacket>());
        Assert.Empty(sent.OfType<GetPropertyPacket>());
        Assert.Empty(sent.OfType<SubscribeForEventPacket>());
    }

    // --- C. Return values, core semantics -----------------------------------------------

    [Fact(Timeout = 15000)]
    public async Task ReturnedAsyncDisposable_IsDisposedOnServer_WhenClientDisposesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var resource = harness.Proxy.GetResource();
        Assert.False(service.Resource.Disposed.IsSet, "Disposed before the client released it");

        await resource.DisposeAsync();

        Assert.Equal(1, service.Resource.DisposeAsyncCount);
    }

    [Fact(Timeout = 15000)]
    public async Task DisposeAsync_CompletesOnlyAfterServerFinishedDisposing()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        service.Resource.OnDisposeAsync = async () => await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        await using var harness = await StartAsync(service, ct);

        var resource = harness.Proxy.GetResource();
        await resource.DisposeAsync();

        // A fresh round-trip: the server must already report the resource as disposed.
        Assert.Equal(1, harness.Proxy.ResourceDisposeCount());
    }

    [Fact(Timeout = 15000)]
    public async Task DisposeAsync_IsIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var resource = harness.Proxy.GetResource();
        var mark = harness.Recorder.Sent.Count;

        await resource.DisposeAsync();
        await resource.DisposeAsync();

        Assert.Single(SentSince(harness, mark).OfType<ReleaseInstancePacket>());
        Assert.Equal(1, harness.Proxy.ResourceDisposeCount());
    }

    [Fact(Timeout = 15000)]
    public async Task Dispose_ThenDisposeAsync_ReleasesExactlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var resource = harness.Proxy.GetResource();
        var mark = harness.Recorder.Sent.Count;

        ((IDisposable)resource).Dispose();
        await resource.DisposeAsync();

        Assert.Single(SentSince(harness, mark).OfType<ReleaseInstancePacket>());
        Assert.True(service.Resource.Disposed.Wait(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(1, service.Resource.DisposeAsyncCount);
    }

    [Fact(Timeout = 15000)]
    public async Task InstanceImplementingBothContracts_IsDisposedThroughDisposeAsyncOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        var both = new TrackedBothResource();
        service.TakeResource(null);

        await using var harness = await StartAsync(service, ct);

        // Expose the dual-contract object by handing it to the server through the client.
        harness.Proxy.TakeResource(both);
        await harness.Proxy.DisposeTakenResourceAsync(ct);

        Assert.True(both.Disposed.Wait(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(1, both.DisposeAsyncCount);
        Assert.Equal(0, both.DisposeCount);
    }

    [Fact(Timeout = 15000)]
    public async Task ReturnedAsyncDisposable_IsDisposed_WhenProxyIsFinalized()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        GetAndDropResource(harness.Proxy);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.True(service.Resource.Disposed.Wait(TimeSpan.FromSeconds(5), ct),
            "The server-side resource was not disposed after the client proxy was finalized");
        Assert.Equal(1, service.Resource.DisposeAsyncCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void GetAndDropResource(IAsyncResourceService proxy)
    {
        _ = proxy.GetResource();
    }

    [Fact(Timeout = 15000)]
    public async Task NoDispose_ReturnedAsyncDisposable_IsNotDisposedOnRelease()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var resource = harness.Proxy.GetBorrowedResource();
        var mark = harness.Recorder.Sent.Count;

        await resource.DisposeAsync();

        // The reference was released, but the borrowed instance is the caller's to dispose.
        Assert.Single(SentSince(harness, mark).OfType<ReleaseInstancePacket>());
        Assert.Equal(0, service.BorrowedResource.DisposeAsyncCount);
    }

    [Fact(Timeout = 15000)]
    public async Task NullAsyncDisposable_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        Assert.Null(harness.Proxy.GetNullResource());
    }

    [Fact(Timeout = 15000)]
    public async Task SameResourceReturnedTwice_IsDisposedOnlyAfterBothProxiesAreReleased()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var first = harness.Proxy.GetResource();
        var second = harness.Proxy.GetSameResourceAgain();

        Assert.Equal(((InterfaceProxy)first).InstanceId, ((InterfaceProxy)second).InstanceId);

        var mark = harness.Recorder.Sent.Count;
        await first.DisposeAsync();

        // Not the last reference: nothing goes to the wire and nothing is disposed.
        Assert.Empty(SentSince(harness, mark).OfType<ReleaseInstancePacket>());
        Assert.Equal(0, service.Resource.DisposeAsyncCount);

        await second.DisposeAsync();

        Assert.Single(SentSince(harness, mark).OfType<ReleaseInstancePacket>());
        Assert.Equal(1, service.Resource.DisposeAsyncCount);
    }

    // --- C2. Asynchronous disposal of any proxy -----------------------------------------

    // Releasing a reference is the same operation for every remote interface, so any proxy — not just
    // one typed as IAsyncDisposable — can be released asynchronously and awaited.
    [Fact(Timeout = 15000)]
    public async Task AnyProxy_CanBeDisposedAsynchronously()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var notification = harness.Proxy.GetDualRole();

        await ((IAsyncDisposable)notification).DisposeAsync();

        // Confirmed release: the server has already disposed the instance behind the proxy.
        Assert.Equal(1, service.DualRole.DisposeAsyncCount);
    }

    [Fact(Timeout = 15000)]
    public async Task AnyProxy_DisposeAndDisposeAsync_ReleaseExactlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var notification = harness.Proxy.GetDualRole();
        var mark = harness.Recorder.Sent.Count;

        ((IDisposable)notification).Dispose();
        await ((IAsyncDisposable)notification).DisposeAsync();

        Assert.Single(SentSince(harness, mark).OfType<ReleaseInstancePacket>());
        await WaitForAsync(() => service.DualRole.DisposeAsyncCount == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, service.DualRole.DisposeAsyncCount);
    }

    // --- D. DTO field -------------------------------------------------------------------

    [Fact(Timeout = 15000)]
    public async Task AsyncDisposableAsDtoField_IsMarshaledAndReleasable()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var box = harness.Proxy.GetResourceBox();

        Assert.Equal("box", box.Name);
        Assert.NotNull(box.Resource);
        Assert.IsAssignableFrom<InterfaceProxy>(box.Resource);

        await box.Resource!.DisposeAsync();

        Assert.Equal(1, service.BoxResource.DisposeAsyncCount);
    }

    // --- E. Argument direction (client -> server) ---------------------------------------

    [Fact(Timeout = 15000)]
    public async Task AsyncDisposablePassedAsArgument_IsDisposedOnClient_WhenServerDisposesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var clientResource = new TrackedAsyncResource();
        harness.Proxy.TakeResource(clientResource);

        await harness.Proxy.DisposeTakenResourceAsync(ct);

        Assert.True(clientResource.Disposed.Wait(TimeSpan.FromSeconds(5), ct),
            "The client-side instance was not disposed when the server released it");
        Assert.Equal(1, clientResource.DisposeAsyncCount);
    }

    [Fact(Timeout = 15000)]
    public async Task NoDispose_ArgumentAsyncDisposable_IsNotDisposed_WhenServerReleasesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var clientResource = new TrackedAsyncResource();
        harness.Proxy.TakeResource(RpcInterface.NoDispose<IAsyncDisposable>(clientResource));

        await harness.Proxy.DisposeTakenResourceAsync(ct);

        Assert.False(clientResource.Disposed.Wait(TimeSpan.FromMilliseconds(500), ct),
            "A borrowed instance must not be disposed by the RPC layer");
    }

    // --- F. The same object under two interfaces ----------------------------------------

    [Fact(Timeout = 15000)]
    public async Task ObjectExposedAsRemoteInterfaceAndAsAsyncDisposable_SharesOneInstance()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var notification = harness.Proxy.GetDualRole();
        var disposable = harness.Proxy.GetDualRoleAsAsyncDisposable();

        Assert.Equal(((InterfaceProxy)notification).InstanceId, ((InterfaceProxy)disposable).InstanceId);

        notification.OnNotification("hello");
        Assert.Equal(1, service.DualRole.NotificationCount);

        await disposable.DisposeAsync();
        Assert.Equal(0, service.DualRole.DisposeAsyncCount);

        ((IDisposable)notification).Dispose();

        // Both references are gone now, so the single underlying instance is disposed once.
        await WaitForAsync(() => service.DualRole.DisposeAsyncCount == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(1, service.DualRole.DisposeAsyncCount);
    }

    [Fact(Timeout = 15000)]
    public async Task ObjectFirstExposedAsAsyncDisposable_ThenAsRemoteInterface_FailsClearly()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        // The adapter is cached per object, so the first marshaling decides which interface it serves.
        _ = harness.Proxy.GetDualRoleAsAsyncDisposable();
        var notification = harness.Proxy.GetDualRole();

        var ex = Assert.Throws<RpcServerException>(() => notification.OnNotification("hello"));
        Assert.Contains("IAsyncDisposable", ex.Message);
    }

    // --- G. Lifecycle edges -------------------------------------------------------------

    [Fact(Timeout = 15000)]
    public async Task ServerDisposesUnreleasedAsyncDisposable_WhenConnectionIsClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        _ = harness.Proxy.GetResource();
        Assert.False(service.Resource.Disposed.IsSet);

        await harness.Client.CloseAsync(ct);

        Assert.True(service.Resource.Disposed.Wait(TimeSpan.FromSeconds(5), ct),
            "The server did not dispose the instance when the connection was closed");
        Assert.Equal(1, service.Resource.DisposeAsyncCount);
    }

    [Fact(Timeout = 15000)]
    public async Task ServerDisposesUnreleasedAsyncDisposable_WhenConnectionIsBroken()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        var harness = await StartAsync(service, ct);

        _ = harness.Proxy.GetResource();
        Assert.False(service.Resource.Disposed.IsSet);

        // Drop the transport under the client without a graceful close.
        harness.Recorder.Dispose();

        Assert.True(service.Resource.Disposed.Wait(TimeSpan.FromSeconds(10), ct),
            "The server did not dispose the instance when the connection was broken");
        Assert.Equal(1, service.Resource.DisposeAsyncCount);

        await harness.DisposeAsync();
    }

    [Fact(Timeout = 15000)]
    public async Task DisposeAsync_AfterChannelClosed_ReturnsWithoutThrowing()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        await using var harness = await StartAsync(service, ct);

        var resource = harness.Proxy.GetResource();
        await harness.Client.CloseAsync(ct);

        var disposeTask = resource.DisposeAsync().AsTask();
        Assert.True(await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(2), ct)) == disposeTask,
            "DisposeAsync did not return promptly on a closed channel");
        await disposeTask;
    }

    [Fact(Timeout = 15000)]
    public async Task DisposeAsync_AfterClientDisposed_IsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        var harness = await StartAsync(service, ct);

        var resource = harness.Proxy.GetResource();
        await harness.Client.DisposeAsync();

        await resource.DisposeAsync();

        harness.TcpHost.Close();
    }

    [Fact(Timeout = 15000)]
    public async Task DisposeAsync_PropagatesRemoteDisposalFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new AsyncResourceService();
        service.Resource.OnDisposeAsync = () => throw new InvalidOperationException("boom");

        await using var harness = await StartAsync(service, ct);

        var resource = harness.Proxy.GetResource();

        var ex = await Assert.ThrowsAsync<RpcServerException>(async () => await resource.DisposeAsync());
        Assert.Contains("boom", ex.Message);
    }

    // --- H. Plumbing unit tests ---------------------------------------------------------

    [Fact]
    public async Task RefCounter_ReleaseAsync_InvokesAsyncCallbackOnLastReleaseOnly()
    {
        var value = new object();
        var syncCalls = 0;
        var asyncCalls = 0;

        var counter = new RefCounter<object>(value, _ => syncCalls++, _ => { asyncCalls++; return ValueTask.CompletedTask; });
        counter.AddRef();
        counter.AddRef();

        Assert.Equal(1, await counter.ReleaseAsync());
        Assert.Equal(0, asyncCalls);

        Assert.Equal(0, await counter.ReleaseAsync());
        Assert.Equal(1, asyncCalls);
        Assert.Equal(0, syncCalls);
    }

    [Fact]
    public async Task RefCounter_ReleaseAsync_FallsBackToSyncCallback()
    {
        var syncCalls = 0;
        var counter = new RefCounter<object>(new object(), _ => syncCalls++);
        counter.AddRef();

        await counter.ReleaseAsync();

        Assert.Equal(1, syncCalls);
    }

    [Fact]
    public async Task InterfaceAdapter_DisposeAsync_PrefersAsyncDisposal_AndRunsExactlyOnce()
    {
        var instance = new TrackedBothResource();
        var adapter = InterfaceAdapter.CreateAdapter(typeof(IAsyncDisposable), instance, RpcPacket.DefaultSerializationContext);

        await ((IAsyncDisposable)adapter).DisposeAsync();
        ((IDisposable)adapter).Dispose();
        await ((IAsyncDisposable)adapter).DisposeAsync();

        Assert.Equal(1, instance.DisposeAsyncCount);
        Assert.Equal(0, instance.DisposeCount);
    }

    // The rule is unconditional: an instance that offers asynchronous disposal is always released
    // through it, including on the synchronous teardown paths, and never through both contracts.
    [Fact]
    public void InterfaceAdapter_Dispose_PrefersAsyncDisposal_ForDualContractInstance()
    {
        var instance = new TrackedBothResource();
        var adapter = InterfaceAdapter.CreateAdapter(typeof(IAsyncDisposable), instance, RpcPacket.DefaultSerializationContext);

        ((IDisposable)adapter).Dispose();

        Assert.Equal(1, instance.DisposeAsyncCount);
        Assert.Equal(0, instance.DisposeCount);
    }

    [Fact]
    public void InterfaceAdapter_Dispose_DisposesAsyncOnlyInstance()
    {
        var instance = new TrackedAsyncResource();
        var adapter = InterfaceAdapter.CreateAdapter(typeof(IAsyncDisposable), instance, RpcPacket.DefaultSerializationContext);

        ((IDisposable)adapter).Dispose();

        Assert.Equal(1, instance.DisposeAsyncCount);
    }

    [Fact]
    public async Task InterfaceAdapter_DoesNotDisposeBorrowedInstance()
    {
        var sync = new TrackedAsyncResource();
        var async = new TrackedAsyncResource();

        var syncAdapter = InterfaceAdapter.CreateAdapter(typeof(IAsyncDisposable), sync, RpcPacket.DefaultSerializationContext, ownsInstance: false);
        var asyncAdapter = InterfaceAdapter.CreateAdapter(typeof(IAsyncDisposable), async, RpcPacket.DefaultSerializationContext, ownsInstance: false);

        ((IDisposable)syncAdapter).Dispose();
        await ((IAsyncDisposable)asyncAdapter).DisposeAsync();

        Assert.Equal(0, sync.DisposeAsyncCount);
        Assert.Equal(0, async.DisposeAsyncCount);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }
    }
}
