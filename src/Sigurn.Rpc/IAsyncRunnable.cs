namespace Sigurn.Rpc;

/// <summary>
/// Represents a component that can be started and run asynchronously until cancelled.
/// </summary>
public interface IAsyncRunnable
{
    /// <summary>
    /// Gets a value indicating whether the component is currently running.
    /// </summary>
    public bool IsRunning { get; }

    /// <summary>
    /// Starts and runs the component asynchronously until the cancellation token is cancelled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token used to stop the component.</param>
    Task RunAsync(CancellationToken cancellationToken);
}
