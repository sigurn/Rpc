namespace Sigurn.Rpc;

/// <summary>
/// Defines how service instances are created.
/// </summary>
public enum InstanceType
{
    /// <summary>
    /// A single instance is used for all calls.
    /// </summary>
    Single,

    /// <summary>
    /// A new instance is created per session.
    /// </summary>
    PerSession,

    /// <summary>
    /// A new instance is created per call.
    /// </summary>
    PerCall
}