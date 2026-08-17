namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// Ambient identity of the instance the current request is dispatched to.
/// </summary>
/// <remarks>
/// An adapter carries no instance id of its own: a shared (Host/Process) adapter is registered in
/// several sessions under a different id in each. The id lives in the request, so the session
/// publishes it around the dispatch and tracing picks it up from here.
/// </remarks>
static class RpcDispatchContext
{
    private static readonly AsyncLocal<Guid?> _instanceId = new();

    /// <summary>Instance id of the request being dispatched, or <c>null</c> outside a dispatch.</summary>
    public static Guid? InstanceId => _instanceId.Value;

    public static IDisposable Scope(Guid instanceId)
    {
        var previous = _instanceId.Value;
        _instanceId.Value = instanceId;
        return Disposable.Create(() => _instanceId.Value = previous);
    }
}
