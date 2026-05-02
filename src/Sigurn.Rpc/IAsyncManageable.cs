namespace Sigurn.Rpc;

public interface IAsyncManageable
{
    bool IsStarted();

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
