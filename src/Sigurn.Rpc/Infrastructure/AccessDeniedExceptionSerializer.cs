using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

class AccessDeniedExceptionSerializer : ITypeSerializer<AccessDeniedException>
{
    public async Task<AccessDeniedException> FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        var message = await Serializer.FromStreamAsync<string>(stream, context, cancellationToken);
        return message is null ? new AccessDeniedException() : new AccessDeniedException(message);
    }

    public async Task ToStreamAsync(Stream stream, AccessDeniedException value, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, value.Message, context, cancellationToken);
    }
}