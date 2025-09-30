using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

interface IServiceHost
{
    Type? FindTypeById(Guid id);
    (ShareWithin Shared, Func<object> Factory) GetServiceInfo(Type type);
    RefCounter<ICallTarget> CreateGlobalInstance(Type type, Func<ICallTarget> factory);
    RefCounter<ICallTarget> CreateHostInstance(Type type, Func<ICallTarget> factory);
}