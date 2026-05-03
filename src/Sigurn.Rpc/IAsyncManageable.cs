namespace Sigurn.Rpc;

/// <summary>
/// Represents a component that can be started and stopped asynchronously.
/// </summary>
public interface IAsyncManageable
{
    /// <summary>
    /// Returns a value indicating whether the component is currently started.
    /// </summary>
    /// <returns><see langword="true"/> if the component is started; otherwise, <see langword="false"/>.</returns>
    bool IsStarted();

    /// <summary>
    /// Starts the component asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the component asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken);
}
