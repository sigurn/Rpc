namespace Sigurn.Rpc.Infrastructure;

sealed class ServiceCatalogProxy : InterfaceProxy, IServiceCatalog
{
    public ServiceCatalogProxy(Guid instanceId) 
        : base(instanceId)
    {
    }

    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken cancellationToken)
    {
        (var res, var _) = await InvokeMethodAsync(0, [], false, cancellationToken);
        if (res is null) throw new InvalidOperationException("Server returned null instead of result.");

        return await FromBytesAsync<IReadOnlyList<ServiceInfo>>(res, cancellationToken) ?? [];
    }
}