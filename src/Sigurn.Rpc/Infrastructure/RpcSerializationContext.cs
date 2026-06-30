using Sigurn.Rpc.Infrastructure.Packets;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Infrastructure;

record class RpcSerializationContext : SerializationContext
{
    private readonly static InterfaceSerializer _serializer = new();
    private readonly static StreamSerializer _streamSerializer = new();

    public RpcSerializationContext(Session session)
        : base(RpcPacket.DefaultSerializationContext)
    {
        Session = session;
    }

    public Session Session { get; }
    
    public override ITypeSerializer? FindTypeSerializer(Type type)
    {
        if (type == typeof(Stream))
            return _streamSerializer;

        if (_serializer.IsTypeSupported(type))
            return _serializer;

        return base.FindTypeSerializer(type);
    }
}