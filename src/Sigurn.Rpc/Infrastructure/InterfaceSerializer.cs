using System.Reflection;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

class InterfaceSerializer : IGeneralSerializer
{
    public bool IsTypeSupported(Type type)
    {
        if (!type.IsInterface) return false;
        var attr = type.GetCustomAttribute<RemoteInterfaceAttribute>();
        if (attr is null) return false;

        return InterfaceAdapter.IsThereAdapterFor(type) && InterfaceProxy.IsThereProxyFor(type);
    }

    public async Task<object> FromStreamAsync(Stream stream, Type type, SerializationContext context, CancellationToken cancellationToken)
    {
        if (!type.IsInterface)
            throw new ArgumentException($"Interface serializer cannot deserialize type {type}");

        Guid instanceId = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken);
        return GetSession(context).GetProxy(type, instanceId);
    }

    public async Task ToStreamAsync(Stream stream, Type type, object value, SerializationContext context, CancellationToken cancellationToken)
    {
        if (!type.IsInterface)
            throw new ArgumentException($"Interface serializer cannot serialize type {type}");

        Guid instanceId = GetSession(context).RegisterInstance(type, value);
        await Serializer.ToStreamAsync(stream, instanceId, context, cancellationToken);
    }

    private static Session GetSession(SerializationContext context)
    {
        Session? session = null;
        if (context is RpcSerializationContext rsc)
            session = rsc.Session;

        return session ?? throw new InvalidOperationException("Session is not available. Cannot serialize/deserialize interfaces");
    }
}