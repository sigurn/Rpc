namespace Sigurn.Rpc.Infrastructure;

class Packet : IPacket
{
    private readonly byte[] _data;
    public Packet(byte[] data)
    {
        Id = Guid.NewGuid();
        _data = data;
    }

    public Packet(IPacket packet)
    {
        Id = packet.Id;
        _data = (byte[])packet.Data.Clone();
        Properties = new Dictionary<Enum, object>(packet.Properties);
    }

    public Packet(IPacket packet, byte[] data)
    {
        Id = packet.Id;
        _data = data;
        Properties = new Dictionary<Enum, object>(packet.Properties);
    }

    public Guid Id { get; }

    public IDictionary<Enum, object> Properties { get; } = new Dictionary<Enum, object>();

    public byte[] Data => _data;

    public IPacket Clone()
    {
        return new Packet(this);
    }

    public IPacket CloneWithNewData(byte[] data)
    {
        return new Packet(this, data);
    }
}