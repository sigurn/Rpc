namespace Sigurn.Rpc;

[RemoteInterface]
interface IServiceCatalog
{
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken cancellationToken);
}