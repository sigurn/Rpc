using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

/// <summary>
/// Provides factory methods for creating asynchronous process channel acceptors using standard I/O streams.
/// </summary>
public static class ProcessHostAsync
{
    class ProcessAcceptor : BaseAcceptor
    {
        private static readonly ILogger _logger = RpcLogging.CreateLogger<ProcessHost>();

        private readonly Func<IProtocol> _protocolFactory;
        private readonly Func<IChannel, IChannel> _channelFactory;

        private IChannel? _channel;


        public ProcessAcceptor(Func<IChannel, IChannel> channelFactory, Func<IProtocol> protocolFactory)
        {
            _channelFactory = channelFactory;
            _protocolFactory = protocolFactory;
        }

        protected override async Task<IChannel?> Accept(CancellationToken cancellationToken)
        {
            using var _ = _logger.Scope();

            IChannel? channel = Interlocked.Exchange(ref _channel, _channel);

            cancellationToken.ThrowIfCancellationRequested();

            if (channel is not null) return null;
                
            var inputStream = Console.OpenStandardInput();
            var outputStream = Console.OpenStandardOutput();

            if (inputStream is null || outputStream is null)
                throw new InvalidOperationException("Cannot get input or output or both streams of the current process");

            Console.SetIn(TextReader.Null);
            Console.SetOut(TextWriter.Null);

            var baseChannel = new ProcessChannel(outputStream, inputStream, _protocolFactory());
            channel = _channelFactory(baseChannel);

            EventHandler? handler = null;
            handler = (object? sender, EventArgs args) =>
            {
                channel.Faulted -= handler;
                channel.Closed -= handler;

                Interlocked.Exchange(ref _channel, null);
                (channel as IDisposable)?.Dispose();

                ProcessTermination.Cancel("Client is disconnected");

                _logger.LogInformation("Client is disconnected. Channel: {channel}", channel);
            };

            channel.Faulted += handler;
            channel.Closed += handler;

            Interlocked.Exchange(ref _channel, channel);

            _logger.LogInformation("Client is connected. Channel: {baseChannel}", baseChannel);

            return channel;
        }

        protected override async Task InternalDispose()
        {
        }
    }

    private static readonly ILogger _logger = RpcLogging.CreateLogger<ProcessHost>();

    private static IChannel DefaultChannelFactory(IChannel channel) => channel;
    private static IProtocol DefaultProtocolFactory() => new ChannelProtocol();

    /// <summary>
    /// Creates a process channel acceptor using default protocol and channel factories.
    /// </summary>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept a connection via standard I/O.</returns>
    public static IAsyncChannelAcceptor Open()
    {
        return Open(DefaultChannelFactory, DefaultProtocolFactory);
    }

    /// <summary>
    /// Creates a process channel acceptor with a custom channel factory.
    /// </summary>
    /// <param name="channelFactory">The factory used to wrap the accepted channel.</param>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept a connection via standard I/O.</returns>
    public static IAsyncChannelAcceptor Open(Func<IChannel, IChannel> channelFactory)
    {
        return Open(channelFactory, DefaultProtocolFactory);
    }

    /// <summary>
    /// Creates a process channel acceptor with custom channel and protocol factories.
    /// </summary>
    /// <param name="channelFactory">The factory used to wrap the accepted channel.</param>
    /// <param name="protocolFactory">The factory used to create the protocol for the channel.</param>
    /// <returns>An <see cref="IAsyncChannelAcceptor"/> ready to accept a connection via standard I/O.</returns>
    public static IAsyncChannelAcceptor Open(Func<IChannel, IChannel> channelFactory, Func<IProtocol> protocolFactory)
    {
        return new ProcessAcceptor(channelFactory, protocolFactory);
    }
};
