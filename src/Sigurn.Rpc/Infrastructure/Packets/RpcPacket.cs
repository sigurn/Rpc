using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

abstract class RpcPacket
{
    public static async Task<RpcPacket?> FromPacketAsync(IPacket packet, SerializationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(cancellationToken);

        using var stream = new MemoryStream(packet.Data);
        return await Serializer.FromStreamAsync<RpcPacket>(stream, context, cancellationToken);
    }

    public static async Task<T?> FromBytesAsync<T>(byte[] data, SerializationContext context, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(data);
        return await Serializer.FromStreamAsync<T>(stream, context, cancellationToken);
    }

    public static async Task<byte[]> ToBytesAsync<T>(T value, SerializationContext context, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await Serializer.ToStreamAsync<T>(stream, value, context, cancellationToken);
        return stream.ToArray();
    }

    internal enum PacketType : byte
    {
        Unknown = 0,
        Success,
        Exception,
        Error,
        GetInstance,
        ServiceInstance,
        ReleaseInstance,
        ServerException,
        MethodCall,
        MethodResult,
        GetProperty,
        PropertyValue,
        SetProperty,
        SubscribeForEvent,
        UnsubscribeFromEvent,
        EventDataPacket,
        CancelRequest,
    }

    internal static SerializationContext DefaultSerializationContext = SerializationContext.Default with
    {
        TypeSerializers =
        [
            new RpcPacketSerializer(),
            new EventArgsSerializer()
        ]
    };

    private readonly PacketType _packageType;

    protected RpcPacket(PacketType packageType)
    {
        _packageType = packageType;
        RequestId = Guid.NewGuid();
    }

    protected RpcPacket(PacketType packageType, RpcPacket package)
    {
        _packageType = packageType;
        RequestId = package.RequestId;
    }

    public Guid RequestId { get; private set; }

    protected abstract Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken);

    protected abstract Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken);

    public async Task<byte[]> ToBytesAsync(SerializationContext context, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await Serializer.ToStreamAsync(stream, this, context, cancellationToken);
        return stream.ToArray();
    }

    class RpcPacketSerializer : ITypeSerializer<RpcPacket>
    {
        public async Task<RpcPacket> FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
        {
            var requestId = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken);
            var packageType = await Serializer.FromStreamAsync<PacketType>(stream, context, cancellationToken);
            RpcPacket? package = null;
            switch (packageType)
            {
                case PacketType.GetInstance:
                    package = new GetInstancePacket();
                    break;

                case PacketType.ServiceInstance:
                    package = new ServiceInstancePacket();
                    break;

                case PacketType.ReleaseInstance:
                    package = new ReleaseInstancePacket();
                    break;

                case PacketType.Success:
                    package = new SuccessPacket();
                    break;

                case PacketType.Exception:
                    package = new ExceptionPacket();
                    break;

                case PacketType.Error:
                    package = new ErrorPacket();
                    break;

                case PacketType.MethodCall:
                    package = new MethodCallPacket();
                    break;

                case PacketType.MethodResult:
                    package = new MethodResultPacket();
                    break;

                case PacketType.GetProperty:
                    package = new GetPropertyPacket();
                    break;

                case PacketType.SetProperty:
                    package = new SetPropertyPacket();
                    break;

                case PacketType.PropertyValue:
                    package = new PropertyValuePacket();
                    break;

                case PacketType.SubscribeForEvent:
                    package = new SubscribeForEventPacket();
                    break;

                case PacketType.UnsubscribeFromEvent:
                    package = new UnsubscribeFromEventPacket();
                    break;

                case PacketType.EventDataPacket:
                    package = new EventDataPacket();
                    break;

                case PacketType.CancelRequest:
                    package = new CancelRequestPacket();
                    break;
            }

            if (package is null)
                throw new Exception("Unknown packet type");

            await package.FromStreamAsync(stream, context, cancellationToken);
            package.RequestId = requestId;
            return package;
        }

        public async Task ToStreamAsync(Stream stream, RpcPacket value, SerializationContext context, CancellationToken cancellationToken)
        {
            await Serializer.ToStreamAsync(stream, value.RequestId, context, cancellationToken);
            await Serializer.ToStreamAsync(stream, value._packageType, context, cancellationToken);
            await value.ToStreamAsync(stream, context, cancellationToken);
        }
    }
}

