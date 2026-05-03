namespace Sigurn.Rpc;

/// <summary>
/// Represents a service that is notified when sessions are attached or detached.
/// </summary>
public interface ISessionsAware
{
    /// <summary>
    /// Called when a session is attached to the service.
    /// </summary>
    /// <param name="session">The session being attached.</param>
    void AttachSession(ISession session);

    /// <summary>
    /// Called when a session is detached from the service.
    /// </summary>
    /// <param name="session">The session being detached.</param>
    void DetachSession(ISession session);
}