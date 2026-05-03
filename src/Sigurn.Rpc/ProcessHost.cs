using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Sigurn.Rpc;

/// <summary>
/// Hosts a process channel using the standard input and output streams of the current process.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of <see cref="ProcessHost"/> with default protocol and channel factories.
    /// </summary>
    public ProcessHost()
    {
        _isOpened = false;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ProcessHost"/> with a custom channel factory.
    /// </summary>
    /// <param name="channelFactory">The factory used to wrap the accepted channel.</param>
    public ProcessHost(Func<IChannel, IChannel> channelFactory)
        : this ()
    {
        _channelFactory = channelFactory;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ProcessHost"/> with a custom protocol factory.
    /// </summary>
    /// <param name="protocolFactory">The factory used to create the protocol for the channel.</param>
    public ProcessHost(Func<IProtocol> protocolFactory)
        : this ()
    {
        _protocolFactory = protocolFactory;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ProcessHost"/> with custom protocol and channel factories.
    /// </summary>
    /// <param name="protocolFactory">The factory used to create the protocol for the channel.</param>
    /// <param name="channelFactory">The factory used to wrap the accepted channel.</param>
    public ProcessHost(Func<IProtocol> protocolFactory, Func<IChannel, IChannel> channelFactory)
        : this ()
    {
        _protocolFactory = protocolFactory;
        _channelFactory = channelFactory;
    }

    /// <summary>
    /// Closes the host and releases all resources.
    /// </summary>
    public void Dispose()
    {
        Close();
    }

    /// <summary>
    /// Gets a value indicating whether the host is currently open.
    /// </summary>
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

    /// <summary>
    /// Opens the host, redirects standard I/O to the channel, and notifies when a connection is established.
    /// </summary>
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

    /// <summary>
    /// Closes the host and the underlying process channel.
    /// </summary>
    public void Close()
    {
        using var _ = _logger.Scope();
        try
        {
            lock(_lock)
            {
                if (!IsOpened) return;
                IsOpened = false;
            }

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
            _logger.LogDebug("Channel host is closed");
        }
    }

    /// <summary>
    /// Occures when a client connects via the process channel.
    /// </summary>
    public event EventHandler<ChannelEventArgs>? Connected;

    /// <summary>
    /// Occures when the client disconnects or the channel faults.
    /// </summary>
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
