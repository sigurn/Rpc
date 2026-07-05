using Microsoft.Extensions.Logging;

namespace Sigurn.Rpc.Infrastructure;

abstract class BaseAcceptor : IAsyncChannelAcceptor
{
    private static readonly ILogger<BaseAcceptor> _logger = RpcLogging.CreateLogger<BaseAcceptor>();

    private readonly Func<IChannel, IChannel>? _channelFactory = null;

    private volatile bool _isAccepting = false;
    private volatile bool _isDisposed = false;
    private volatile Func<IChannel, CancellationToken, Task<bool>>? _validator = null;

    protected BaseAcceptor(Func<IChannel, IChannel>? channelFactory = null)
    {
        _channelFactory = channelFactory;
    }

    public bool IsAccepting => Interlocked.Exchange(ref _isAccepting, _isAccepting);

    public async Task<IChannel?> AcceptAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isDisposed, _isDisposed))
            throw new InvalidOperationException("The object is already disposed");

        if (Interlocked.Exchange(ref _isAccepting, true))
            throw new InvalidOperationException("The object is already accepting connections.");

        try
        {
            do
            {
                var channel = await Accept(cancellationToken).ConfigureAwait(false);
                if (channel is null) return null;

                var validator = Interlocked.Exchange(ref _validator, _validator);
                if (validator is not null && !await validator(channel, cancellationToken).ConfigureAwait(false))
                {
                    if (channel is IAsyncDisposable ad)
                        await ad.DisposeAsync().ConfigureAwait(false);
                    else if (channel is IDisposable d)
                        d.Dispose();

                    continue;
                }

                return _channelFactory != null ?
                    _channelFactory(channel) : channel;
            } while(!cancellationToken.IsCancellationRequested);

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Accept loop failed");
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _isAccepting, false);
        }
    }

    public Func<IChannel, CancellationToken, Task<bool>>? SetChannelValidator(Func<IChannel, CancellationToken, Task<bool>>? validator)
    {
        return Interlocked.Exchange(ref _validator, validator);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, true))
            return;

        await InternalDispose().ConfigureAwait(false);
    }

    protected abstract Task<IChannel?> Accept(CancellationToken cancellationToken);

    protected abstract Task InternalDispose();
}
