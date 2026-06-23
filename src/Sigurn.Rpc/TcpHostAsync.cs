using System.Net;
using System.Net.Sockets;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

/// <summary>
/// Provides factory methods for creating asynchronous TCP channel acceptors.
/// </summary>
public static class TcpHostAsync
{
    class TcpAcceptor : BaseAcceptor,  ILocalAddress
    {
        private readonly Socket _socket;
        private readonly Func<IProtocol> _protocolFactory;
        private readonly Func<IChannel, IChannel> _channelFactory;

        public TcpAcceptor(Socket socket, Func<IProtocol> protocolFactory, Func<IChannel, IChannel> channelFactory)
            : base (channelFactory)
        {
            ArgumentNullException.ThrowIfNull(socket);
            ArgumentNullException.ThrowIfNull(protocolFactory);
            ArgumentNullException.ThrowIfNull(channelFactory);

            _socket = socket;
            _protocolFactory = protocolFactory;
            _channelFactory = channelFactory;
        }

        public string LocalAddress => _socket?.LocalEndPoint?.ToString() ?? string.Empty;

        protected override async Task<IChannel?> Accept(CancellationToken cancellationToken)
        {
            var socket = await _socket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            if (socket is null) return null;
            return new TcpChannel(socket, _protocolFactory());
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
    /// Creates a TCP channel acceptor listening on the <see cref="DefaultEndPoint"/>.
    /// </summary>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept connections.</returns>
    public static IAsyncChannelAcceptor Open()
    {
        return Open(DefaultEndPoint, DefaultChannelFactory, DefaultProtocolFactory);
    }

    /// <summary>
    /// Creates a TCP channel acceptor listening on the specified endpoint.
    /// </summary>
    /// <param name="endPoint">The endpoint to listen on.</param>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept connections.</returns>
    public static IAsyncChannelAcceptor Open(IPEndPoint endPoint)
    {
        return Open(endPoint, DefaultChannelFactory, DefaultProtocolFactory);
    }

    /// <summary>
    /// Creates a TCP channel acceptor listening on the specified endpoint with a custom channel factory.
    /// </summary>
    /// <param name="endPoint">The endpoint to listen on.</param>
    /// <param name="channelFactory">The factory used to wrap each accepted channel.</param>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept connections.</returns>
    public static IAsyncChannelAcceptor Open(IPEndPoint endPoint, Func<IChannel, IChannel> channelFactory)
    {
        return Open(endPoint, channelFactory, DefaultProtocolFactory);
    }

    /// <summary>
    /// Creates a TCP channel acceptor listening on the specified endpoint with custom channel and protocol factories.
    /// </summary>
    /// <param name="endPoint">The endpoint to listen on.</param>
    /// <param name="channelFactory">The factory used to wrap each accepted channel.</param>
    /// <param name="protocolFactory">The factory used to create a protocol for each accepted channel.</param>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept connections.</returns>
    public static IAsyncChannelAcceptor Open(IPEndPoint endPoint, Func<IChannel, IChannel> channelFactory, Func<IProtocol> protocolFactory)
    {
        var socket = new Socket(endPoint.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(endPoint);
        socket.Listen();

        return new TcpAcceptor(socket, protocolFactory, channelFactory);
    }
}
