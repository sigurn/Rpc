namespace Sigurn.Rpc;

/// <summary>
/// Defines the scope within which a service instance is shared.
/// </summary>
public enum ShareWithin
{
    /// <summary>
    /// A new instance is created for every call.
    /// </summary>
    None,

    /// <summary>
    /// The instance is shared within a single session.
    /// </summary>
    Session,

    /// <summary>
    /// The instance is shared across all sessions on the same host.
    /// </summary>
    Host,

    /// <summary>
    /// The instance is shared across all sessions in the process.
    /// </summary>
    Process,
}
