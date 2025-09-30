using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

interface ISerializationContext
{
    SerializationContext Context { get; }
}