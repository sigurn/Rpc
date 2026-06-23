namespace Sigurn.Rpc;

/// <summary>
/// Disables the RPC timeout for the decorated method. The call will wait indefinitely for a server response.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class NoRpcTimeoutAttribute : Attribute { }
