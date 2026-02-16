namespace Sigurn.Rpc;

public interface IManageableAsync
{
    bool IsStarted();

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public interface IManageable
{
    bool IsStarted();
    void Start();
    void Stop();
}