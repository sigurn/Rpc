using Sigurn.Rpc;

namespace Sigurn.Rpc.Infrastructure;

abstract class BaseAcceptor : IAsyncChannelAcceptor
{
    private volatile bool _isAccepting = false;
    private volatile bool _isDisposed = false;

    public bool IsAccepting => Interlocked.Exchange(ref _isAccepting, _isAccepting);

    public async Task<IChannel> AcceptAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isDisposed, _isDisposed))
            throw new InvalidOperationException("The object is already disposed");

        if (Interlocked.Exchange(ref _isAccepting, true))
            throw new InvalidOperationException("The object is already accepting connections.");

        try
        {
            return await Accept(cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref _isAccepting, false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, true))
            return;

        await InternalDispose();
    }

    protected abstract Task<IChannel> Accept(CancellationToken cancellationToken);

    protected abstract Task InternalDispose();
}
