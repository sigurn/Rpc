namespace Sigurn.Rpc;

public interface IAsyncChannelHost
{
    bool IsOpened { get; }

    bool IsAccepting { get; }

    public Task OpenAsync(CancellationToken cancellationToken);
    
    public Task<IChannel> AcceptAsync(CancellationToken cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken);
}