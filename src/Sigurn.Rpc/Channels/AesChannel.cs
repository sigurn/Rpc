using System.Security.Cryptography;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Channels;

public class AesChannel : ProcessionChannel
{
    public enum Property
    {
        IsEncrypted
    }

    private static readonly byte[] _marker = [0x45, 0x4E, 0x43, 0x41, 0x45, 0x53, 0xF2, 0x7D, 0x8E, 0xFD];
    private static readonly Aes _aes = Aes.Create();
    private readonly object _lock = new();

    public AesChannel(IChannel channel)
        : base(channel)
    {
    }

    private byte[]? _key;
    private byte[]? _iv;

    public void SetKey(byte[]? key, byte[]? iv)
    {
        lock (_lock)
        {
            _key = key;
            _iv = iv;
        }
    }

    private (byte[] key, byte[] iv) GetKey()
    {
        lock (_lock)
        {
            if (_key is null || _iv is null)
                throw new InvalidDataException("AES key is not defined");
            return (_key, _iv);
        }
    }

    protected override async Task<IPacket> ProcessReceivedPacket(IPacket packet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(cancellationToken);

        packet.Properties[Property.IsEncrypted] = false;
        if (packet.Data.Length < _marker.Length)
            return packet;

        int pos = 0;
        foreach (var b in _marker)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (packet.Data[pos++] != b) return packet;
        }

        packet.Properties[Property.IsEncrypted] = true;

        (byte[] key, byte[] iv) = GetKey();

        using var srcStream = new MemoryStream(packet.Data[pos..]);
        using var dstStream = new MemoryStream();
        using var crypto = new CryptoStream(srcStream, _aes.CreateDecryptor(key, iv), CryptoStreamMode.Read);

        var _ = await Serializer.FromStreamAsync<int>(crypto, SerializationContext.Default with { AllowNullValues = false, ByteOrder = ByteOrder.Network }, cancellationToken);
        await crypto.CopyToAsync(dstStream, cancellationToken);

        return new Packet(packet, dstStream.ToArray());
    }

    protected override async Task<IPacket> ProcessSendingPacket(IPacket packet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(cancellationToken);

        var isEncrypted = true;
        if (packet.Properties.TryGetValue(Property.IsEncrypted, out var value) && value is bool flag)
            isEncrypted = flag;

        if (!isEncrypted)
            return packet;

        var salt = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
        (byte[] key, byte[] iv) = GetKey();

        using var srcStream = new MemoryStream(packet.Data);
        using var dstStream = new MemoryStream();
        using var crypto = new CryptoStream(dstStream, _aes.CreateEncryptor(key, iv), CryptoStreamMode.Write);

        await dstStream.WriteAsync(_marker, cancellationToken);
        await Serializer.ToStreamAsync(crypto, salt, SerializationContext.Default with { AllowNullValues = false, ByteOrder = ByteOrder.Network }, cancellationToken);
        await srcStream.CopyToAsync(crypto, cancellationToken);

        return new Packet(packet, dstStream.ToArray());
    }
}