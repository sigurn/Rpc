namespace Sigurn.Rpc.Infrastructure;

public abstract class HostBaseAsync : IAsyncChannelHost, IAsyncDisposable
{
    private readonly Lock _lock = new ();
    private volatile bool _isOpened = false;
    private volatile bool _isAccepting = false;
    private volatile bool _isDisposed = false;

    protected HostBaseAsync()
    {
    }

    private Task? _disposingTask;
    public async ValueTask DisposeAsync()
    {
        Task task;
        lock(_lock)
        {
            if (_isDisposed) return;

            if (_disposingTask is null)
                _disposingTask = Dispose(CancellationToken.None);

            task = _disposingTask;
        }

        try
        {
            await task;
        }
        finally
        {
            lock(_lock)
            {
                if (!_isDisposed && _disposingTask is not null)
                {
                    _disposingTask = null;
                    _isDisposed = true;
                }
            }                        
        }
    }

    public bool IsOpened
    {
        get
        {
            lock(_lock)
                return _isOpened;
        }
    }

    public bool IsAccepting
    {
        get
        {
            lock(_lock)
                return _isAccepting;
        }        
    }

    private Task? _openingTask;
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock(_lock)
        {
            if (_isDisposed)
                throw new InvalidOperationException("Host is already disposed");

            if (_isOpened) return;

            if (_openingTask is null)
                _openingTask = Open(cancellationToken);

            task = _openingTask;
        }

        try
        {
            await task;            
        }
        finally
        {
            lock(_lock)
            {
                if (!_isOpened && _openingTask is not null)
                {
                    _openingTask = null;
                    _isOpened = true;
                }
            }            
        }
    }

    public async Task<IChannel> AcceptAsync(CancellationToken cancellationToken)
    {
        lock(_lock)
        {
            if (_isDisposed)
                throw new InvalidOperationException("Host is already disposed");

            if (!_isOpened)
                throw new InvalidOperationException("Host is closed. Cannot accept connections on the closed host.");

            if (_isAccepting)
                throw new InvalidOperationException("Host is already accepting. Cannot start concurrent accepting");

            _isAccepting = true;
        }

        try
        {
            return await Accept(cancellationToken);
        }
        finally
        {
            lock(_lock)
                _isAccepting = false;
        }
    }

    private Task? _closingTask;
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        Task task;
        lock(_lock)
        {
            if (_isDisposed)
                throw new InvalidOperationException("Host is already disposed");

            if (!_isOpened) return;

            if (_closingTask is null)
                _closingTask = Close(cancellationToken);
                
            task = _closingTask;
        }

        try
        {
            await task;            
        }
        finally
        {
            lock(_lock)
            {
                if (_isOpened && _closingTask is not null)
                {
                    _closingTask = null;
                    _isOpened = false;
                }
            }            
        }
    }

    protected abstract Task Dispose(CancellationToken cancellationToken);

    protected abstract Task Open (CancellationToken cancellationToken);

    protected abstract Task<IChannel> Accept(CancellationToken cancellationToken);

    protected abstract Task Close(CancellationToken cancellationToken);
}