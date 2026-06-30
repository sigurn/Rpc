namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Remote-by-reference protocol used to transfer a <see cref="System.IO.Stream"/> over an RPC
/// connection. This interface is the wire contract behind the transparent <see cref="System.IO.Stream"/>
/// marshaling performed by <see cref="StreamSerializer"/>; user code normally works with a plain
/// <see cref="System.IO.Stream"/> and never references this type directly.
/// </summary>
/// <remarks>
/// Data is transferred in chunks (see <see cref="RemoteStream.MaxChunkSize"/>) because the channel
/// protocol limits a single packet's size. Each operation is a separate RPC round-trip, so a single
/// large transfer occupies the channel and competes with other traffic on the same session.
/// </remarks>
[RemoteInterface]
public interface IRemoteStream
{
    /// <summary>
    /// Gets the capabilities of the remote stream. Fetched once when the wrapper is created.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The remote stream capabilities.</returns>
    Task<RemoteStreamInfo> GetInfoAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads up to <paramref name="count"/> bytes from the remote stream.
    /// </summary>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bytes read; an empty array indicates the end of the stream.</returns>
    [NoRpcTimeout]
    Task<byte[]> ReadAsync(int count, CancellationToken cancellationToken);

    /// <summary>
    /// Writes <paramref name="data"/> to the remote stream.
    /// </summary>
    /// <param name="data">The bytes to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [NoRpcTimeout]
    Task WriteAsync(byte[] data, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the position within the remote stream.
    /// </summary>
    /// <param name="offset">The byte offset relative to <paramref name="origin"/>.</param>
    /// <param name="origin">The reference point used to obtain the new position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new position within the stream.</returns>
    Task<long> SeekAsync(long offset, SeekOrigin origin, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the length of the remote stream.
    /// </summary>
    /// <param name="value">The desired length.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetLengthAsync(long value, CancellationToken cancellationToken);

    /// <summary>
    /// Flushes any buffered data to the underlying remote stream.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FlushAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current position within the remote stream.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current position.</returns>
    Task<long> GetPositionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the current position within the remote stream.
    /// </summary>
    /// <param name="value">The desired position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SetPositionAsync(long value, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current length of the remote stream.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current length.</returns>
    Task<long> GetLengthAsync(CancellationToken cancellationToken);
}
