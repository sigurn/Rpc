using System.Collections.Immutable;

namespace Sigurn.Rpc;

/// <summary>
/// Defines the contract for a channel-level protocol that frames data into blocks for sending and receiving.
/// </summary>
public interface IProtocol
{
    /// <summary>
    /// Gets a value indicating whether a sending operation is currently in progress.
    /// </summary>
    bool IsSending { get; }

    /// <summary>
    /// Gets a value indicating whether a receiving operation is currently in progress.
    /// </summary>
    bool IsReceiving { get; }

    /// <summary>
    /// Starts a sending operation with the specified data.
    /// </summary>
    /// <param name="data">The raw data to send.</param>
    void StartSending(ReadOnlySpan<byte> data);

    /// <summary>
    /// Returns the next block of framed data to be transmitted, or <see langword="null"/> if all blocks have been produced.
    /// </summary>
    /// <returns>The next block to send, or <see langword="null"/> when sending is complete.</returns>
    byte[]? GetNextBlockToSend();

    /// <summary>
    /// Completes the current sending operation.
    /// </summary>
    void EndSending();

    /// <summary>
    /// Starts a receiving operation and returns the size of the first block to read.
    /// </summary>
    /// <returns>The number of bytes to read for the first block.</returns>
    int StartReceiving();

    /// <summary>
    /// Processes the next received block of data and returns the size of the next block to read, or zero when receiving is complete.
    /// </summary>
    /// <param name="data">The received data block.</param>
    /// <returns>The number of bytes to read for the next block, or zero when all data has been received.</returns>
    int ApplyNextReceivedBlock(ReadOnlySpan<byte> data);

    /// <summary>
    /// Completes the current receiving operation and returns the assembled payload.
    /// </summary>
    /// <returns>The fully received data.</returns>
    byte[] EndReceiving();
}