using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Describes the capabilities of a remote <see cref="System.IO.Stream"/> exposed through
/// <see cref="IRemoteStream"/>. The information is fetched once when a <see cref="RemoteStream"/>
/// wrapper is created so that capability checks do not require a round-trip.
/// </summary>
public sealed class RemoteStreamInfo : ISerializable
{
    /// <summary>
    /// Sentinel value for <see cref="Length"/> meaning the length is not available
    /// (for example for a non-seekable stream).
    /// </summary>
    public const long UnknownLength = -1;

    /// <summary>
    /// Gets a value indicating whether the remote stream supports reading.
    /// </summary>
    public bool CanRead { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the remote stream supports writing.
    /// </summary>
    public bool CanWrite { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the remote stream supports seeking.
    /// </summary>
    public bool CanSeek { get; private set; }

    /// <summary>
    /// Gets the length of the remote stream, or <see cref="UnknownLength"/> when it is not available.
    /// </summary>
    public long Length { get; private set; } = UnknownLength;

    /// <summary>
    /// Creates a new <see cref="RemoteStreamInfo"/> describing the specified stream.
    /// </summary>
    /// <param name="stream">The stream to describe.</param>
    /// <returns>A new <see cref="RemoteStreamInfo"/> instance.</returns>
    public static RemoteStreamInfo FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return new RemoteStreamInfo
        {
            CanRead = stream.CanRead,
            CanWrite = stream.CanWrite,
            CanSeek = stream.CanSeek,
            Length = stream.CanSeek ? stream.Length : UnknownLength,
        };
    }

    /// <summary>
    /// Deserializes this instance from the specified stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="context">The serialization context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        CanRead = await Serializer.FromStreamAsync<bool>(stream, context, cancellationToken).ConfigureAwait(false);
        CanWrite = await Serializer.FromStreamAsync<bool>(stream, context, cancellationToken).ConfigureAwait(false);
        CanSeek = await Serializer.FromStreamAsync<bool>(stream, context, cancellationToken).ConfigureAwait(false);
        Length = await Serializer.FromStreamAsync<long>(stream, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes this instance to the specified stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="context">The serialization context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, CanRead, context, cancellationToken).ConfigureAwait(false);
        await Serializer.ToStreamAsync(stream, CanWrite, context, cancellationToken).ConfigureAwait(false);
        await Serializer.ToStreamAsync(stream, CanSeek, context, cancellationToken).ConfigureAwait(false);
        await Serializer.ToStreamAsync(stream, Length, context, cancellationToken).ConfigureAwait(false);
    }
}
