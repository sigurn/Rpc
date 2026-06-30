using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Serializer that marshals <see cref="Stream"/> instances by reference over an RPC connection.
/// On serialization the local stream is wrapped in a <see cref="RemoteStreamSource"/>, registered
/// with the session and represented on the wire by its instance id; on deserialization a
/// <see cref="RemoteStream"/> proxy is created. This makes a plain <see cref="Stream"/> usable
/// directly as an RPC method parameter or return value.
/// </summary>
/// <remarks>
/// The serializer is bound to the exact <see cref="Stream"/> type. A method signature must use
/// <see cref="Stream"/> itself (not a concrete subclass such as <c>FileStream</c>), because the
/// deserialized value is always a <see cref="RemoteStream"/>.
/// </remarks>
public sealed class StreamSerializer : ITypeSerializer<Stream>
{
    /// <inheritdoc/>
    public async Task ToStreamAsync(Stream stream, Stream value, SerializationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);

        var session = GetSession(context);
        var source = new RemoteStreamSource(value);
        var instanceId = session.RegisterInstance(typeof(IRemoteStream), source);

        await Serializer.ToStreamAsync(stream, instanceId, context, cancellationToken).ConfigureAwait(false);
        await RemoteStreamInfo.FromStream(value).ToStreamAsync(stream, context, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Stream> FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        var session = GetSession(context);

        var instanceId = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken).ConfigureAwait(false);
        var info = new RemoteStreamInfo();
        await info.FromStreamAsync(stream, context, cancellationToken).ConfigureAwait(false);

        var proxy = (IRemoteStream)session.GetProxy(typeof(IRemoteStream), instanceId);
        return new RemoteStream(proxy, RemoteStream.MaxChunkSize, info);
    }

    private static Session GetSession(SerializationContext context)
    {
        Session? session = null;
        if (context is RpcSerializationContext rsc)
            session = rsc.Session;

        return session ?? throw new InvalidOperationException("Session is not available. Cannot serialize/deserialize streams");
    }
}
