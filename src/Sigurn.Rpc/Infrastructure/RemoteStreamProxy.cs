namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Client-side proxy that forwards <see cref="IRemoteStream"/> calls to the remote implementation.
/// Hand-written (the library does not run the RPC source generator on its own code) and kept in sync
/// with <see cref="RemoteStreamAdapter"/> by method id.
/// </summary>
sealed class RemoteStreamProxy : InterfaceProxy, IRemoteStream
{
    public RemoteStreamProxy(Guid instanceId)
        : base(instanceId)
    {
    }

    public async Task<RemoteStreamInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        var (res, _) = await InvokeMethodAsync(0, [], false, cancellationToken).ConfigureAwait(false);
        return await FromBytesAsync<RemoteStreamInfo>(res, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned null instead of result.");
    }

    public async Task<byte[]> ReadAsync(int count, CancellationToken cancellationToken)
    {
        using var _noTimeout = new RpcContext { Timeout = Timeout.InfiniteTimeSpan };
        var (res, _) = await InvokeMethodAsync(1, [ToBytes(count)], false, cancellationToken).ConfigureAwait(false);
        return await FromBytesAsync<byte[]>(res, cancellationToken).ConfigureAwait(false) ?? [];
    }

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var _noTimeout = new RpcContext { Timeout = Timeout.InfiniteTimeSpan };
        await InvokeMethodAsync(2, [ToBytes(data)], false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> SeekAsync(long offset, SeekOrigin origin, CancellationToken cancellationToken)
    {
        var (res, _) = await InvokeMethodAsync(3, [ToBytes(offset), ToBytes(origin)], false, cancellationToken).ConfigureAwait(false);
        return await FromBytesAsync<long>(res, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetLengthAsync(long value, CancellationToken cancellationToken)
    {
        await InvokeMethodAsync(4, [ToBytes(value)], false, cancellationToken).ConfigureAwait(false);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await InvokeMethodAsync(5, [], false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetPositionAsync(CancellationToken cancellationToken)
    {
        var (res, _) = await InvokeMethodAsync(6, [], false, cancellationToken).ConfigureAwait(false);
        return await FromBytesAsync<long>(res, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPositionAsync(long value, CancellationToken cancellationToken)
    {
        await InvokeMethodAsync(7, [ToBytes(value)], false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetLengthAsync(CancellationToken cancellationToken)
    {
        var (res, _) = await InvokeMethodAsync(8, [], false, cancellationToken).ConfigureAwait(false);
        return await FromBytesAsync<long>(res, cancellationToken).ConfigureAwait(false);
    }
}
