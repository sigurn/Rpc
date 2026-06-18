namespace Sigurn.Rpc;

/// <summary>
/// Requires the calling session to be authenticated before the decorated member can be accessed.
/// </summary>
/// <remarks>
/// Can be applied to a method, property, event, or an entire interface. When applied to an
/// interface the requirement is enforced for all of its members.
/// </remarks>
[AttributeUsage(AttributeTargets.Method |
                AttributeTargets.Property |
                AttributeTargets.Event |
                AttributeTargets.Interface,
                AllowMultiple = false, Inherited = false)]
public class RequireAuthenticatedAttribute : Attribute
{
}