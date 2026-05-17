namespace Sigurn.Rpc;

/// <summary>
/// Provides asynchronous acceptance of incoming channel connections.
/// </summary>
public interface IAsyncChannelAcceptor : IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the acceptor is currently accepting connections.
    /// </summary>
    bool IsAccepting { get; }

    /// <summary>
    /// Waits for and accepts the next incoming channel connection asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The accepted channel or null if there is nothing to accept and work is finished.</returns>
    public Task<IChannel?> AcceptAsync(CancellationToken cancellationToken);
}