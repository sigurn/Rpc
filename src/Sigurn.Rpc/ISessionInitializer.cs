namespace Sigurn.Rpc;

/// <summary>
/// Provides a session-initialize hook with access to the freshly connected transport and to full RPC,
/// after the channel (re)connects but before session state is restored and before the connection is
/// announced as ready.
/// </summary>
/// <remarks>
/// A typical hook walks <see cref="IChainedChannel.BaseChannel"/> from <see cref="Channel"/> to find an
/// <c>AesChannel</c> and call <c>SetKey</c>, then performs an authentication call via
/// <see cref="GetService{T}(CancellationToken)"/>. Any service proxy obtained through
/// <see cref="GetService{T}(CancellationToken)"/> is transient: it is released automatically when the hook
/// returns (whether it succeeds or throws) and is never re-requested by session restore.
/// </remarks>
public interface ISessionInitializer
{
    /// <summary>
    /// Gets the freshly connected channel chain. Walk <see cref="IChainedChannel.BaseChannel"/> to reach
    /// a specific decorator (e.g. an <c>AesChannel</c>) before making RPC calls.
    /// </summary>
    IChannel Channel { get; }

    /// <summary>
    /// Obtains a transient proxy for the remote service of the specified interface type, usable for the
    /// duration of the hook. The proxy is released automatically when the hook completes and is never
    /// re-requested by session restore.
    /// </summary>
    /// <typeparam name="T">The interface type of the remote service.</typeparam>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A proxy that implements <typeparamref name="T"/> and forwards calls to the remote service.</returns>
    Task<T> GetService<T>(CancellationToken cancellationToken) where T : class;
}
