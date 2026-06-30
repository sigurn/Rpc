namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Consumer-side <see cref="Stream"/> that forwards its operations to a remote stream over RPC.
/// This is the object handed to user code when a service method returns or receives a
/// <see cref="Stream"/>; <see cref="StreamSerializer"/> creates it transparently.
/// </summary>
/// <remarks>
/// <para>
/// Every operation is a separate RPC round-trip and reads/writes are split into chunks no larger
/// than <see cref="MaxChunkSize"/> (a single channel packet is limited in size). A large transfer
/// therefore occupies the underlying channel and competes with other RPC traffic on the same
/// session; there is no read-ahead.
/// </para>
/// <para>
/// Like <see cref="Stream"/> in general, instances are not safe for concurrent use: do not issue
/// overlapping read/write operations on the same instance.
/// </para>
/// <para>
/// Disposing this stream releases the remote instance, which closes the underlying source stream on
/// the owner side. Always dispose it (directly or via <c>using</c>) to release the remote resource
/// deterministically.
/// </para>
/// </remarks>
public sealed class RemoteStream : Stream
{
    /// <summary>
    /// The maximum number of bytes transferred in a single read or write operation. Chosen well
    /// below the channel packet size limit to leave room for framing and serialization overhead.
    /// </summary>
    public const int MaxChunkSize = 64 * 1024;

    private readonly IRemoteStream _proxy;
    private readonly int _chunkSize;
    private RemoteStreamInfo? _info;

    /// <summary>
    /// Creates a new <see cref="RemoteStream"/> over the specified remote stream proxy.
    /// </summary>
    /// <param name="proxy">The remote stream proxy to forward operations to.</param>
    /// <param name="chunkSize">The maximum number of bytes per read/write operation. Defaults to <see cref="MaxChunkSize"/>.</param>
    public RemoteStream(IRemoteStream proxy, int chunkSize = MaxChunkSize)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");

        _proxy = proxy;
        _chunkSize = chunkSize;
    }

    /// <summary>
    /// Creates a <see cref="RemoteStream"/> with the remote capabilities already known, so the
    /// synchronous capability properties do not require a round-trip. Used by <see cref="StreamSerializer"/>.
    /// </summary>
    internal RemoteStream(IRemoteStream proxy, int chunkSize, RemoteStreamInfo info)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(info);
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");

        _proxy = proxy;
        _chunkSize = chunkSize;
        _info = info;
    }

    private RemoteStreamInfo Info => _info ??= _proxy.GetInfoAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override bool CanRead => Info.CanRead;

    /// <inheritdoc/>
    public override bool CanWrite => Info.CanWrite;

    /// <inheritdoc/>
    public override bool CanSeek => Info.CanSeek;

    /// <inheritdoc/>
    public override long Length => _proxy.GetLengthAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override long Position
    {
        get => _proxy.GetPositionAsync(CancellationToken.None).GetAwaiter().GetResult();
        set => _proxy.SetPositionAsync(value, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;

        var toRead = Math.Min(buffer.Length, _chunkSize);
        var data = await _proxy.ReadAsync(toRead, cancellationToken).ConfigureAwait(false);
        if (data.Length == 0) return 0;

        data.AsSpan().CopyTo(buffer.Span);
        return data.Length;
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var length = Math.Min(_chunkSize, buffer.Length - offset);
            var chunk = buffer.Slice(offset, length).ToArray();
            await _proxy.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            offset += length;
        }
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        return _proxy.SeekAsync(offset, origin, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        _proxy.SetLengthAsync(value, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        _proxy.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _proxy.FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            (_proxy as IDisposable)?.Dispose();

        base.Dispose(disposing);
    }
}
