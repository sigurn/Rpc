using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

/// <summary>
/// Provides factory methods for creating asynchronous SSL/TLS channel acceptors.
/// </summary>
public static class SslHostAsync
{
    class SslAcceptor : BaseAcceptor,  ILocalAddress
    {
        private readonly Socket _socket;
        private readonly Func<IProtocol> _protocolFactory;
        private readonly Func<IChannel, IChannel> _channelFactory;
        private readonly X509Certificate _certificate;
        private readonly Func<X509Certificate?, X509Chain?, bool>? _certificateValidator;
        private readonly bool _requireClientCertificate;

        public SslAcceptor(Socket socket, X509Certificate certificate, Func<X509Certificate?, X509Chain?, bool>? certificateValidator, bool requireClientCertificate, Func<IProtocol> protocolFactory, Func<IChannel, IChannel> channelFactory)
        {
            ArgumentNullException.ThrowIfNull(socket);
            ArgumentNullException.ThrowIfNull(certificate);
            ArgumentNullException.ThrowIfNull(protocolFactory);
            ArgumentNullException.ThrowIfNull(channelFactory);

            _socket = socket;
            _certificate = certificate;
            _certificateValidator = certificateValidator;
            _requireClientCertificate = requireClientCertificate;
            _protocolFactory = protocolFactory;
            _channelFactory = channelFactory;
        }

        public string LocalAddress => _socket?.LocalEndPoint?.ToString() ?? string.Empty;

        protected override async Task<IChannel> Accept(CancellationToken cancellationToken)
        {
            var socket = await _socket.AcceptAsync(cancellationToken);
            var channel = _channelFactory(new SslChannel(socket, _certificate, _certificateValidator, _requireClientCertificate, _protocolFactory()));

            return channel;
        }

        protected override Task InternalDispose()
        {
            _socket.Close();
            _socket.Dispose();

            return Task.CompletedTask;
        }
    }

    private static IChannel DefaultChannelFactory(IChannel channel) => channel;
    private static IProtocol DefaultProtocolFactory() => new ChannelProtocol();

    /// <summary>
    /// Gets the default endpoint used when no endpoint is specified (<see cref="IPAddress.Any"/> on port 35768).
    /// </summary>
    public static IPEndPoint DefaultEndPoint = new IPEndPoint(IPAddress.Any, 35768);

    /// <summary>
    /// Creates an SSL channel acceptor on the <see cref="DefaultEndPoint"/> with the specified server certificate.
    /// </summary>
    /// <param name="certificate">The server certificate used for SSL authentication.</param>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept SSL connections.</returns>
    public static IAsyncChannelAcceptor Open(X509Certificate certificate)
    {
        return Open(DefaultEndPoint, certificate, null, true, DefaultChannelFactory, DefaultProtocolFactory);
    }

    /// <summary>
    /// Creates an SSL channel acceptor on the specified endpoint with the specified server certificate.
    /// </summary>
    /// <param name="endPoint">The endpoint to listen on.</param>
    /// <param name="certificate">The server certificate used for SSL authentication.</param>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept SSL connections.</returns>
    public static IAsyncChannelAcceptor Open(IPEndPoint endPoint, X509Certificate certificate)
    {
        return Open(endPoint, certificate, null, true, DefaultChannelFactory, DefaultProtocolFactory);
    }

    /// <summary>
    /// Creates an SSL channel acceptor on the specified endpoint with a custom channel factory.
    /// </summary>
    /// <param name="endPoint">The endpoint to listen on.</param>
    /// <param name="certificate">The server certificate used for SSL authentication.</param>
    /// <param name="channelFactory">The factory used to wrap each accepted channel.</param>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept SSL connections.</returns>
    public static IAsyncChannelAcceptor Open(IPEndPoint endPoint, X509Certificate certificate, Func<IChannel, IChannel> channelFactory)
    {
        return Open(endPoint, certificate, null, true, channelFactory, DefaultProtocolFactory);
    }

    /// <summary>
    /// Creates an SSL channel acceptor with full control over certificate validation, client certificate requirement, and custom factories.
    /// </summary>
    /// <param name="endPoint">The endpoint to listen on.</param>
    /// <param name="certificate">The server certificate used for SSL authentication.</param>
    /// <param name="certificateValidator">The callback to validate client certificates, or <see langword="null"/> to use default validation.</param>
    /// <param name="requireClientCertificate">If <see langword="true"/>, clients must present a certificate.</param>
    /// <param name="channelFactory">The factory used to wrap each accepted channel.</param>
    /// <param name="protocolFactory">The factory used to create a protocol for each accepted channel.</param>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept SSL connections.</returns>
    public static IAsyncChannelAcceptor Open(IPEndPoint endPoint, X509Certificate certificate, Func<X509Certificate?, X509Chain?, bool>? certificateValidator, bool requireClientCertificate, Func<IChannel, IChannel> channelFactory, Func<IProtocol> protocolFactory)
    {
        var socket = new Socket(endPoint.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(endPoint);
        socket.Listen();

        return new SslAcceptor(socket, certificate, certificateValidator, requireClientCertificate, protocolFactory, channelFactory);
    }
}
