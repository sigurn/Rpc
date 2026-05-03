namespace Sigurn.Rpc;

/// <summary>
/// Defines a contract for computing a CRC checksum incrementally over data blocks.
/// </summary>
/// <typeparam name="T">The type of the CRC value.</typeparam>
public interface ICrcComputer<T>
{
    /// <summary>
    /// Initializes a new CRC computation and returns the initial CRC value.
    /// </summary>
    /// <returns>The initial CRC value.</returns>
    T InitCrc();

    /// <summary>
    /// Processes the next block of data and returns the updated CRC value.
    /// </summary>
    /// <param name="crc">The current CRC value.</param>
    /// <param name="data">The data block to process.</param>
    /// <returns>The updated CRC value after processing the block.</returns>
    T AddBlock(T crc, ReadOnlySpan<byte> data);

    /// <summary>
    /// Completes the CRC computation and returns the final CRC value.
    /// </summary>
    /// <param name="crc">The current CRC value.</param>
    /// <returns>The final CRC value.</returns>
    T CompleteCrc(T crc);
}