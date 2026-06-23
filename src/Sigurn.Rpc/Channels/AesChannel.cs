using System.Security.Cryptography;
using Sigurn.Rpc.Infrastructure;
using Sigurn.Serialize;

namespace Sigurn.Rpc.Channels;

/// <summary>
/// Implements a channel decorator that encrypts outgoing packets and decrypts incoming packets using AES.
/// </summary>
public class AesChannel : ProcessionChannel
{
    private static readonly AsyncLocal<bool?> _isEncryped = new();

    /// <summary>
    /// Sets the encryption scope for the current async context, overriding the default encryption behavior.
    /// </summary>
    /// <param name="isEncrypted">If <see langword="true"/>, packets in this scope will be encrypted.</param>
    /// <returns>A disposable that restores the previous encryption scope when disposed.</returns>
    public static IDisposable SetEncryptionScope(bool isEncrypted)
    {
        var oldIsEncrypted = _isEncryped.Value;
        _isEncryped.Value = isEncrypted;
        return Disposable.Create(() => _isEncryped.Value = oldIsEncrypted);        
    }

    private static bool IsEncrypted
    {
        get
        {
            if (_isEncryped.Value.HasValue)
                return _isEncryped.Value.Value;
            return true;
        }
    }

    /// <summary>
    /// Defines packet properties used by <see cref="AesChannel"/>.
    /// </summary>
    public enum Property
    {
        /// <summary>
        /// Indicates whether the packet is encrypted.
        /// </summary>
        IsEncrypted
    }

    private static readonly byte[] _marker = [0x45, 0x4E, 0x43, 0x41, 0x45, 0x53, 0xF2, 0x7D, 0x8E, 0xFD];
    private static readonly Aes _aes = Aes.Create();
    
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of <see cref="AesChannel"/> wrapping the specified channel.
    /// </summary>
    /// <param name="channel">The underlying channel to wrap.</param>
    public AesChannel(IChannel channel)
        : base(channel)
    {
    }

    private byte[]? _key;
    private byte[]? _iv;

    /// <summary>
    /// Sets the AES encryption key and initialization vector.
    /// </summary>
    /// <param name="key">The AES key, or <see langword="null"/> to disable encryption.</param>
    /// <param name="iv">The AES initialization vector, or <see langword="null"/> to disable encryption.</param>
    public void SetKey(byte[]? key, byte[]? iv)
    {
        lock (_lock)
        {
            _key = key;
            _iv = iv;
        }
    }

    private (byte[]? key, byte[]? iv) GetKey()
    {
        lock (_lock)
        {
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

        (byte[]? key, byte[]? iv) = GetKey();
        if (key is null || iv is null)
            throw new InvalidOperationException("Received encrypted package but there is no encryption keys");

        using var srcStream = new MemoryStream(packet.Data[pos..]);
        using var dstStream = new MemoryStream();
        using (var crypto = new CryptoStream(srcStream, _aes.CreateDecryptor(key, iv), CryptoStreamMode.Read))
        {
            var _ = await Serializer.FromStreamAsync<int>(crypto, SerializationContext.Default with { AllowNullValues = false, ByteOrder = ByteOrder.Network }, cancellationToken).ConfigureAwait(false);
            await crypto.CopyToAsync(dstStream, cancellationToken).ConfigureAwait(false);
        }

        return new Packet(packet, dstStream.ToArray());
    }

    protected override async Task<IPacket> ProcessSendingPacket(IPacket packet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(cancellationToken);

        var isEncrypted = IsEncrypted;
        if (packet.Properties.TryGetValue(Property.IsEncrypted, out var value) && value is bool flag)
            isEncrypted = flag;

        if (!isEncrypted)
            return packet;

        (byte[]? key, byte[]? iv) = GetKey();
        if (key is null || iv is null) return packet;

        var salt = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);

        using var srcStream = new MemoryStream(packet.Data);
        using var dstStream = new MemoryStream();
        using (var crypto = new CryptoStream(dstStream, _aes.CreateEncryptor(key, iv), CryptoStreamMode.Write))
        {
            await dstStream.WriteAsync(_marker, cancellationToken).ConfigureAwait(false);
            await Serializer.ToStreamAsync(crypto, salt, SerializationContext.Default with { AllowNullValues = false, ByteOrder = ByteOrder.Network }, cancellationToken).ConfigureAwait(false);
            await srcStream.CopyToAsync(crypto, cancellationToken).ConfigureAwait(false);
        }        

        return new Packet(packet, dstStream.ToArray());
    }
}