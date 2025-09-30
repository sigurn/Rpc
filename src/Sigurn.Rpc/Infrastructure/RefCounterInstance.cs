namespace Sigurn.Rpc.Infrastructure;

class RefCounterInstance<T> : IDisposable where T : class
{
    public RefCounterInstance(object instance)
    {
        _instance = instance;
        Counter = 1;
    }

    public void Dispose()
    {
        if (_instance is IDisposable d)
            d.Dispose();

        _instance = null;
        Counter = 0;
    }

    private object? _instance;
    public object Instance => _instance ?? throw new InvalidOperationException("Object instance is not available");
    public int Counter { get; set; }
}