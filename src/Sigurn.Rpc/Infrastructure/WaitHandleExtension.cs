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

        TaskCompletionSource tcs = new (TaskCreationOptions.RunContinuationsAsynchronously);

        void handler(object? sender, EventArgs args) => tcs.TrySetResult();

        void Subscribe()
        {
            switch (state)
            {
                case ChannelState.Opening: channel.Opening += handler; break;
                case ChannelState.Opened: channel.Opened += handler; break;
                case ChannelState.Closing: channel.Closing += handler; break;
                case ChannelState.Closed: channel.Closed += handler; break;
                case ChannelState.Faulted: channel.Faulted += handler; break;
                default: throw new ArgumentOutOfRangeException($"Unsupported state {state}");
            }
        }

        void Unsubscribe()
        {
            switch (state)
            {
                case ChannelState.Opening: channel.Opening -= handler; break;
                case ChannelState.Opened: channel.Opened -= handler; break;
                case ChannelState.Closing: channel.Closing -= handler; break;
                case ChannelState.Closed: channel.Closed -= handler; break;
                case ChannelState.Faulted: channel.Faulted -= handler; break;
            }
        }

        using var ctr = cancellationToken.Register(static s => ((TaskCompletionSource)s!).TrySetCanceled(), tcs);

        Subscribe();
        try
        {
            // Re-check after subscribing to close the race where the channel reaches
            // the target state between the initial check and the subscription.
            if (channel.State == state) tcs.TrySetResult();
            await tcs.Task;
        }
        finally
        {
            Unsubscribe();
        }
    }
}