using System.Collections.Immutable;

namespace Sigurn.Rpc;

public interface IProtocol
{
    bool IsSending { get; }
    bool IsReceiving { get; }

    void StartSending(ReadOnlySpan<byte> data);
    byte[]? GetNextBlockToSend();
    void EndSending();

    int StartReceiving();
    int ApplyNextReceivedBlock(ReadOnlySpan<byte> data);
    byte[] EndReceiving();
}