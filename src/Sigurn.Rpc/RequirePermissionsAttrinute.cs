namespace Sigurn.Rpc;

/// <summary>
/// Requires the calling session to hold the specified permissions before the decorated member can be accessed.
/// </summary>
/// <typeparam name="T">The enumeration type that defines the available permissions.</typeparam>
/// <param name="permissions">The permissions required to access the decorated member.</param>
/// <remarks>
/// Can be applied to a method, property, event, or an entire interface. When applied to an
/// interface the requirement is enforced for all of its members.
/// </remarks>
[AttributeUsage(AttributeTargets.Method |
                AttributeTargets.Property |
                AttributeTargets.Event |
                AttributeTargets.Interface,
                AllowMultiple = false, Inherited = false)]
public class RequirePermissionsAttribute<T>(params T[] permissions) : Attribute where T : Enum
{
    private readonly T[] _permissions = [.. permissions];

    /// <summary>
    /// Gets the permissions required to access the decorated member.
    /// </summary>
    public T[] Permissions => _permissions;
}
