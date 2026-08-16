namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Client-side proxy for a remote <see cref="IAsyncDisposable"/>.
/// </summary>
/// <remarks>
/// <para>
/// No method call is ever produced for <see cref="DisposeAsync"/>: disposing this proxy releases its
/// reference to the shared remote instance, and the last release sends a
/// <see cref="Packets.ReleaseInstancePacket"/> which makes the remote side dispose the wrapped object.
/// This is what keeps the object from being disposed twice — there is no separate remote
/// <c>DisposeAsync</c> call in addition to the release.
/// </para>
/// <para>
/// Reference semantics: when the same remote object is marshaled more than once, the peer gets one proxy
/// per marshaling over a single shared reference count. Only the last release reaches the wire, so
/// <c>await DisposeAsync()</c> on a non-final proxy returns immediately without disposing anything.
/// </para>
/// </remarks>
sealed class AsyncDisposableProxy : InterfaceProxy, IAsyncDisposable
{
    public AsyncDisposableProxy(Guid instanceId)
        : base(instanceId)
    {
    }

    public ValueTask DisposeAsync()
    {
        return ReleaseAsync();
    }
}
