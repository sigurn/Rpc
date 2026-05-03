namespace Sigurn.Rpc;

/// <summary>
/// Marks an interface as a remote RPC interface that can be consumed over a channel.
/// </summary>
[AttributeUsage(AttributeTargets.Interface)]
public class RemoteInterfaceAttribute : Attribute
{
}