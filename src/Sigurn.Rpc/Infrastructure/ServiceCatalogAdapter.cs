
namespace Sigurn.Rpc.Infrastructure;

sealed class ServiceCatalogAdapter : InterfaceAdapter
{
    private const string @__method_0 = "GetServicesAsync(System.Threading.CancellationToken)";

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
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_0, 0);
            try
            {
                if (args is null || args.Count != 0)
                    throw new InvalidOperationException("Invalid number of arguments");
                if (oneWay) return (null, null);

                var services = await _catalog.GetServicesAsync(cancellationToken).ConfigureAwait(false);

                return (await ToBytesAsync(services, cancellationToken).ConfigureAwait(false), null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_0, 0))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_0, 0);
            }
        }

        return await base.InvokeMethodAsync(methodId, args, oneWay, cancellationToken).ConfigureAwait(false);
    }
}
