using System.Runtime.CompilerServices;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Tests;

public interface ITestNotification
{
    void OnNotification(string data);
}

class TestNotificationAdapter : InterfaceAdapter
{
    [ModuleInitializer]
    public static void MethodInit()
    {
        RegisterAdapter<ITestNotification>(x => new TestNotificationAdapter(x));
    }

    private readonly ITestNotification _instance;
    public TestNotificationAdapter(ITestNotification instance)
        : base(typeof(ITestNotification), instance)
    {
        _instance = instance;
    }

    public override async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        if (methodId == 1)
        {
            if (args is null || args.Count != 1)
                throw new ArgumentException("Invalid number of arguments");

            var arg0 = await FromBytesAsync<string>(args[0], cancellationToken);
            if (arg0 is null)
                throw new ArgumentException("data");

            _instance.OnNotification(arg0);
        }

        return (Result: null, Args: null);
    }
}

class TestNotificationProxy : InterfaceProxy, ITestNotification
{
    [ModuleInitializer]
    public static void MethodInit()
    {
        RegisterProxy<ITestNotification>(x => new TestNotificationProxy(x));
    }

    public TestNotificationProxy(Guid instanceId)
        : base(instanceId)
    {
        
    }

    public void OnNotification(string data)
    {
        InvokeMethod(1, [ToBytes(data)], false);
    }
}

public interface ITestService
{
    void Method1();

    int Add(int a, int b);

    void GetString(out string text);

    bool ModifyString(ref string text);

    Task Method1Async(CancellationToken cancellationToken);

    Task<int> AddAsync(int a, int b, CancellationToken cancellationToken);

    void Subscribe(ITestNotification? handler);

    void NotifySubscribers(string data);

    void Unsubscribe(ITestNotification handler);

    int Property1 { get; set; }

    event EventHandler TestEvent;
}

class TestServiceAdapter : InterfaceAdapter
{
    [ModuleInitializer]
    public static void MethodInit()
    {
        RegisterAdapter<ITestService>(x => new TestServiceAdapter(x));
    }

    private readonly ITestService _instance;

    public TestServiceAdapter(ITestService instance)
        : base(typeof(ITestService), instance)
    {
        _instance = instance;
    }

    public override async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        if (methodId == 1)
        {
            _instance.Method1();
        }
        else if (methodId == 2)
        {
            if (args is null || args.Count != 2)
                throw new ArgumentException("Invalid number of arguments");

            var arg0 = await FromBytesAsync<int>(args[0], cancellationToken);
            var arg1 = await FromBytesAsync<int>(args[1], cancellationToken);
            var res = _instance.Add(arg0, arg1);
            return (Result: await ToBytesAsync(res, cancellationToken), Args: null);
        }
        else if (methodId == 3)
        {
            string text;
            _instance.GetString(out text);
            return (Result: null, Args: [await ToBytesAsync(text, cancellationToken)]);
        }
        else if (methodId == 4)
        {
            if (args is null || args.Count != 1)
                throw new ArgumentException("Invalid number of arguments");

            string text = await FromBytesAsync<string>(args[0], cancellationToken) ?? throw new ArgumentNullException("text");
            var res = _instance.ModifyString(ref text);
            return (Result: await ToBytesAsync(res, cancellationToken), Args: [await ToBytesAsync(text, cancellationToken)]);
        }
        else if (methodId == 5)
        {
            if (args is null || args.Count != 0)
                throw new ArgumentException("Invalid number of arguments");

            await _instance.Method1Async(cancellationToken);
            return (Result: null, Args: null);
        }
        else if (methodId == 6)
        {
            if (args is null || args.Count != 2)
                throw new ArgumentException("Invalid number of arguments");

            var a = await FromBytesAsync<int>(args[0], cancellationToken);
            var b = await FromBytesAsync<int>(args[1], cancellationToken);
            var res = await _instance.AddAsync(a, b, cancellationToken);
            return (Result: await ToBytesAsync(res, cancellationToken), Args: null);
        }
        else if (methodId == 7)
        {
            if (args is null || args.Count != 1)
                throw new ArgumentException("Invalid number of arguments");

            var handler = await FromBytesAsync<ITestNotification>(args[0], cancellationToken);
            _instance.Subscribe(handler);
        }
        else if (methodId == 8)
        {
            if (args is null || args.Count != 1)
                throw new ArgumentException("Invalid number of arguments");

            var data = await FromBytesAsync<string>(args[0], cancellationToken);
            if (data is null)
                throw new ArgumentNullException("data");

            _instance.NotifySubscribers(data);
        }
        else if (methodId == 9)
        {
            if (args is null || args.Count != 1)
                throw new ArgumentException("Invalid number of arguments");

            var handler = await FromBytesAsync<ITestNotification>(args[0], cancellationToken);
            if (handler is null)
                throw new NullReferenceException("Handler cannot be null");
            _instance.Unsubscribe(handler);
        }

        return (Result: null, Args: null);
    }

    public override async Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken)
    {
        if (propertyId == 1)
        {
            return await ToBytesAsync(_instance.Property1, cancellationToken);
        }

        throw new Exception("Unknown property");
    }

    public override async Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken)
    {
        if (propertyId == 1)
        {
            ArgumentNullException.ThrowIfNull(value);
            _instance.Property1 = await FromBytesAsync<int>(value, cancellationToken);
            return;
        }

        throw new Exception("Unknown property");
    }

    public override Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        try
        {
            if (eventId == 1)
            {
                _instance.TestEvent += OnTestEvent;
                return Task.CompletedTask;
            }

            throw new ArgumentException("Unknown event", nameof(eventId));
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    public override Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        try
        {
            if (eventId == 1)
            {
                _instance.TestEvent -= OnTestEvent;
                return Task.CompletedTask;
            }

            throw new ArgumentException("Unknown event", nameof(eventId));
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;

        (_instance as IDisposable)?.Dispose();
    }

    private void OnTestEvent(object? sender, EventArgs args)
    {
        SendEvent(1, ToBytes(args));
    }
}

class TestServiceProxy : InterfaceProxy, ITestService
{
    [ModuleInitializer]
    public static void MethodInit()
    {
        RegisterProxy<ITestService>(x => new TestServiceProxy(x));
    }

    public TestServiceProxy(Guid instanceId)
        : base(instanceId)
    {
    }

    public void Method1()
    {
        InvokeMethod(1, [], false);
    }

    int ITestService.Add(int a, int b)
    {
        IReadOnlyList<byte[]> args =
        [
            ToBytes(a),
            ToBytes(b)
        ];

        var (res, outArgs) = InvokeMethod(2, args, false);
        if (res is null)
            throw new Exception("Server did not return result value");

        return FromBytes<int>(res);
    }

    void ITestService.GetString(out string text)
    {
        var res = InvokeMethod(3, null, false);
        if (res.Args is null)
            throw new Exception("Server has not returned output arguments");

        text = FromBytes<string>(res.Args[0]) ?? string.Empty;
    }

    bool ITestService.ModifyString(ref string text)
    {
        var res = InvokeMethod(4, [ToBytes(text)], false);

        if (res.Result is null)
            throw new Exception("Server haven't returned result value");
        if (res.Args is null)
            throw new Exception("Server has not returned output arguments");

        text = FromBytes<string>(res.Args[0]) ?? string.Empty;
        return FromBytes<bool>(res.Result);
    }

    async Task ITestService.Method1Async(CancellationToken cancellationToken)
    {
        await InvokeMethodAsync(5, [], false, cancellationToken);
    }

    async Task<int> ITestService.AddAsync(int a, int b, CancellationToken cancellationToken)
    {
        var res = await InvokeMethodAsync(6, [await ToBytesAsync(a, cancellationToken), await ToBytesAsync(b, cancellationToken)], false, cancellationToken);
        if (res.Result is null)
            throw new Exception("Server returned void result");
        return await FromBytesAsync<int>(res.Result, cancellationToken);
    }

    void ITestService.Subscribe(ITestNotification? handler)
    {
        InvokeMethod(7, [ToBytes(handler)], false);
    }

    void ITestService.NotifySubscribers(string data)
    {
        InvokeMethod(8, [ToBytes(data)], false);
    }

    void ITestService.Unsubscribe(ITestNotification handler)
    {
        InvokeMethod(9, [ToBytes(handler)], false);
    }

    int ITestService.Property1
    {
        get => GetProperty<int>(1);
        set => SetProperty(1, value);
    }

    private EventHandler? _testEvent;
    event EventHandler ITestService.TestEvent
    {
        add
        {
            _testEvent += value;
            AttachEventHandler(1);
        }

        remove
        {
            _testEvent -= value;
            DetachEventHandler(1);
        }
    }

    protected override void OnEvent(int eventId, IReadOnlyList<byte[]> args)
    {
        if (eventId == 1)
        {
            _testEvent?.Invoke(this, EventArgs.Empty);
        }
    }
}

class TestService : ITestService, IDisposable
{
    private readonly List<string> _log;
    private readonly ManualResetEvent? _destroyEvent;
    public TestService(List<string> log, ManualResetEvent? destroyEvent = null)
    {
        _log = log;
        _destroyEvent = destroyEvent;
        _log.AddWithLock("Created");
    }

    public void Dispose()
    {
        _log.AddWithLock("Disposed");
        _destroyEvent?.Set();
    }

    void ITestService.Method1()
    {
        _log.AddWithLock("Method1");
    }

    int ITestService.Add(int a, int b)
    {
        _log.AddWithLock($"Add {a}, {b}");
        return a + b;
    }

    void ITestService.GetString(out string text)
    {
        text = "Test string from service";
    }

    bool ITestService.ModifyString(ref string text)
    {
        const string addition = "Addition from service.";

        if (text is null) return false;

        if (text == string.Empty)
        {
            text = addition;
            return true;
        }

        text = text.TrimEnd();
        if (text.Last() != '.')
            text += '.';
        text += " ";
        text += addition;
        return true;
    }

    Task ITestService.Method1Async(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    Task<int> ITestService.AddAsync(int a, int b, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<int>(a + b);
    }

    private readonly List<ITestNotification> _subscriptions = new();
    void ITestService.Subscribe(ITestNotification? handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_subscriptions)
            _subscriptions.Add(handler);
    }

    void ITestService.NotifySubscribers(string data)
    {
        List<ITestNotification> subscriptions;
        lock (_subscriptions)
            subscriptions = _subscriptions.ToList();

        foreach (var s in subscriptions)
            s.OnNotification(data);
    }

    void ITestService.Unsubscribe(ITestNotification handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_subscriptions)
            _subscriptions.Remove(handler);
    }

    private int _property1;
    int ITestService.Property1
    {
        get
        {
            _log.AddWithLock("GetProperty1");
            return _property1;
        }

        set
        {
            _property1 = value;
            _log.AddWithLock($"SetProperty1 {value}");
        }
    }

    public void RaiseTestEvent()
    {
        _testEvent?.Invoke(this, EventArgs.Empty);
    }

    private EventHandler? _testEvent;
    event EventHandler ITestService.TestEvent
    {
        add
        {
            _testEvent += value;
        }

        remove
        {
            _testEvent -= value;
        }
    }
}
