using System.IO.Compression;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Channels;

public class GZipChannel : ProcessionChannel
{
    public enum Property
    {
        IsCompressed
    }

    private static readonly byte[] _marker = [0x47, 0x5A, 0x49, 0x50, 0xF2, 0x3F, 0xDD, 0xF9];

    public GZipChannel(IChannel channel)
        : base(channel)
    {
    }

    protected override async Task<IPacket> ProcessReceivedPacket(IPacket packet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(cancellationToken);

        packet.Properties[Property.IsCompressed] = false;
        if (packet.Data.Length < _marker.Length)
            return packet;

        int pos = 0;
        foreach (var b in _marker)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.Data[pos++] != b)
                return packet;
        }

        using var dstStream = new MemoryStream();
        using var srcStream = new MemoryStream(packet.Data[pos..]);
        using var gzipStream = new GZipStream(srcStream, CompressionLevel.Optimal);

        await gzipStream.CopyToAsync(dstStream);

        return new Packet(packet, dstStream.ToArray());
    }

    protected override async Task<IPacket> ProcessSendingPacket(IPacket packet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(cancellationToken);

        bool isCompressed = true;
        if (packet.Properties.TryGetValue(Property.IsCompressed, out var value) && value is bool flag)
            isCompressed = flag;

        if (!isCompressed)
            return packet;

        using var dstStream = new MemoryStream();
        using var srcStream = new MemoryStream(packet.Data);
        using var gzipStream = new GZipStream(dstStream, CompressionLevel.Optimal);

        await dstStream.WriteAsync(_marker, cancellationToken);
        await srcStream.CopyToAsync(gzipStream);

        return new Packet(packet, dstStream.ToArray());
    }
}