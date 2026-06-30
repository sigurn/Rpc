namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Server-side (owner-side) implementation of <see cref="IRemoteStream"/> that wraps a local
/// <see cref="Stream"/> and exposes it for remote access. The wrapped stream is owned by this
/// instance and is disposed when the RPC instance is released (the generated adapter disposes the
/// wrapped instance when its reference count drops to zero).
/// </summary>
public sealed class RemoteStreamSource : IRemoteStream, IDisposable, IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly int _chunkSize;

    /// <summary>
    /// Creates a new <see cref="RemoteStreamSource"/> wrapping the specified stream.
    /// </summary>
    /// <param name="stream">The local stream to expose remotely. Ownership is transferred to this instance.</param>
    /// <param name="chunkSize">The maximum number of bytes returned by a single read. Defaults to <see cref="RemoteStream.MaxChunkSize"/>.</param>
    public RemoteStreamSource(Stream stream, int chunkSize = RemoteStream.MaxChunkSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");

        _stream = stream;
        _chunkSize = chunkSize;
    }

    /// <inheritdoc/>
    public Task<RemoteStreamInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(RemoteStreamInfo.FromStream(_stream));
    }

    /// <inheritdoc/>
    public async Task<byte[]> ReadAsync(int count, CancellationToken cancellationToken)
    {
        if (count <= 0) return [];

        var toRead = Math.Min(count, _chunkSize);
        var buffer = new byte[toRead];
        var read = await _stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);

        if (read <= 0) return [];
        if (read == buffer.Length) return buffer;

        var result = new byte[read];
        Array.Copy(buffer, result, read);
        return result;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        await _stream.WriteAsync(data.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<long> SeekAsync(long offset, SeekOrigin origin, CancellationToken cancellationToken)
    {
        return Task.FromResult(_stream.Seek(offset, origin));
    }

    /// <inheritdoc/>
    public Task SetLengthAsync(long value, CancellationToken cancellationToken)
    {
        _stream.SetLength(value);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FlushAsync(CancellationToken cancellationToken)
    {
        return _stream.FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<long> GetPositionAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_stream.Position);
    }

    /// <inheritdoc/>
    public Task SetPositionAsync(long value, CancellationToken cancellationToken)
    {
        _stream.Position = value;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<long> GetLengthAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_stream.Length);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stream.Dispose();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        return _stream.DisposeAsync();
    }
}
