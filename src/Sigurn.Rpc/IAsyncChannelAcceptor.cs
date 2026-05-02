namespace Sigurn.Rpc;

public interface IAsyncChannelAcceptor : IAsyncDisposable
{
    bool IsAccepting { get; }

    public Task<IChannel> AcceptAsync(CancellationToken cancellationToken);
}