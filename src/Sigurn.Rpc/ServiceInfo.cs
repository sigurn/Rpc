using Sigurn.Rpc.Infrastructure;
using Sigurn.Serialize;

namespace Sigurn.Rpc;

/// <summary>
/// Describes a service registered on a host, including its interface type and sharing scope.
/// </summary>
public class ServiceInfo : ISerializable
{
    /// <summary>
    /// Gets the fully qualified name of the service interface.
    /// </summary>
    public string InterfaceName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the <see cref="Type"/> of the service interface, or <see langword="null"/> if the type is not available locally.
    /// </summary>
    public Type? InterfaceType { get; private set; }

    /// <summary>
    /// Gets the sharing scope of the service instances.
    /// </summary>
    public ShareWithin ShareType { get; private set; }

    /// <summary>
    /// Creates a new <see cref="ServiceInfo"/> from the specified interface type and sharing scope.
    /// </summary>
    /// <param name="type">The interface type of the service.</param>
    /// <param name="shareType">The sharing scope of the service instances.</param>
    /// <returns>A new <see cref="ServiceInfo"/> instance.</returns>
    public static ServiceInfo Create(Type type, ShareWithin shareType)
    {
        ArgumentNullException.ThrowIfNull(type);

        return new ServiceInfo()
        {
            InterfaceType = type,
            ShareType = shareType,
            InterfaceName = type.FullName ?? string.Empty
        };
    }

    /// <summary>
    /// Deserializes this instance from the specified stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="context">The serialization context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        var typeGuid = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken);
        InterfaceName = (await Serializer.FromStreamAsync<string>(stream, context, cancellationToken)) ?? string.Empty;
        ShareType = await Serializer.FromStreamAsync<ShareWithin>(stream, context, cancellationToken);
        InterfaceType = GetTypeById(typeGuid);
    }

    /// <summary>
    /// Serializes this instance to the specified stream asynchronously.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="context">The serialization context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        if (InterfaceType is null)
            throw new InvalidOperationException("Interface type cannot be null");
        await Serializer.ToStreamAsync(stream, InterfaceType.GUID, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, InterfaceName, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, ShareType, context, cancellationToken);
    }

    private static Type? GetTypeById(Guid id)
    {
        var type = InterfaceTypeRegistry.GetTypeById(id);
        if (type is not null) return type;
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.GUID == id);
    }
}

 