namespace Sigurn.Rpc;

[RemoteInterface]
public interface IServiceCatalog
{
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken cancellationToken);
}