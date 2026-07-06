using System.Runtime.CompilerServices;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Tests;

// A transient service used to observe that proxies obtained inside the session-initialize hook are
// released when the hook completes and are never re-requested by session restore. Registered as
// ShareWithin.None so every GetInstance yields a fresh, disposable server-side instance and Live
// tracks how many are currently alive. Hand-written Adapter/Proxy mirror what the generator emits.
public interface ICounterService
{
    int Ping();
}

public sealed class CounterService : ICounterService, IDisposable
{
    public static int Live;

    public CounterService() => Interlocked.Increment(ref Live);

    public int Ping() => 42;

    public void Dispose() => Interlocked.Decrement(ref Live);
}

sealed class CounterServiceAdapter : InterfaceAdapter
{
    [ModuleInitializer]
    public static void Init() => RegisterAdapter<ICounterService>(x => new CounterServiceAdapter(x));

    private readonly ICounterService _instance;

    public CounterServiceAdapter(ICounterService instance)
        : base(typeof(ICounterService), instance)
    {
        _instance = instance;
    }

    public override async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        if (methodId == 1)
        {
            var res = _instance.Ping();
            return (Result: await ToBytesAsync(res, cancellationToken), Args: null);
        }

        return (Result: null, Args: null);
    }
}

sealed class CounterServiceProxy : InterfaceProxy, ICounterService
{
    [ModuleInitializer]
    public static void Init() => RegisterProxy<ICounterService>(x => new CounterServiceProxy(x));

    public CounterServiceProxy(Guid instanceId)
        : base(instanceId)
    {
    }

    public int Ping()
    {
        var (res, _) = InvokeMethod(1, [], false);
        if (res is null)
            throw new Exception("Server did not return result value");

        return FromBytes<int>(res);
    }
}
