using System.Collections.Immutable;

namespace Sigurn.Rpc;

public class ChannelProtocol : IProtocol
{
    private readonly ImmutableArray<byte> _startMarker = [0xA5, 0xB6, 0xC7, 0xD8];
    private readonly ImmutableArray<byte> _version = [0x01, 0x00];

    private enum Step
    {
        None,
        StartMarker,
        Header,
        HeaderCrc,
        Body,
        BodyCrc
    }

    private enum ReceiveError
    {
        None,
        CannotReadHeader,
        CannotReadHeaderCrc,
        InvalidHeaderCrc,
        UnsupportedVersion,
        InvalidPacketLength,
        TooLongPacket,
        CannotReadBody,
        CannotReadBodyCrc,
        InvalidBodyCrc,
    }

    private readonly object _sendLock = new ();
    private volatile bool _isSending;
    private volatile Step _sendingStep;
    private byte[] _sendingData = [];
    private byte[] _sendingBlock = [];


    private readonly object _receiveLock = new ();
    private volatile bool _isReceiving;
    private volatile Step _recevingStep;
    private byte[]? _receivedData = null;
    private int _receivePosition = 0;
    private int _bodyLength = 0;

    private ReceiveError _receiveError;

    public int MaxPacketSize = 0x100000;

    public bool IsSending
    { 
        get
        {
            lock(_sendLock)
                return _isSending;
        }
    }

    public bool IsReceiving
    { 
        get
        {
            lock(_sendLock)
                return _isReceiving;
        }
    }

    public void StartSending(ReadOnlySpan<byte> data)
    {
        lock(_sendLock)
        {
            if (_isSending)
                throw new InvalidOperationException("Cannot start a new sending operation before previous one is completed.");
            _isSending = true;
            _sendingStep = Step.StartMarker;
            _sendingData = data.ToArray();
        }
    }

    public byte[]? GetNextBlockToSend()
    {
        lock(_sendLock)
        {
            if (!_isSending)
                throw new InvalidOperationException("Sending is not started yet. Please start sending first.");

            switch(_sendingStep)
            {
                case Step.StartMarker:
                    _sendingBlock = _startMarker.ToArray();
                    _sendingStep = Step.Header;
                    break;

                case Step.Header:
                    _sendingBlock = _version.Concat(BitConverter.IsLittleEndian ? 
                        BitConverter.GetBytes(_sendingData.Length).Reverse() : 
                        BitConverter.GetBytes(_sendingData.Length)).ToArray();
                    _sendingStep = Step.HeaderCrc;
                    break;

                case Step.HeaderCrc:
                    _sendingBlock = BitConverter.GetBytes(Crc32.ComputeCrc(_sendingBlock));
                    if (BitConverter.IsLittleEndian)
                        _sendingBlock = _sendingBlock.Reverse().ToArray();
                    _sendingStep = Step.Body;
                    break;

                case Step.Body:
                    _sendingBlock = _sendingData;
                    _sendingStep = Step.BodyCrc;
                    break;

                case Step.BodyCrc:
                    _sendingBlock = BitConverter.GetBytes(Crc32.ComputeCrc(_sendingBlock));
                    if (BitConverter.IsLittleEndian)
                        _sendingBlock = _sendingBlock.Reverse().ToArray();
                    _sendingStep = Step.None;
                    break;

                default:
                    return null;
            }

            return _sendingBlock;
        }
    }

    public void EndSending()
    {
        lock(_sendLock)
        {
            if (!_isSending)
                throw new InvalidOperationException("There is no sending operation at the moment");

            _sendingStep = Step.None;
            _isSending = false;
        }
    }

    public int StartReceiving()
    {
        lock(_receiveLock)
        {
            if (_isReceiving)
                throw new InvalidOperationException("Cannot start a new receiving operation before previous one is completed.");

            _isReceiving = true;
            _recevingStep = Step.StartMarker;
            _receivePosition = 0;
            _receiveError = ReceiveError.None;
        }

        return 1;
    }

    public int ApplyNextReceivedBlock(ReadOnlySpan<byte> data)
    {
        lock(_receiveLock)
        {
            if (!_isReceiving)
                throw new InvalidOperationException("Receiving is not started yet. Please start receiving first.");

            switch(_recevingStep)
            {
                case Step.StartMarker:
                    if (data.Length == 1 && _startMarker[_receivePosition] == data[0])
                        _receivePosition++;
                    else
                        _receivePosition = 0;

                    if (_receivePosition >= _startMarker.Length)
                    {
                        _receivePosition = 0;
                        _recevingStep = Step.Header;
                        return 6;
                    }

                    return 1;
                
                case Step.Header:
                    if (data.Length < 6)
                    {
                        _receivePosition = 0;
                        _receiveError = ReceiveError.CannotReadHeader;
                        _recevingStep = Step.None;
                        return 0;
                    }

                    _receivedData = data[..6].ToArray();
                    _recevingStep = Step.HeaderCrc;
                    return 4;

                case Step.HeaderCrc:
                    if (data.Length < 4)
                    {
                        _receiveError = ReceiveError.CannotReadHeaderCrc;
                        _recevingStep = Step.None;
                        return 0;
                    }

                    var headerCrcData = data[..4].ToArray();
                    
                    if (BitConverter.IsLittleEndian)
                        headerCrcData = headerCrcData.Reverse().ToArray();

                    var headerCrc = BitConverter.ToUInt32(headerCrcData);
                    if (headerCrc != Crc32.ComputeCrc(_receivedData ?? [], 0, 6))
                    {
                        _receiveError = ReceiveError.InvalidHeaderCrc;
                        _recevingStep = Step.None;
                        return 0;
                    }                    

                    if (_receivedData is null)
                    {
                        _receiveError = ReceiveError.CannotReadHeader;
                        _recevingStep = Step.None;
                        return 0;
                    }                    

                    if (_version[0] != _receivedData[0] || _version[1] != _receivedData[1])
                    {
                        _receivePosition = 0;
                        _receiveError = ReceiveError.UnsupportedVersion;
                        _recevingStep = Step.None;
                        return 0;
                    }

                    var lenData = _receivedData.Skip(2).Take(4).ToArray();
                    if (BitConverter.IsLittleEndian)
                        lenData = lenData.Reverse().ToArray();
                    var len = BitConverter.ToInt32(lenData);
                    if (len < 0)
                    {
                        _receivePosition = 0;
                        _receiveError = ReceiveError.InvalidPacketLength;
                        _recevingStep = Step.None;
                        return 0;
                    }

                    if (len > MaxPacketSize)
                    {
                        _receivePosition = 0;
                        _receiveError = ReceiveError.TooLongPacket;
                        _recevingStep = Step.None;
                        return 0;
                    }

                    _bodyLength = len;
                    _receivedData = null;
                    _recevingStep = Step.Body;
                    return len;
                
                case Step.Body:
                    if (data.Length < _bodyLength)
                    {
                        _receiveError = ReceiveError.CannotReadBody;
                        _recevingStep = Step.None;
                        return 0;
                    }

                    _receivedData = data[.._bodyLength].ToArray();
                    _recevingStep = Step.BodyCrc;
                    return 4;

                case Step.BodyCrc:
                    if (data.Length < 4)
                    {
                        _receiveError = ReceiveError.CannotReadBodyCrc;
                        _recevingStep =  Step.None;
                        return 0;
                    }

                    var bodyCrcData = data[..4].ToArray();
                    if (BitConverter.IsLittleEndian)
                        bodyCrcData = bodyCrcData.Reverse().ToArray();
                    
                    var crc = BitConverter.ToUInt32(bodyCrcData);
                    if (crc != Crc32.ComputeCrc(_receivedData ?? []))
                    {
                        _receiveError = ReceiveError.InvalidBodyCrc;
                        _recevingStep =  Step.None;
                        return 0;
                    }

                    _receiveError = ReceiveError.None;
                    _recevingStep = Step.None;
                    return 0;
                
                default:
                    return 0;
            }
        }
    }

    public byte[] EndReceiving()
    {
        lock(_receiveLock)
        {
            _isReceiving = false;
            _recevingStep = Step.None;

            switch(_receiveError)
            {
                case ReceiveError.CannotReadHeader:
                    throw new ProtocolException("Cannot read all bytes of the packaet header.");

                case ReceiveError.CannotReadHeaderCrc:
                    throw new ProtocolException("Cannot read all bytes of the packet header CRC.");

                case ReceiveError.InvalidHeaderCrc:
                    throw new ProtocolException("The packet has invalid header CRC.");

                case ReceiveError.InvalidPacketLength:
                    throw new ProtocolException("The packet has invalid packet length.");

                case ReceiveError.TooLongPacket:
                    throw new ProtocolException("The packet is too long.");

                case ReceiveError.UnsupportedVersion:
                    throw new ProtocolException("The packet has unsupported protocol version.");

                case ReceiveError.CannotReadBody:
                    throw new ProtocolException("Cannot read all bytes of the packet data.");

                case ReceiveError.CannotReadBodyCrc:
                    throw new ProtocolException("Cannot read all bytes of the packaet data CRC.");

                case ReceiveError.InvalidBodyCrc:
                    throw new ProtocolException("The packet has invalid data CRC.");
            }

            return _receivedData?.Take(_bodyLength)?.ToArray() ?? [];
        }
    }
}
