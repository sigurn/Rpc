using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure.Packets;

sealed class MethodCallPacket : RpcPacket
{
    public MethodCallPacket()
        : base(PacketType.MethodCall)
    { 
    }

    private bool _oneWay = false;
    public bool OneWay
    { 
        get => _oneWay;
        init => _oneWay = value;
    }

    private Guid _instanceId;
    public Guid InstanceId
    { 
        get => _instanceId;
        init => _instanceId = value;
    }

    private int _methodId = -1;
    public int MethodId
    {
        get => _methodId;
        init => _methodId = value;
    }

    private IReadOnlyList<byte[]> _args = [];
    public IReadOnlyList<byte[]> Args
    { 
        get => _args;
        init => _args = value;
    }

    protected override async Task FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        _oneWay = await Serializer.FromStreamAsync<bool>(stream, context, cancellationToken);
        _instanceId = await Serializer.FromStreamAsync<Guid>(stream, context, cancellationToken);
        _methodId = await Serializer.FromStreamAsync<int>(stream, context, cancellationToken);
        _args = await Serializer.FromStreamAsync<IReadOnlyList<byte[]>>(stream, context, cancellationToken) ?? [];
    }

    protected override async Task ToStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)
    {
        await Serializer.ToStreamAsync(stream, _oneWay, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, _instanceId, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, _methodId, context, cancellationToken);
        await Serializer.ToStreamAsync(stream, _args, context, cancellationToken);
    }
}