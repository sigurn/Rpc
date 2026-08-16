namespace Sigurn.Rpc.Infrastructure;

sealed class ServiceCatalogProxy : InterfaceProxy, IServiceCatalog
{
    private const string @__method_0 = "GetServicesAsync(System.Threading.CancellationToken)";

    public ServiceCatalogProxy(Guid instanceId)
        : base(instanceId)
    {
    }

    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_0, 0);
        try
        {
            (var res, var _) = await InvokeMethodAsync(0, [], false, cancellationToken).ConfigureAwait(false);
            if (res is null) throw new InvalidOperationException("Server returned null instead of result.");

            return await FromBytesAsync<IReadOnlyList<ServiceInfo>>(res, cancellationToken).ConfigureAwait(false) ?? [];
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
}
