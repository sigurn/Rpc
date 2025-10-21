using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Sigurn.Rpc;

public class SslChannel : BaseChannel, IAutenticatedChannel
{
    private readonly IPEndPoint _endPoint;
    private readonly X509Certificate? _certificate;
    private Socket? _socket;
    private SslStream? _sslStream;
    private Func<X509Certificate?, X509Chain?, bool>? _certificateValidator;
    private string? _serverName;

    private readonly IProtocol _protocol = new ChannelProtocol();

    internal SslChannel(Socket socket, X509Certificate certificate, Func<X509Certificate?, X509Chain?, bool>? certificateValidator, bool requireClientCertificate, IProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(protocol);

        if (socket.RemoteEndPoint is not IPEndPoint ipep)
            throw new ArgumentException("The channel does supports TCP/IP sockets only");

        _endPoint = ipep;
        _certificate = certificate;
        _certificateValidator = certificateValidator;
        _socket = socket;
        _protocol = protocol;

        _sslStream = new SslStream(new NetworkStream(_socket), false, ValidateRemoteCertificate);
        _sslStream.AuthenticateAsServer(_certificate, requireClientCertificate, SslProtocols.Tls13 | SslProtocols.Tls12, true);

        State = ChannelState.Opened;
    }

    public SslChannel(IPEndPoint endPoint, Func<X509Certificate?, X509Chain?, bool> certificateValidator)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(certificateValidator);

        _endPoint = endPoint;
        _certificateValidator = certificateValidator;
        _socket = null;
    }

    public SslChannel(IPEndPoint endPoint, Func<X509Certificate?, X509Chain?, bool>? certificateValidator, IProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(protocol);

        _endPoint = endPoint;
        _certificateValidator = certificateValidator;
        _protocol = protocol;
        _socket = null;
    }

    public SslChannel(IPEndPoint endPoint, Func<X509Certificate?, X509Chain?, bool>? certificateValidator, X509Certificate certificate, IProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(protocol);

        _endPoint = endPoint;
        _certificate = certificate;
        _certificateValidator = certificateValidator;
        _protocol = protocol;
        _socket = null;
    }

    public SslChannel(IPEndPoint endPoint, X509Certificate certificate, IProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(protocol);

        _endPoint = endPoint;
        _certificate = certificate;
        _protocol = protocol;
        _socket = null;
    }

    public SslChannel(IPEndPoint endPoint)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        _endPoint = endPoint;
        _socket = null;
    }

    public IPEndPoint LocalEndPoint
    {
        get
        {
            lock (_lock)
                return _endPoint;
        }
    }

    public IPEndPoint RemoteEndPoint
    {
        get
        {
            lock (_lock)
                return (IPEndPoint)(_socket?.RemoteEndPoint ?? throw new InvalidOperationException("Remote endpoint is not available"));
        }
    }
    
    public string? ServerName
    {
        get
        {
            lock (_lock)
                return _serverName;
        }

        set
        {
            lock (_lock)
                _serverName = value;
        }
    }

    public X509Certificate2? RemoteCertificate
    {
        get
        {
            lock (_lock)
            {
                if (_sslStream is null) return null;
                if (_sslStream.RemoteCertificate is X509Certificate2 c2) return c2;
                if (_sslStream.RemoteCertificate is X509Certificate c1) return new X509Certificate2(c1);
                return null;
            }
        }
    }
    
    protected override async Task InternalOpenAsync(CancellationToken cancellationToken)
    {
        var socket = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        await socket.ConnectAsync(_endPoint, cancellationToken);

        var sslStrem = new SslStream(new NetworkStream(socket), false, ValidateRemoteCertificate);

        SslClientAuthenticationOptions authOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
            TargetHost = ServerName,
            RemoteCertificateValidationCallback = ValidateRemoteCertificate
        };

        if (_certificate is not null)
            authOptions.ClientCertificates = new X509CertificateCollection(new X509Certificate[] { _certificate });

        await sslStrem.AuthenticateAsClientAsync(authOptions, cancellationToken);

        lock (_lock)
            _socket = socket;
    }

    protected override Task InternalCloseAsync(CancellationToken cancellationToken)
    {
        Socket? socket;
        SslStream? sslStream;

        lock (_lock)
        {
            sslStream = _sslStream;
            _sslStream = null;

            socket = _socket;
            _socket = null;
        }

        if (sslStream is not null)
        {
            sslStream.Close();
            sslStream.Dispose();
        }

        if (socket is not null)
        {
            socket.Close();
            socket.Dispose();
        }

        return Task.CompletedTask;
    }

    protected override async Task<IPacket> InternalReceiveAsync(CancellationToken cancellationToken)
    {
        byte[] buf = [];

        SslStream stream;
        lock (_lock)
        {
            if (_sslStream is null)
                throw new InvalidOperationException("There is no opened connection to receive data from");

            stream = _sslStream;
        }

        int size = _protocol.StartReceiving();

        try
        {
            while (size != 0)
                size = _protocol.ApplyNextReceivedBlock(await ReceiveData(stream, size, cancellationToken));

            return IPacket.Create(_protocol.EndReceiving());
        }
        catch (SocketException ex)
        {
            _protocol.EndReceiving();

            if (ex.SocketErrorCode == SocketError.OperationAborted)
                throw new OperationCanceledException("Receve operation was cancelled", ex);

            GoToFaultedState();
            throw;
        }
        catch
        {
            _protocol.EndReceiving();
            throw;
        }
    }

    protected override async Task<IPacket> InternalSendAsync(IPacket packet, CancellationToken cancellationToken)
    {
        SslStream stream;
        lock (_lock)
        {
            if (_sslStream is null)
                throw new InvalidOperationException("There is no opened connection to send data to");

            stream = _sslStream;
        }

        _protocol.StartSending(packet.Data);

        try
        {
            byte[]? buf = null;

            do
            {
                buf = _protocol.GetNextBlockToSend();
                if (buf is not null)
                    await SendData(stream, buf, cancellationToken);
            }
            while (buf is not null);

            _protocol.EndSending();
        }
        catch (SocketException ex)
        {
            _protocol.EndSending();

            if (ex.SocketErrorCode == SocketError.OperationAborted)
                throw new OperationCanceledException("Send operation was cancelled", ex);

            GoToFaultedState();
            throw;
        }
        catch
        {
            if (_protocol.IsSending)
                _protocol.EndSending();
            throw;
        }

        return packet;
    }

    private static async Task<byte[]> ReceiveData(Stream stream, int size, CancellationToken cancellationToken)
    {
        var buf = new byte[size];
        int pos = 0;

        while (pos < size)
        {
            var len = await stream.ReadAsync(new Memory<byte>(buf, pos, size - pos), cancellationToken);
            if (len == 0)
                throw new SocketException((int)SocketError.ConnectionAborted);

            pos += len;
        }

        return buf;
    }

    private static async Task SendData(Stream stream, byte[] data, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(data, cancellationToken);
    }

    private bool ValidateRemoteCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (_certificateValidator is null)
            return sslPolicyErrors == SslPolicyErrors.None;

        return _certificateValidator(certificate, chain);
    }
}