namespace Sigurn.Rpc;

class Disposable : IDisposable
{
    public static IDisposable Create(Action dispose)
    {
        return new Disposable(dispose);
    }

    public static IDisposable Create<T>(Action<T> dispose, T instance)
    {
        return new Disposable(() => dispose(instance));
    }

    private int _isDisposed = 0;
    private readonly Action _dispose;

    private Disposable(Action dispose)
    {
        _dispose = dispose;
    }

    void IDisposable.Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
        _dispose();
    }
}