namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Server-side adapter for a local <see cref="IAsyncDisposable"/> exposed to the remote peer.
/// </summary>
/// <remarks>
/// Unlike every other remote interface, <see cref="IAsyncDisposable"/> has no dispatch surface: its only
/// operation — <see cref="IAsyncDisposable.DisposeAsync"/> — <em>is</em> the release of the reference.
/// The remote peer therefore never sends a method call for it: releasing the last proxy reference sends a
/// <see cref="Packets.ReleaseInstancePacket"/>, and the adapter's own disposal disposes the wrapped
/// instance exactly once (subject to <see cref="RpcInterface.NoDispose{T}(T)"/>). Any dispatch packet
/// reaching this adapter is a protocol error and is reported as such.
/// </remarks>
sealed class AsyncDisposableAdapter : InterfaceAdapter
{
    public AsyncDisposableAdapter(IAsyncDisposable instance)
        : base(typeof(IAsyncDisposable), instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
    }

    private static NotSupportedException NotDispatchable(string kind)
    {
        return new NotSupportedException(
            $"IAsyncDisposable exposes no remote {kind}; the instance is released through the " +
            "instance-release path (ReleaseInstancePacket), not through a method call.");
    }

    public override Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        return Task.FromException<(byte[]? Result, IReadOnlyList<byte[]>? Args)>(NotDispatchable("methods"));
    }

    public override Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken)
    {
        return Task.FromException<byte[]?>(NotDispatchable("properties"));
    }

    public override Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken)
    {
        return Task.FromException(NotDispatchable("properties"));
    }

    public override Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        return Task.FromException(NotDispatchable("events"));
    }

    public override Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        return Task.FromException(NotDispatchable("events"));
    }
}
