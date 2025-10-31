

namespace Sigurn.Rpc.Infrastructure;

sealed class ServiceCatalogAdapter : InterfaceAdapter
{
    private readonly IServiceCatalog _catalog;
    public ServiceCatalogAdapter(IServiceCatalog instance)
        : base(typeof(IServiceCatalog), instance)
    {
        _catalog = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    public override async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        if (methodId == 0)
        {
            if (args is null || args.Count != 0)
                throw new InvalidOperationException("Invalid number of arguments");
            if (oneWay) return (null, null);

            var services = await _catalog.GetServicesAsync(cancellationToken);

            return (await ToBytesAsync(services, cancellationToken), null);
        }
        
        return await base.InvokeMethodAsync(methodId, args, oneWay, cancellationToken);
    }
}