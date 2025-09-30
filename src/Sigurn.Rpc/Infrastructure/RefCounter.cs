namespace Sigurn.Rpc.Infrastructure;

// struct ObjectRef<T> : IDisposable where T : class
// {
//     private readonly T _value;
//     private readonly Action _released;
//     private int _isDisposed = 0;

//     public ObjectRef(T value, Action released)
//     {
//         _value = value;
//         _released = released;
//     }

//     public void Dispose()
//     {
//         if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
//         _released();
//     }

//     public T Value
//     {
//         get
//         {
//             CheckDisposed();
//             return _value;
//         }
//     }

//     private void CheckDisposed()
//     {
//         if (Interlocked.Exchange(ref _isDisposed, _isDisposed) != 0)
//             throw new ObjectDisposedException(null);
//     }

//     public override int GetHashCode()
//     {
//         return _value.GetHashCode();
//     }

//     public override bool Equals(object? obj)
//     {
//         if (obj is null) return false;
//         return Equals(_value, obj);
//     }
// }

class RefCounter<T> : IDisposable where T : class
{
    private readonly Action<T> _disposed;
    private readonly T _value;

    private int _counter = 0;
    private int _isDisposed = 0;

    public RefCounter(T value, Action<T> disposed)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(disposed);

        _value = value;
        _disposed = disposed;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
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

    private void CheckDisposed()
    {
        if (Interlocked.Exchange(ref _isDisposed, _isDisposed) != 0)
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