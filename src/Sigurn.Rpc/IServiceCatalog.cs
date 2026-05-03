namespace Sigurn.Rpc;

/// <summary>
/// Provides discovery of services available on the remote host.
/// </summary>
[RemoteInterface]
public interface IServiceCatalog
{
    /// <summary>
    /// Returns the list of services registered on the remote host asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of available services.</returns>
    Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(CancellationToken cancellationToken);
}