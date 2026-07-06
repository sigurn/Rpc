namespace Sigurn.Rpc;

/// <summary>
/// Thrown when a remote interface instance can no longer be used because the underlying channel
/// reconnected and the instance could not be restored on the new session. Derived (non-root) proxies
/// obtained from method results become invalid after a reopen — re-request them from the service.
/// </summary>
public sealed class RpcInvalidInstanceException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of <see cref="RpcInvalidInstanceException"/>.
    /// </summary>
    public RpcInvalidInstanceException()
        : base("The remote instance is no longer valid after the channel reconnected.")
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="RpcInvalidInstanceException"/> with a custom message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public RpcInvalidInstanceException(string message)
        : base(message)
    {
    }
}
