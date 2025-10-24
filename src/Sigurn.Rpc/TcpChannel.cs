using System.Net;
using System.Net.Sockets;

namespace Sigurn.Rpc;

public class TcpChannel : BaseChannel, IAddressableChannel
{
    private readonly IPEndPoint _endPoint;
    private Socket? _socket;
    private readonly IProtocol _protocol = new ChannelProtocol();

    internal TcpChannel(Socket socket, IProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(protocol);

        if (socket.RemoteEndPoint is not IPEndPoint ipep)
            throw new ArgumentException("The channel supports TCP/IP sockets only");

        _endPoint = ipep;
        _socket = socket;
        _protocol = protocol;

        State = ChannelState.Opened;
    }

    public TcpChannel(IPEndPoint endPoint, IProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(protocol);

        _endPoint = endPoint;
        _protocol = protocol;
        _socket = null;
    }

    public TcpChannel(IPEndPoint endPoint)
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

    string IAddressableChannel.LocalAddress
    {
        get
        {
            lock (_lock)
                return _endPoint.ToString();
        }
    }
    
    string IAddressableChannel.RemoteAddress
    {
        get
        {
            lock (_lock)
                return _socket?.RemoteEndPoint?.ToString() ?? string.Empty;
        }
    }
    
    protected override async Task InternalOpenAsync(CancellationToken cancellationToken)
    {
        var socket = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        await socket.ConnectAsync(_endPoint, cancellationToken);

        lock (_lock)
            _socket = socket;
    }

    protected override Task InternalCloseAsync(CancellationToken cancellationToken)
    {
        Socket? socket;
        lock(_lock)
        {
            socket = _socket;
            _socket = null;
        }

        if (socket is null) return Task.CompletedTask;

        socket.Shutdown(SocketShutdown.Both);
        socket.Close();
        socket.Dispose();

        return Task.CompletedTask;
    }

    protected override async Task<IPacket> InternalReceiveAsync(CancellationToken cancellationToken)
    {
        byte[] buf = [];

        Socket socket;
        lock(_lock)
        {
            if (_socket is null || !_socket.Connected)
                throw new InvalidOperationException("There is no opened socket to receive data from");

            socket = _socket;
        }

        int size = _protocol.StartReceiving();

        try
        {
            while(size != 0)
                size = _protocol.ApplyNextReceivedBlock(await ReceiveData(socket, size, cancellationToken));

            return IPacket.Create(_protocol.EndReceiving());
        }
        catch(SocketException ex)
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
        Socket socket;
        lock(_lock)
        {
            if (_socket is null || !_socket.Connected)
                throw new InvalidOperationException("There is no opened socket to send data to");

            socket = _socket;
        }

        _protocol.StartSending(packet.Data);

        try
        {
            byte[]? buf = null;

            do
            {
                buf = _protocol.GetNextBlockToSend();
                if (buf is not null)
                    await SendData(socket, buf, cancellationToken);
            }
            while(buf is not null);

            _protocol.EndSending();
        }
        catch(SocketException ex)
        {
            _protocol.EndSending();

            GoToFaultedState();

            if (ex.SocketErrorCode == SocketError.OperationAborted)
                throw new OperationCanceledException("Send operation was cancelled", ex);

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

    private static async Task<byte[]> ReceiveData(Socket socket, int size, CancellationToken cancellationToken)
    {
        var buf = new byte[size];
        int pos = 0;

        while(pos < size)
        {
            var len = await socket.ReceiveAsync(new Memory<byte>(buf, pos, size - pos), SocketFlags.None, cancellationToken).ConfigureAwait(false);
            if (len == 0)
                throw new SocketException((int)SocketError.ConnectionAborted);

            pos += len;
        }

        return buf;
    }

    private static async Task SendData(Socket socket, byte[] data, CancellationToken cancellationToken)
    {
        int pos = 0;

        while(pos < data.Length)
            pos += await socket.SendAsync( new ReadOnlyMemory<byte>(data, pos, data.Length - pos), SocketFlags.None, cancellationToken).ConfigureAwait(false);
    }
}