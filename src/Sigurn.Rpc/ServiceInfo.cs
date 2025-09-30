using System.Reflection;

namespace Sigurn.Rpc;

public sealed class ServiceInfo<T>
{
    private readonly Func<ISession, T> _factory;
    public ServiceInfo(Func<ISession, T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        ServiceType = typeof(T);
        _factory = factory;

        var attr = ServiceType.GetCustomAttribute<RemoteServiceAttribute>(false);
        if (attr is null)
            throw new ArgumentException($"The class '{typeof(T)}' does not have attribute RemoteServiceAttribute, so class cannot be used as remote service");
    }

    public Type ServiceType { get; }

    public Guid ServiceId => ServiceType.GUID;

    public IReadOnlyList<Type> SupportedInterfaces { get; } = new List<Type>();

    public IReadOnlyList<Type> AvailableIntefraces { get; } = new List<Type>();


}