namespace Sigurn.Rpc.Infrastructure;

static class WaitHandleExtensions
{
    private static readonly  TimeSpan Infinite = new TimeSpan(-1);

    public static Task<bool> WaitOneAsync(this WaitHandle handle, CancellationToken cancellationToken)
    {
        return WaitOneAsync(handle, Infinite, cancellationToken);
    }

    public static Task<bool> WaitOneAsync(this WaitHandle handle, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var taskSource = new TaskCompletionSource<bool>();

        if (cancellationToken.IsCancellationRequested) return Task.FromResult(false);

        var ctr = cancellationToken.Register(() => taskSource.TrySetCanceled());
        var rwh = ThreadPool.RegisterWaitForSingleObject(handle, (state, isTimedOut) => 
        {
            if (state is TaskCompletionSource<bool> tcs)
                tcs.TrySetResult(!isTimedOut);
        }, taskSource, (int)(timeout == Infinite ? -1 : timeout.TotalMilliseconds), true);

        var task = taskSource.Task;
        
        _ = task.ContinueWith(t =>
        {
            ctr.Dispose();
            rwh.Unregister(null);
        });

        return task;
    }
}