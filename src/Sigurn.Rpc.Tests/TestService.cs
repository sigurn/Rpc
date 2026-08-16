using System.Runtime.CompilerServices;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Tests;

[RemoteInterface]
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
    Task MethodThrowAsync(CancellationToken cancellationToken);
    ITestNotification GetSubService();
}

class TestServiceAdapter : InterfaceAdapter
{
    [ModuleInitializer]
    public static void MethodInit()
    {
        RegisterAdapter<ITestService>(x => new TestServiceAdapter(x));
    }

    // Member names mirror what the source generator emits for a [RemoteInterface] type.
    private const string @__method_1 = "Method1()";
    private const string @__method_2 = "Add(int, int)";
    private const string @__method_3 = "GetString(out string)";
    private const string @__method_4 = "ModifyString(ref string)";
    private const string @__method_5 = "Method1Async(System.Threading.CancellationToken)";
    private const string @__method_6 = "AddAsync(int, int, System.Threading.CancellationToken)";
    private const string @__method_7 = "Subscribe(Sigurn.Rpc.Tests.ITestNotification?)";
    private const string @__method_8 = "NotifySubscribers(string)";
    private const string @__method_9 = "Unsubscribe(Sigurn.Rpc.Tests.ITestNotification)";
    private const string @__method_10 = "MethodThrowAsync(System.Threading.CancellationToken)";
    private const string @__method_11 = "GetSubService()";
    private const string @__property_1 = "Property1";
    private const string @__event_1 = "TestEvent";

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
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_1, 1);
            try
            {
                _instance.Method1();
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_1, 1);
            }
        }
        else if (methodId == 2)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_2, 2);
            try
            {
                if (args is null || args.Count != 2)
                    throw new ArgumentException("Invalid number of arguments");

                var arg0 = await FromBytesAsync<int>(args[0], cancellationToken);
                var arg1 = await FromBytesAsync<int>(args[1], cancellationToken);
                var res = _instance.Add(arg0, arg1);
                return (Result: await ToBytesAsync(res, cancellationToken), Args: null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_2, 2))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_2, 2);
            }
        }
        else if (methodId == 3)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_3, 3);
            try
            {
                string text;
                _instance.GetString(out text);
                return (Result: null, Args: [await ToBytesAsync(text, cancellationToken)]);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_3, 3))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_3, 3);
            }
        }
        else if (methodId == 4)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_4, 4);
            try
            {
                if (args is null || args.Count != 1)
                    throw new ArgumentException("Invalid number of arguments");

                string text = await FromBytesAsync<string>(args[0], cancellationToken) ?? throw new ArgumentNullException("text");
                var res = _instance.ModifyString(ref text);
                return (Result: await ToBytesAsync(res, cancellationToken), Args: [await ToBytesAsync(text, cancellationToken)]);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_4, 4))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_4, 4);
            }
        }
        else if (methodId == 5)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_5, 5);
            try
            {
                if (args is null || args.Count != 0)
                    throw new ArgumentException("Invalid number of arguments");

                await _instance.Method1Async(cancellationToken);
                return (Result: null, Args: null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_5, 5))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_5, 5);
            }
        }
        else if (methodId == 6)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_6, 6);
            try
            {
                if (args is null || args.Count != 2)
                    throw new ArgumentException("Invalid number of arguments");

                var a = await FromBytesAsync<int>(args[0], cancellationToken);
                var b = await FromBytesAsync<int>(args[1], cancellationToken);
                var res = await _instance.AddAsync(a, b, cancellationToken);
                return (Result: await ToBytesAsync(res, cancellationToken), Args: null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_6, 6))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_6, 6);
            }
        }
        else if (methodId == 7)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_7, 7);
            try
            {
                if (args is null || args.Count != 1)
                    throw new ArgumentException("Invalid number of arguments");

                var handler = await FromBytesAsync<ITestNotification>(args[0], cancellationToken);
                _instance.Subscribe(handler);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_7, 7))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_7, 7);
            }
        }
        else if (methodId == 8)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_8, 8);
            try
            {
                if (args is null || args.Count != 1)
                    throw new ArgumentException("Invalid number of arguments");

                var data = await FromBytesAsync<string>(args[0], cancellationToken);
                if (data is null)
                    throw new ArgumentNullException("data");

                _instance.NotifySubscribers(data);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_8, 8))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_8, 8);
            }
        }
        else if (methodId == 9)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_9, 9);
            try
            {
                if (args is null || args.Count != 1)
                    throw new ArgumentException("Invalid number of arguments");

                var handler = await FromBytesAsync<ITestNotification>(args[0], cancellationToken);
                if (handler is null)
                    throw new NullReferenceException("Handler cannot be null");
                _instance.Unsubscribe(handler);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_9, 9))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_9, 9);
            }
        }
        else if (methodId == 10)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_10, 10);
            try
            {
                if (args is null || args.Count != 0)
                    throw new ArgumentException("Invalid number of arguments");

                await _instance.MethodThrowAsync(cancellationToken);
                return (Result: null, Args: null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_10, 10))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_10, 10);
            }
        }
        else if (methodId == 11)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_11, 11);
            try
            {
                var result = _instance.GetSubService();
                return (Result: await ToBytesAsync<ITestNotification>(result, cancellationToken), Args: null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_11, 11))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_11, 11);
            }
        }

        return (Result: null, Args: null);
    }

    public override async Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken)
    {
        if (propertyId == 1)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.PropertyGet, @__property_1, 1);
            try
            {
                return await ToBytesAsync(_instance.Property1, cancellationToken);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.PropertyGet, @__property_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.PropertyGet, @__property_1, 1);
            }
        }

        throw new Exception("Unknown property");
    }

    public override async Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken)
    {
        if (propertyId == 1)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.PropertySet, @__property_1, 1);
            try
            {
                ArgumentNullException.ThrowIfNull(value);
                _instance.Property1 = await FromBytesAsync<int>(value, cancellationToken);
                return;
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.PropertySet, @__property_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.PropertySet, @__property_1, 1);
            }
        }

        throw new Exception("Unknown property");
    }

    public override Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        try
        {
            if (eventId == 1)
            {
                if (IsTraceEnabled) TraceEnter(RpcTraceOperation.EventAttach, @__event_1, 1);
                try
                {
                    _instance.TestEvent += OnTestEvent;
                    return Task.CompletedTask;
                }
                catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.EventAttach, @__event_1, 1))
                {
                    throw;
                }
                finally
                {
                    if (IsTraceEnabled) TraceExit(RpcTraceOperation.EventAttach, @__event_1, 1);
                }
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
                if (IsTraceEnabled) TraceEnter(RpcTraceOperation.EventDetach, @__event_1, 1);
                try
                {
                    _instance.TestEvent -= OnTestEvent;
                    return Task.CompletedTask;
                }
                catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.EventDetach, @__event_1, 1))
                {
                    throw;
                }
                finally
                {
                    if (IsTraceEnabled) TraceExit(RpcTraceOperation.EventDetach, @__event_1, 1);
                }
            }

            throw new ArgumentException("Unknown event", nameof(eventId));
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private void OnTestEvent(object? sender, EventArgs args)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.EventRaise, @__event_1, 1);
        try
        {
            SendEvent(1, ToBytes(args));
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.EventRaise, @__event_1, 1))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.EventRaise, @__event_1, 1);
        }
    }
}

class TestServiceProxy : InterfaceProxy, ITestService
{
    [ModuleInitializer]
    public static void MethodInit()
    {
        RegisterProxy<ITestService>(x => new TestServiceProxy(x));
    }

    // Member names mirror what the source generator emits for a [RemoteInterface] type.
    private const string @__method_1 = "Method1()";
    private const string @__method_2 = "Add(int, int)";
    private const string @__method_3 = "GetString(out string)";
    private const string @__method_4 = "ModifyString(ref string)";
    private const string @__method_5 = "Method1Async(System.Threading.CancellationToken)";
    private const string @__method_6 = "AddAsync(int, int, System.Threading.CancellationToken)";
    private const string @__method_7 = "Subscribe(Sigurn.Rpc.Tests.ITestNotification?)";
    private const string @__method_8 = "NotifySubscribers(string)";
    private const string @__method_9 = "Unsubscribe(Sigurn.Rpc.Tests.ITestNotification)";
    private const string @__method_10 = "MethodThrowAsync(System.Threading.CancellationToken)";
    private const string @__method_11 = "GetSubService()";
    private const string @__property_1 = "Property1";
    private const string @__event_1 = "TestEvent";

    public TestServiceProxy(Guid instanceId)
        : base(instanceId)
    {
    }

    public void Method1()
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_1, 1);
        try
        {
            InvokeMethod(1, [], false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_1, 1))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_1, 1);
        }
    }

    int ITestService.Add(int a, int b)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_2, 2);
        try
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
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_2, 2))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_2, 2);
        }
    }

    void ITestService.GetString(out string text)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_3, 3);
        try
        {
            var res = InvokeMethod(3, null, false);
            if (res.Args is null)
                throw new Exception("Server has not returned output arguments");

            text = FromBytes<string>(res.Args[0]) ?? string.Empty;
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_3, 3))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_3, 3);
        }
    }

    bool ITestService.ModifyString(ref string text)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_4, 4);
        try
        {
            var res = InvokeMethod(4, [ToBytes(text)], false);

            if (res.Result is null)
                throw new Exception("Server haven't returned result value");
            if (res.Args is null)
                throw new Exception("Server has not returned output arguments");

            text = FromBytes<string>(res.Args[0]) ?? string.Empty;
            return FromBytes<bool>(res.Result);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_4, 4))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_4, 4);
        }
    }

    async Task ITestService.Method1Async(CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_5, 5);
        try
        {
            await InvokeMethodAsync(5, [], false, cancellationToken);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_5, 5))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_5, 5);
        }
    }

    async Task<int> ITestService.AddAsync(int a, int b, CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_6, 6);
        try
        {
            var res = await InvokeMethodAsync(6, [await ToBytesAsync(a, cancellationToken), await ToBytesAsync(b, cancellationToken)], false, cancellationToken);
            if (res.Result is null)
                throw new Exception("Server returned void result");
            return await FromBytesAsync<int>(res.Result, cancellationToken);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_6, 6))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_6, 6);
        }
    }

    void ITestService.Subscribe(ITestNotification? handler)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_7, 7);
        try
        {
            InvokeMethod(7, [ToBytes(handler)], false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_7, 7))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_7, 7);
        }
    }

    void ITestService.NotifySubscribers(string data)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_8, 8);
        try
        {
            InvokeMethod(8, [ToBytes(data)], false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_8, 8))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_8, 8);
        }
    }

    void ITestService.Unsubscribe(ITestNotification handler)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_9, 9);
        try
        {
            InvokeMethod(9, [ToBytes(handler)], false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_9, 9))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_9, 9);
        }
    }

    async Task ITestService.MethodThrowAsync(CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_10, 10);
        try
        {
            await InvokeMethodAsync(10, [], false, cancellationToken);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_10, 10))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_10, 10);
        }
    }

    ITestNotification ITestService.GetSubService()
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOperation.MethodCall, @__method_11, 11);
        try
        {
            var (res, _) = InvokeMethod(11, [], false);
            return FromBytes<ITestNotification>(res) ?? throw new InvalidOperationException("Server returned null for GetSubService");
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.MethodCall, @__method_11, 11))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOperation.MethodCall, @__method_11, 11);
        }
    }

    int ITestService.Property1
    {
        get
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.PropertyGet, @__property_1, 1);
            try
            {
                return GetProperty<int>(1);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.PropertyGet, @__property_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.PropertyGet, @__property_1, 1);
            }
        }

        set
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.PropertySet, @__property_1, 1);
            try
            {
                SetProperty(1, value);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.PropertySet, @__property_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.PropertySet, @__property_1, 1);
            }
        }
    }

    private EventHandler? _testEvent;
    event EventHandler ITestService.TestEvent
    {
        add
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.EventAttach, @__event_1, 1);
            try
            {
                _testEvent += value;
                AttachEventHandler(1);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.EventAttach, @__event_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.EventAttach, @__event_1, 1);
            }
        }

        remove
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.EventDetach, @__event_1, 1);
            try
            {
                _testEvent -= value;
                DetachEventHandler(1);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.EventDetach, @__event_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.EventDetach, @__event_1, 1);
            }
        }
    }

    protected override void OnEvent(int eventId, IReadOnlyList<byte[]> args)
    {
        if (eventId == 1)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOperation.EventRaise, @__event_1, 1);
            try
            {
                _testEvent?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOperation.EventRaise, @__event_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOperation.EventRaise, @__event_1, 1);
            }
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

    Task ITestService.MethodThrowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromException(new NotImplementedException());
    }

    private Func<ITestNotification>? _subServiceFactory;
    public void SetSubServiceFactory(Func<ITestNotification> factory) => _subServiceFactory = factory;

    ITestNotification ITestService.GetSubService()
    {
        if (_subServiceFactory is null) throw new InvalidOperationException("SubService factory not configured");
        return _subServiceFactory();
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
