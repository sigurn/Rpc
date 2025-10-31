using Sigurn.Serialize;

namespace Sigurn.Rpc;

public class ServiceInfo : ISerializable
{
    public string InterfaceName { get; private set; } = string.Empty;
    public Type? InterfaceType { get; private set; }
    public ShareWithin ShareType { get; private set; }

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

    public async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        var typeGuid = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken);
        InterfaceName = (await Serializer.FromStreamAsync<string>(stream, context, cancellationToken)) ?? string.Empty;
        ShareType = await Serializer.FromStreamAsync<ShareWithin>(stream, context, cancellationToken);
        InterfaceType = GetTypeById(typeGuid);
    }

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
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.GUID == id);
    }
}

 