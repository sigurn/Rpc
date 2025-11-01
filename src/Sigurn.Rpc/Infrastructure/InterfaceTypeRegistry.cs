namespace Sigurn.Rpc.Infrastructure;

static class InterfaceTypeRegistry
{
    private static readonly Dictionary<Guid, Type> _types = new();
    public static void RegisterType<T>()
    {
        RegisterType(typeof(T));
    }
    
    public static void RegisterType(Type type)
    {
        lock(_types)
        {
            if (_types.ContainsKey(type.GUID)) return;
            _types.Add(type.GUID, type);
        }
    }

    public static Type? GetTypeById(Guid id)
    {
        lock(_types)
        {
            if (_types.TryGetValue(id, out Type? type))
                return type;
            return null;
        }
    }
}