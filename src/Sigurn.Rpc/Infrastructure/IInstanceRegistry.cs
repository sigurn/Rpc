namespace Sigurn.Rpc.Infrastructure;

interface IInstanceRegistry
{
    Guid RegisterInstance<T>(object instance);
    object GetInstance<T>(Guid instanceId);
}