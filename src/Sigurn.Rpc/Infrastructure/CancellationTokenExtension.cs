namespace Sigurn.Rpc.Infrastructure;

public static class CancellationTokenExtension
{
    public static async Task WaitForCancellationAsync(this CancellationToken cancellationToken)
    {
        if(cancellationToken.IsCancellationRequested) return;
        await cancellationToken.WaitHandle.WaitOneAsync(CancellationToken.None);
    }
}