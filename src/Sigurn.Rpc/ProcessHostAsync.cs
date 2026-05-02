using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

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

        protected override async Task<IChannel> Accept(CancellationToken cancellationToken)
        {
            using var _ = _logger.Scope();

            IChannel? channel = Interlocked.Exchange(ref _channel, _channel);

            cancellationToken.ThrowIfCancellationRequested();

            if (channel is not null)
            {
                await cancellationToken.WaitForCancellationAsync();
                throw new OperationCanceledException();
            }
                
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
            if (_channel is IAsyncDisposable ad)
                await ad.DisposeAsync();
            else if (_channel is IDisposable d)
                d.Dispose();
        }
    }

    private static readonly ILogger _logger = RpcLogging.CreateLogger<ProcessHost>();

    private static IChannel DefaultChannelFactory(IChannel channel) => channel;
    private static IProtocol DefaultProtocolFactory() => new ChannelProtocol();

    public static IAsyncChannelAcceptor Open()
    {
        return Open(DefaultChannelFactory, DefaultProtocolFactory);
    }

    public static IAsyncChannelAcceptor Open(Func<IChannel, IChannel> channelFactory)
    {
        return Open(channelFactory, DefaultProtocolFactory);        
    }

    public static IAsyncChannelAcceptor Open(Func<IChannel, IChannel> channelFactory, Func<IProtocol> protocolFactory)
    {
        return new ProcessAcceptor(channelFactory, protocolFactory);
    }
};
