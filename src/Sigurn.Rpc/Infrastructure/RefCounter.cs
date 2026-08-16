namespace Sigurn.Rpc.Infrastructure;

// Note: this type deliberately does NOT implement IAsyncDisposable. Callers pick the release
// semantics explicitly — Release/Dispose for the best-effort synchronous paths (teardown, finalizers)
// and ReleaseAsync/DisposeAsync where the caller awaits the disposal. Implementing the interface would
// let a generic `is IAsyncDisposable` probe silently move teardown onto the awaiting path.
class RefCounter<T> : IDisposable where T : class
{
    private readonly Action<T> _disposed;
    private readonly Func<T, ValueTask>? _disposedAsync;
    private readonly T _value;

    private int _counter = 0;
    private volatile int _isDisposed = 0;

    public RefCounter(T value, Action<T> disposed)
        : this(value, disposed, null)
    {
    }

    public RefCounter(T value, Action<T> disposed, Func<T, ValueTask>? disposedAsync)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(disposed);

        _value = value;
        _disposed = disposed;
        _disposedAsync = disposedAsync;
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0) return;
        _disposed(_value);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0) return;

        if (_disposedAsync is not null)
            await _disposedAsync(_value).ConfigureAwait(false);
        else
            _disposed(_value);
    }

    public T Value
    {
        get
        {
            CheckDisposed();

            return _value;
        }
    }

    public int AddRef()
    {
        CheckDisposed();

        return Interlocked.Increment(ref _counter);
    }

    public int Release()
    {
        CheckDisposed();

        var count = Interlocked.Decrement(ref _counter);
        if (count == 0) Dispose();
        return count;
    }

    public async ValueTask<int> ReleaseAsync()
    {
        CheckDisposed();

        var count = Interlocked.Decrement(ref _counter);
        if (count == 0) await DisposeAsync().ConfigureAwait(false);
        return count;
    }

    private void CheckDisposed()
    {
        if (_isDisposed != 0)
            throw new ObjectDisposedException(null);
    }

    public override bool Equals(object? obj)
    {
        return _value.Equals(obj);
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }
}