using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc;

public class ProcessHostAsync : IDisposable, IChannelHostAsync
{
    private static readonly object _slock = new ();
    private static readonly ILogger _logger = RpcLogging.CreateLogger<ProcessHost>();

    private static IChannel DefaultChannelFactory(IChannel channel) => channel;
    private static IProtocol DefaultProtocolFactory() => new ChannelProtocol();

    private static CancellationTokenSource? _pcts = null;
    public static CancellationToken GetProcessCancellationToken()
    {
        CancellationTokenSource cts;

        lock(_slock)
        {
            if (_pcts != null) return _pcts.Token;

            cts = new CancellationTokenSource();
            _pcts = cts;
        }

        PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
        {
            _logger.LogDebug("SIGINT received (Ctrl+C)");
            cts.Cancel();
        });

        PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            _logger.LogDebug("SIGTERM received (taskkill/kill)");
            cts.Cancel();
        });

        // Console.CancelKeyPress += (s,a) =>
        // {
        //     cts.Cancel();
        // };

        // AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        // {
        //     cts.Cancel();
        // };

        return cts.Token;
    }

    private readonly object _lock = new ();
    private readonly Func<IProtocol> _protocolFactory = DefaultProtocolFactory;
    private readonly Func<IChannel, IChannel> _channelFactory = DefaultChannelFactory;

    private IChannel? _channel;

    private readonly CancellationTokenSource _cts = new ();
    private volatile bool _isAccepting = false;


    public ProcessHostAsync()
    {
    }

    public ProcessHostAsync(Func<IChannel, IChannel> channelFactory)
        : this ()
    {
        _channelFactory = channelFactory;
    }

    public ProcessHostAsync(Func<IProtocol> protocolFactory)
        : this ()
    {
        _protocolFactory = protocolFactory;
    }

    public ProcessHostAsync(Func<IProtocol> protocolFactory, Func<IChannel, IChannel> channelFactory)
        : this ()
    {
        _protocolFactory = protocolFactory;
        _channelFactory = channelFactory;
    }

    public void Dispose()
    {
        IChannel? channel;

        lock(_lock)
        {
            if (_isAccepting)
                _cts.Cancel();
            channel = _channel;
            _channel = null;            
        }

        ((IDisposable?)channel)?.Dispose();
    }

    public bool IsAccepting
    {
        get
        {
            lock(_lock)
                return _isAccepting;
        }

        private set
        {
            lock(_lock)
                _isAccepting = value;
        }
    }

    public async Task<IChannel> AcceptAsync(CancellationToken cancellationToken)
    {
        using var _ = _logger.Scope();
        lock(_lock)
        {
            if (_isAccepting)
                throw new InvalidOperationException("Host is already accepting connection. Cannot start parallel accept operation.");
            _isAccepting = true;
        }

        IChannel? channel = null;
        try
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken).Token;
            lock(_lock)
                channel = _channel;

            cancellationToken.ThrowIfCancellationRequested();

            if (channel is not null)
            {
                await cancellationToken.WaitForCancellationAsync();

                lock(_lock)
                    _channel = null;

                ((IDisposable)channel)?.Dispose();
                
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

                lock(_lock)
                    _channel = null;

                _logger.LogInformation("Client is disconnected. Channel: {channel}", channel);
            };

            channel.Faulted += handler;
            channel.Closed += handler;

            lock(_lock)
                _channel = channel;    

            _logger.LogInformation("Client is connected. Channel: {baseChannel}", baseChannel);

            return channel;
        }
        finally
        {
            lock(_lock)
                _isAccepting = false;
        }
    }
};
