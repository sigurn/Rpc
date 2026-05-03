using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Sigurn.Rpc;

/// <summary>
/// Hosts a synchronous SSL/TLS channel listener that accepts incoming connections and raises channel events.
/// </summary>
public class SslHost : IDisposable, IChannelHost
{
    private static IChannel DefaultChannelFactory(IChannel channel) => channel;
    private static IProtocol DefaultProtocolFactory() => new ChannelProtocol();

    private const int _defaultPort = 35769;

    private readonly object _lock = new ();
    private readonly Func<IProtocol> _protocolFactory = DefaultProtocolFactory;
    private readonly Func<IChannel, IChannel> _channelFactory = DefaultChannelFactory;

    private readonly HashSet<IChannel> _channels = [];

    private IPEndPoint _endPoint = new IPEndPoint(IPAddress.Loopback, _defaultPort);
    private X509Certificate? _certificate;
    private bool _requireClientCertificate = false;
    private Func<X509Certificate?, X509Chain?, bool>? _certificateValidator;
    private IPEndPoint? _listeningEndPoint;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _acceptTask;
    private volatile bool _isOpened = false;

    /// <summary>
    /// Initializes a new instance of <see cref="SslHost"/> with default protocol and channel factories.
    /// </summary>
    public SslHost()
    {
        _cancellationTokenSource = null;
        _isOpened = false;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SslHost"/> with custom protocol and channel factories.
    /// </summary>
    /// <param name="protocolFactory">The factory used to create a protocol for each accepted channel.</param>
    /// <param name="channelFactory">The factory used to wrap each accepted channel.</param>
    public SslHost(Func<IProtocol> protocolFactory, Func<IChannel, IChannel> channelFactory)
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
    /// Gets or sets the TCP endpoint to listen on. Cannot be changed while the host is open.
    /// </summary>
    public IPEndPoint EndPoint
    { 
        get
        {
            lock(_lock)
            {
                if (_listeningEndPoint is not null) return _listeningEndPoint;
                return _endPoint;
            }
        }

        set
        {
            lock(_lock)
            {
                if (IsOpened)
                    throw new InvalidOperationException("Cannot change end-point when host is opened.");

                _endPoint = value;
            }
        } 
    }
    
    /// <summary>
    /// Gets or sets the server certificate used during SSL authentication.
    /// </summary>
    public X509Certificate? Certificate
    {
        get
        {
            lock (_lock)
                return _certificate;
        }

        set
        {
            lock (_lock)
                _certificate = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether clients must present a certificate during authentication.
    /// </summary>
    public bool RequireClientCertificate
    {
        get
        {
            lock (_lock)
                return _requireClientCertificate;
        }

        set
        {
            lock (_lock)
                _requireClientCertificate = value;
        }
    }

    /// <summary>
    /// Gets or sets the callback used to validate client certificates, or <see langword="null"/> to use default validation.
    /// </summary>
    public Func<X509Certificate?, X509Chain?, bool>? CertificateValidator
    {
        get
        {
            lock (_lock)
                return _certificateValidator;
        }

        set
        {
            lock (_lock)
                _certificateValidator = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the host is currently open and accepting connections.
    /// </summary>
    public bool IsOpened
    {
        get
        {
            lock (_lock)
                return _isOpened;
        }

        private set
        {
            lock (_lock)
                _isOpened = value;
        }
    }

    /// <summary>
    /// Opens the host and starts listening for incoming SSL connections.
    /// </summary>
    public void Open()
    {
        EndPoint endPoint;
        CancellationToken cancellationToken;

        lock(_lock)
        {
            if (IsOpened) return;

            IsOpened = true;
            endPoint = EndPoint;
            if (_cancellationTokenSource is null)
            {
                _cancellationTokenSource = new CancellationTokenSource();
            }
            else if (_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
            }
            cancellationToken = _cancellationTokenSource.Token;
        }

        var socket = new Socket(EndPoint.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        Action<Task<Socket>> handler = x => {};
        handler = (Task<Socket> task) =>
        {
            try
            {
                OnConnected(task.Result);

                if (cancellationToken.IsCancellationRequested)
                {
                    socket?.Close();
                    socket?.Dispose();
                    return;
                }

                lock(_lock)
                {
                    if (_acceptTask is null) return;
                    _acceptTask = socket.AcceptAsync(cancellationToken).AsTask().ContinueWith(handler);
                }
            }
            catch
            {
                socket.Close();
                socket.Dispose();
            }
        };

        socket.Bind(endPoint);
        socket.Listen();
        lock(_lock)
        {
            _listeningEndPoint = (IPEndPoint?)socket.LocalEndPoint;
            _acceptTask = socket.AcceptAsync(cancellationToken).AsTask().ContinueWith(handler);
        }
    }

    /// <summary>
    /// Closes the host, stops accepting connections, and closes all active channels.
    /// </summary>
    public void Close()
    {
        CancellationTokenSource? cancellationTokenSource = null;
        Task? acceptTask = null;
        try
        {
            lock(_lock)
            {
                if (!IsOpened) return;
                IsOpened = false;
                cancellationTokenSource = _cancellationTokenSource;
                _cancellationTokenSource = null;
                acceptTask = _acceptTask;
                _acceptTask = null;
                _listeningEndPoint = null;
            }

            if (cancellationTokenSource is not null)
                cancellationTokenSource.Cancel();

            IChannel[] channels;
            lock(_channels)
            {
                channels = _channels.ToArray();
                _channels.Clear();
            }

            var tasks = channels
                .Where(x => x.State != ChannelState.Closed)
                .Select(x => x.CloseAsync(CancellationToken.None))
                .ToArray();
            
            Task.WaitAll(tasks);
            acceptTask?.Wait();

            foreach(var d in channels.Where(x => x is IDisposable).Select(x => (IDisposable)x))
                d.Dispose();
        }
        finally
        {
            cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// Occures when a client connects and a new SSL channel is created.
    /// </summary>
    public event EventHandler<ChannelEventArgs>? Connected;

    /// <summary>
    /// Occures when a client disconnects and the associated channel is closed or faulted.
    /// </summary>
    public event EventHandler<ChannelEventArgs>? Disconnected;

    private void OnConnected(Socket socket)
    {
        X509Certificate certificate = Certificate ?? throw new InvalidOperationException("Cannot accept SSL connections without server certificate");
        var channel = _channelFactory(new SslChannel(socket, certificate, CertificateValidator, RequireClientCertificate, _protocolFactory()));
        EventHandler? handler = null;
        handler = (object? sender, EventArgs args) =>
        {
            channel.Faulted -= handler;
            channel.Closed -= handler;
            OnDisconnected(channel);
        };

        channel.Faulted += handler;
        channel.Closed += handler;

        lock(_channels)
            _channels.Add(channel);
        
        Connected?.Invoke(this, new ChannelEventArgs(channel));
    }

    private void OnDisconnected(IChannel channel)
    {
        lock(_channels)
            if (_channels.Contains(channel))
                _channels.Remove(channel);

        Disconnected?.Invoke(this, new ChannelEventArgs(channel));
    }
};
