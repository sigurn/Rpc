using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Sigurn.Rpc;

public class ProcessHost : IDisposable, IChannelHost
{
    private static readonly ILogger _logger = RpcLogging.CreateLogger<ProcessHost>();

    private static IChannel DefaultChannelFactory(IChannel channel) => channel;
    private static IProtocol DefaultProtocolFactory() => new ChannelProtocol();

    private readonly object _lock = new ();
    private readonly Func<IProtocol> _protocolFactory = DefaultProtocolFactory;
    private readonly Func<IChannel, IChannel> _channelFactory = DefaultChannelFactory;

    private IChannel? _channel;
    private volatile bool _isOpened = false;

    public ProcessHost()
    {
        _isOpened = false;
    }

    public ProcessHost(Func<IChannel, IChannel> channelfactory)
        : this ()
    {
        _channelFactory = channelfactory;
    }

    public ProcessHost(Func<IProtocol> protocolFactory)
        : this ()
    {
        _protocolFactory = protocolFactory;
    }

    public ProcessHost(Func<IProtocol> protocolFactory, Func<IChannel, IChannel> channelfactory)
        : this ()
    {
        _protocolFactory = protocolFactory;
        _channelFactory = channelfactory;
    }

    public void Dispose()
    {
        Close();
    }

    public bool IsOpened
    {
        get
        {
            lock(_lock)
                return _isOpened;
        }

        private set
        {
            lock(_lock)
                _isOpened = value;
        }
    }

    public void Open()
    {
        using var _ = _logger.Scope();
        lock(_lock)
        {
            if (IsOpened) return;

            IsOpened = true;
        }

        var inputStream = Console.OpenStandardInput();
        var outputStream = Console.OpenStandardOutput();

        if (inputStream is null || outputStream is null)
            throw new InvalidOperationException("Cannot get input or output or both streams of the current process");

        Console.SetIn(TextReader.Null);
        Console.SetOut(TextWriter.Null);

        var channel = new ProcessChannel(outputStream, inputStream, _protocolFactory());
        ThreadPool.QueueUserWorkItem<IChannel>(x => OnConnected(x), channel, true);
        _logger.LogDebug("Channel host is opened");
    }

    public void Close()
    {
        using var _ = _logger.Scope();
        CancellationTokenSource? cancellationTokenSource = null;
        try
        {
            lock(_lock)
            {
                if (!IsOpened) return;
                IsOpened = false;
            }

            if (cancellationTokenSource is not null)
                cancellationTokenSource.Cancel();

            IChannel? channel;
            lock(_lock)
            {
                channel = _channel;
                _channel = null;
            }

            if (channel is not null && channel.State != ChannelState.Closed)
            {
                channel.CloseAsync(CancellationToken.None).Wait();
                if (channel is IDisposable d)
                    d.Dispose();
            }
        }
        finally
        {
            cancellationTokenSource?.Dispose();
            _logger.LogDebug("Channel host is closed");
        }
    }

    public event EventHandler<ChannelEventArgs>? Connected;
    public event EventHandler<ChannelEventArgs>? Disconnected;

    private void OnConnected(IChannel baseChannel)
    {
        _logger.LogDebug("Client is connected: {0}", baseChannel);

        var channel = _channelFactory(baseChannel);
        EventHandler? handler = null;
        handler = (object? sender, EventArgs args) =>
        {
            channel.Faulted -= handler;
            channel.Closed -= handler;
            OnDisconnected(channel);
        };

        channel.Faulted += handler;
        channel.Closed += handler;

        lock(_lock)
            _channel = channel;
        
        Connected?.Invoke(this, new ChannelEventArgs(channel));
    }

    private void OnDisconnected(IChannel channel)
    {
        _logger.LogDebug("Client is disconnected: {0}", channel);

        lock(_lock)
        {
            if (_channel == channel)
                _channel = null;
        }

        Disconnected?.Invoke(this, new ChannelEventArgs(channel));
    }
};
