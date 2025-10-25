using Sigurn.Rpc.Channels;
using Moq;
using System.Security.Cryptography;

namespace Sigurn.Rpc.Tests;

public class AesChannelTests
{
    [Fact]
    public void CannotCreateInstanceWithNullChannel()
    {
#pragma warning disable CS8625
        Assert.Throws<ArgumentNullException>(() => new AesChannel(null));
#pragma warning restore CS8625
    }

    [Fact]
    public async Task SendUnencryptedPackage()
    {
        var mock = new Mock<IChannel>();
        using AesChannel aesChannel = new AesChannel(mock.Object);

        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        aesChannel.SetKey(aes.Key, aes.IV);

        IPacket? sentPacket = null;

        mock.Setup(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<IPacket, CancellationToken>((p, ct) => sentPacket = p)
            .Returns<IPacket, CancellationToken>((p, ct) =>
            {
                sentPacket = p;
                return Task.FromResult(p);
            });

        var packet = IPacket.Create(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        packet.Properties[AesChannel.Property.IsEncrypted] = false;

        await aesChannel.SendAsync(packet, CancellationToken.None);
        Assert.NotNull(sentPacket);
        Assert.Equal(packet.Data, sentPacket.Data);

        mock.Verify(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendUnencryptedPackageWhenKeysAreMissing()
    {
        var mock = new Mock<IChannel>();
        using AesChannel aesChannel = new AesChannel(mock.Object);

        IPacket? sentPacket = null;

        mock.Setup(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<IPacket, CancellationToken>((p, ct) => sentPacket = p)
            .Returns<IPacket, CancellationToken>((p, ct) =>
            {
                sentPacket = p;
                return Task.FromResult(p);
            });

        var packet = IPacket.Create(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        await aesChannel.SendAsync(packet, CancellationToken.None);
        Assert.NotNull(sentPacket);
        Assert.Equal(packet.Data, sentPacket.Data);

        mock.Verify(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendUnencryptedPackageByContext()
    {
        var mock = new Mock<IChannel>();
        using AesChannel aesChannel = new AesChannel(mock.Object);

        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        aesChannel.SetKey(aes.Key, aes.IV);

        IPacket? sentPacket = null;

        mock.Setup(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<IPacket, CancellationToken>((p, ct) => sentPacket = p)
            .Returns<IPacket, CancellationToken>((p, ct) =>
            {
                sentPacket = p;
                return Task.FromResult(p);
            });

        var packet = IPacket.Create(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        using (AesChannel.SetEncryptionScope(false))
            await aesChannel.SendAsync(packet, CancellationToken.None);
        Assert.NotNull(sentPacket);
        Assert.Equal(packet.Data, sentPacket.Data);

        mock.Verify(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private readonly byte[] _marker = [0x45, 0x4E, 0x43, 0x41, 0x45, 0x53, 0xF2, 0x7D, 0x8E, 0xFD];

    [Fact]
    public async Task SendEncryptedPackage()
    {
        var mock = new Mock<IChannel>();
        using AesChannel aesChannel = new AesChannel(mock.Object);

        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        aesChannel.SetKey(aes.Key, aes.IV);

        IPacket? sentPacket = null;

        mock.Setup(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<IPacket, CancellationToken>((p, ct) => sentPacket = p)
            .Returns<IPacket, CancellationToken>((p, ct) =>
            {
                sentPacket = p;
                return Task.FromResult(p);
            });

        var packet = IPacket.Create(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        await aesChannel.SendAsync(packet, CancellationToken.None);

        Assert.NotNull(sentPacket);
        Assert.Equal(26, sentPacket.Data.Length);
        Assert.Equal(_marker, sentPacket.Data[.._marker.Length]);

        mock.Verify(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiveEncryptedPackage()
    {
        var mock = new Mock<IChannel>();
        using AesChannel aesChannel = new AesChannel(mock.Object);

        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        aesChannel.SetKey(aes.Key, aes.IV);

        IPacket? sentPacket = null;

        mock.Setup(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<IPacket, CancellationToken>((p, ct) => sentPacket = p)
            .Returns<IPacket, CancellationToken>((p, ct) =>
            {
                sentPacket = p;
                return Task.FromResult(p);
            });
        mock.Setup(x => x.ReceiveAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>((ct) => Task.FromResult(sentPacket ?? throw new Exception("Packet is not defined")));

        var packet = IPacket.Create(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        await aesChannel.SendAsync(packet, CancellationToken.None);

        Assert.NotNull(sentPacket);
        Assert.Equal(26, sentPacket.Data.Length);
        Assert.Equal(_marker, sentPacket.Data[.._marker.Length]);

        var receivedPacket = await aesChannel.ReceiveAsync(CancellationToken.None);
        Assert.Equal(packet.Data, receivedPacket.Data);
        Assert.True(receivedPacket.Properties.ContainsKey(AesChannel.Property.IsEncrypted));
        Assert.True((bool)receivedPacket.Properties[AesChannel.Property.IsEncrypted]);

        mock.Verify(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(x => x.ReceiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceiveUnencryptedPackage()
    {
        var mock = new Mock<IChannel>();
        using AesChannel aesChannel = new AesChannel(mock.Object);

        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        aesChannel.SetKey(aes.Key, aes.IV);

        IPacket? sentPacket = null;

        mock.Setup(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()))
            .Callback<IPacket, CancellationToken>((p, ct) => sentPacket = p)
            .Returns<IPacket,CancellationToken>((p, ct) =>
            {
                sentPacket = p;
                return Task.FromResult(p);
            });
        mock.Setup(x => x.ReceiveAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>((ct) => Task.FromResult(sentPacket ?? throw new Exception("Packet is not defined")));

        var packet = IPacket.Create(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        using(AesChannel.SetEncryptionScope(false))
            await aesChannel.SendAsync(packet, CancellationToken.None);

        Assert.NotNull(sentPacket);
        Assert.Equal(10, sentPacket.Data.Length);

        var receivedPacket = await aesChannel.ReceiveAsync(CancellationToken.None);
        Assert.Equal(packet.Data, receivedPacket.Data);
        Assert.True(receivedPacket.Properties.ContainsKey(AesChannel.Property.IsEncrypted));
        Assert.False((bool)receivedPacket.Properties[AesChannel.Property.IsEncrypted]);

        mock.Verify(x => x.SendAsync(It.IsAny<IPacket>(), It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(x => x.ReceiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}