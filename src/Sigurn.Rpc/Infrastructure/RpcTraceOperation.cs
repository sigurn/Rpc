namespace Sigurn.Rpc.Infrastructure;

/// <summary>
/// The kind of interface member access being traced by an adapter or a proxy.
/// </summary>
public enum RpcTraceOperation
{
    /// <summary>A method call.</summary>
    MethodCall,

    /// <summary>Reading a property value.</summary>
    PropertyGet,

    /// <summary>Writing a property value.</summary>
    PropertySet,

    /// <summary>Subscribing to an event.</summary>
    EventAttach,

    /// <summary>Unsubscribing from an event.</summary>
    EventDetach,

    /// <summary>Delivering event data.</summary>
    EventRaise,
}
