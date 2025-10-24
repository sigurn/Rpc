using System.Net;
using System.Net.Sockets;

namespace Sigurn.Rpc;

public class TcpHost : IDisposable, IChannelHost
{
    private static IChannel DefaultChannelFactory(IChannel channel) => channel;
    private static IProtocol DefaultProtocolFactory() => new ChannelProtocol();

    private const int _defaultPort = 35768;

    private readonly object _lock = new ();
    private readonly Func<IProtocol> _protocolFactory = DefaultProtocolFactory;
    private readonly Func<IChannel, IChannel> _channelFactory = DefaultChannelFactory;

    private readonly HashSet<IChannel> _channels = [];

    private IPEndPoint _endPoint = new IPEndPoint(IPAddress.Loopback, _defaultPort);
    private IPEndPoint? _listeningEndPoint;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _acceptTask;
    private volatile bool _isOpened = false;

    public TcpHost()
    {
        _cancellationTokenSource = null;
        _isOpened = false;
    }

    public TcpHost(Func<IChannel, IChannel> channelfactory)
        : this ()
    {
        _channelFactory = channelfactory;
    }

    public TcpHost(Func<IProtocol> protocolFactory)
        : this ()
    {
        _protocolFactory = protocolFactory;
    }

    public TcpHost(Func<IProtocol> protocolFactory, Func<IChannel, IChannel> channelfactory)
        : this ()
    {
        _protocolFactory = protocolFactory;
        _channelFactory = channelfactory;
    }

    public void Dispose()
    {
        Close();
    }

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
                    socket.Close();
                    socket.Dispose();
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

            acceptTask?.Wait();

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

            foreach(var d in channels.Where(x => x is IDisposable).Select(x => (IDisposable)x))
                d.Dispose();
        }
        finally
        {
            cancellationTokenSource?.Dispose();
        }
    }

    public event EventHandler<ChannelEventArgs>? Connected;
    public event EventHandler<ChannelEventArgs>? Disconnected;

    private void OnConnected(Socket socket)
    {
        var channel = _channelFactory(new TcpChannel(socket, _protocolFactory()));
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
