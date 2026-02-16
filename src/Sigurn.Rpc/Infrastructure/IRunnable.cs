namespace Sigurn.Rpc;

public interface IRunnableAsync
{
    public bool IsRunning { get; }
    Task RunAsync(CancellationToken cancellationToken);
}

public interface IRunnable
{
    public bool IsRunning { get; }
    void Run(WaitHandle stopHandle);
}