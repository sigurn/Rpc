namespace Sigurn.Rpc;

public interface ICrcComputer<T>
{
    T InitCrc();

    T AddBlock(T crc, ReadOnlySpan<byte> data);

    T CompleteCrc(T crc);
}