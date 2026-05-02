namespace Sigurn.Rpc;

public interface IAsyncRunnable
{
    public bool IsRunning { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
