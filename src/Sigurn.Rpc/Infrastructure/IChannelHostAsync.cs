namespace Sigurn.Rpc;

public interface IChannelHostAsync
{
    bool IsAccepting { get; }

    Task<IChannel> AcceptAsync(CancellationToken cancellationToken);
}