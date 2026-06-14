using System.Threading.Tasks.Sources;

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

    public static async Task WaitForStateAsync(this IChannel channel, ChannelState state, CancellationToken cancellationToken)
    {
        if (state == ChannelState.Created)
            throw new ArgumentOutOfRangeException($"Cannot wait for {ChannelState.Created} state");

        if (channel.State == state) return;

        TaskCompletionSource tcs = new ();
        var waitTask = cancellationToken
            .WaitForCancellationAsync()
            .ContinueWith(t => tcs.TrySetCanceled());

        void handler(object? sender, EventArgs args)
        {
            tcs.SetResult();
        }

        switch(state)
        {
            case ChannelState.Opening:
                try
                {
                    channel.Opening += handler;
                    await tcs.Task;
                }
                finally
                {
                    channel.Opening -= handler;
                    waitTask.Dispose();
                }
                break;

            case ChannelState.Opened:
                try
                {
                    channel.Opened += handler;
                    await tcs.Task;
                }
                finally
                {
                    channel.Opened -= handler;
                    waitTask.Dispose();
                }
                break;

            case ChannelState.Closing:
                try
                {
                    channel.Closing += handler;
                    await tcs.Task;
                }
                finally
                {
                    channel.Closing -= handler;
                    waitTask.Dispose();
                }
                break;

            case ChannelState.Closed:
                try
                {
                    channel.Closed += handler;
                    await tcs.Task;
                }
                finally
                {
                    channel.Closed -= handler;
                    waitTask.Dispose();
                }
                break;

            case ChannelState.Faulted:
                try
                {
                    channel.Faulted += handler;
                    await tcs.Task;
                }
                finally
                {
                    channel.Faulted -= handler;
                    waitTask.Dispose();
                }
                break;
            
            default:
                throw new ArgumentOutOfRangeException($"Unsupported state {state}");
        }
    }
}