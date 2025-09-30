using System.Runtime.CompilerServices;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

class EventArgsSerializer : ITypeSerializer<EventArgs>
{
    public Task<EventArgs> FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(EventArgs.Empty);
    }

    public Task ToStreamAsync(Stream stream, EventArgs value, SerializationContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}