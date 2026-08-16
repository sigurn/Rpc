//HintName: ITestService.g.cs
#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices;

using Sigurn.Rpc;

using RpcTraceOp = Sigurn.Rpc.Infrastructure.RpcTraceOperation;

namespace MyCode.Rpc.Infrastructure;

sealed class ITestService_Adapter : Sigurn.Rpc.Infrastructure.InterfaceAdapter
{
    [ModuleInitializer]
    internal static void Initializer()
    {
        RegisterAdapter<MyCode.ITestService>(x => new ITestService_Adapter(x));
    }

    private readonly MyCode.ITestService _instance;

    public ITestService_Adapter(MyCode.ITestService instance)
        : base(typeof(MyCode.ITestService), instance)
    {
        _instance = instance;
    }

    private const string @__rpcInterfaceName = "MyCode.ITestService";
    protected override string RpcInterfaceName => @__rpcInterfaceName;

    private const string @__property_0 = "Prop1";
    private const string @__property_1 = "Prop2";
    private const string @__property_2 = "Prop3";
    private const string @__property_3 = "Prop4";
    private const string @__property_4 = "Prop5";
    private const string @__method_0 = "Method1()";
    private const string @__method_1 = "Method2()";
    private const string @__method_2 = "Method3(string?)";
    private const string @__method_3 = "Method4(string)";
    private const string @__method_4 = "Method5(out string)";
    private const string @__method_5 = "Method6(ref string)";
    private const string @__method_6 = "Method7(ref int, out string[])";
    private const string @__method_7 = "Method8()";
    private const string @__method_8 = "Method9()";
    private const string @__method_9 = "Method10()";
    private const string @__method_10 = "Method11(System.Threading.CancellationToken)";
    private const string @__method_11 = "Method12(bool, string, System.Threading.CancellationToken)";
    private const string @__method_12 = "Method13(string, string, System.Threading.CancellationToken)";
    private const string @__event_0 = "Event1";
    private const string @__event_1 = "Event2";

    public override async Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken)
    {
        CheckPermissions([ MyCode.Permission.Perm1 ]);

        if (propertyId == 0)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertyGet, @__property_0, 0);
            try
            {
                CheckAuthenticated();
                return await ToBytesAsync<string?>(_instance.Prop1, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertyGet, @__property_0, 0))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertyGet, @__property_0, 0);
            }
        }
        else if (propertyId == 1)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertyGet, @__property_1, 1);
            try
            {
                return await ToBytesAsync<int>(_instance.Prop2, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertyGet, @__property_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertyGet, @__property_1, 1);
            }
        }
        else if (propertyId == 3)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertyGet, @__property_3, 3);
            try
            {
                return await ToBytesAsync<System.Collections.Generic.IList<System.Guid>>(_instance.Prop4, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Property value cannot be null");
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertyGet, @__property_3, 3))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertyGet, @__property_3, 3);
            }
        }
        else if (propertyId == 4)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertyGet, @__property_4, 4);
            try
            {
                return await ToBytesAsync<bool?>(_instance.Prop5, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertyGet, @__property_4, 4))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertyGet, @__property_4, 4);
            }
        }

        throw new Exception("Unknown property");
    }

    public override async Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken)
    {
        CheckPermissions([ MyCode.Permission.Perm1 ]);

        if (propertyId == 0)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertySet, @__property_0, 0);
            try
            {
                CheckAuthenticated();
                _instance.Prop1 = await FromBytesAsync<string?>(value, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertySet, @__property_0, 0))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertySet, @__property_0, 0);
            }
        }
        else if (propertyId == 2)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertySet, @__property_2, 2);
            try
            {
                _instance.Prop3 = await FromBytesAsync<int>(value, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertySet, @__property_2, 2))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertySet, @__property_2, 2);
            }
        }
        else if (propertyId == 3)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertySet, @__property_3, 3);
            try
            {
                _instance.Prop4 = await FromBytesAsync<System.Collections.Generic.IList<System.Guid>>(value, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Property value cannot be null");
                return;
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertySet, @__property_3, 3))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertySet, @__property_3, 3);
            }
        }
        else if (propertyId == 4)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertySet, @__property_4, 4);
            try
            {
                CheckAuthenticated();
                CheckPermissions([ MyCode.Permission.Perm2 ]);
                _instance.Prop5 = await FromBytesAsync<bool?>(value, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertySet, @__property_4, 4))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertySet, @__property_4, 4);
            }
        }

        throw new Exception("Unknown property");
    }

    public override async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        CheckPermissions([ MyCode.Permission.Perm1 ]);

        if (methodId == 0)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_0, 0);
            try
            {
                CheckAuthenticated();

                _instance.Method1();
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_0, 0))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_0, 0);
            }
        }
        else if (methodId == 1)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_1, 1);
            try
            {
                CheckPermissions([ MyCode.Permission.Perm3 ]);
                bool @__res = _instance.Method2();
                return (Result: await ToBytesAsync<bool>(@__res, cancellationToken).ConfigureAwait(false), null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_1, 1);
            }
        }
        else if (methodId == 2)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_2, 2);
            try
            {
                if (args is null || args.Count != 1)
                    throw new ArgumentException("Invalid number of arguments");

                var @text = await FromBytesAsync<string?>(args[0], cancellationToken).ConfigureAwait(false);
                _instance.Method3(@text);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_2, 2))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_2, 2);
            }
        }
        else if (methodId == 3)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_3, 3);
            try
            {
                if (args is null || args.Count != 1)
                    throw new ArgumentException("Invalid number of arguments");

                var @text = await FromBytesAsync<string>(args[0], cancellationToken).ConfigureAwait(false) ?? throw new ArgumentNullException("text");
                string @__res = _instance.Method4(@text);
                return (Result: await ToBytesAsync<string>(@__res, cancellationToken).ConfigureAwait(false), null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_3, 3))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_3, 3);
            }
        }
        else if (methodId == 4)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_4, 4);
            try
            {
                string @text;
                _instance.Method5(out @text);
                return (Result: null, [await ToBytesAsync<string>(@text, cancellationToken).ConfigureAwait(false)]);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_4, 4))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_4, 4);
            }
        }
        else if (methodId == 5)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_5, 5);
            try
            {
                if (args is null || args.Count != 1)
                    throw new ArgumentException("Invalid number of arguments");

                var @text = await FromBytesAsync<string>(args[0], cancellationToken).ConfigureAwait(false) ?? throw new ArgumentNullException("text");
                _instance.Method6(ref @text);
                return (Result: null, [await ToBytesAsync<string>(@text, cancellationToken).ConfigureAwait(false)]);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_5, 5))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_5, 5);
            }
        }
        else if (methodId == 6)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_6, 6);
            try
            {
                if (args is null || args.Count != 1)
                    throw new ArgumentException("Invalid number of arguments");

                var @n = await FromBytesAsync<int>(args[0], cancellationToken).ConfigureAwait(false);
                string[] @outText;
                bool @__res = _instance.Method7(ref @n, out @outText);
                return (Result: await ToBytesAsync<bool>(@__res, cancellationToken).ConfigureAwait(false), [await ToBytesAsync<int>(@n, cancellationToken).ConfigureAwait(false), await ToBytesAsync<string[]>(@outText, cancellationToken).ConfigureAwait(false)]);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_6, 6))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_6, 6);
            }
        }
        else if (methodId == 7)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_7, 7);
            try
            {
                string? @__res = _instance.Method8();
                return (Result: await ToBytesAsync<string?>(@__res, cancellationToken).ConfigureAwait(false), null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_7, 7))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_7, 7);
            }
        }
        else if (methodId == 8)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_8, 8);
            try
            {
                bool? @__res = _instance.Method9();
                return (Result: await ToBytesAsync<bool?>(@__res, cancellationToken).ConfigureAwait(false), null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_8, 8))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_8, 8);
            }
        }
        else if (methodId == 9)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_9, 9);
            try
            {
                await _instance.Method10().ConfigureAwait(false);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_9, 9))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_9, 9);
            }
        }
        else if (methodId == 10)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_10, 10);
            try
            {
                await _instance.Method11(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_10, 10))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_10, 10);
            }
        }
        else if (methodId == 11)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_11, 11);
            try
            {
                if (args is null || args.Count != 2)
                    throw new ArgumentException("Invalid number of arguments");

                var @flag = await FromBytesAsync<bool>(args[0], cancellationToken).ConfigureAwait(false);
                var @text = await FromBytesAsync<string>(args[1], cancellationToken).ConfigureAwait(false) ?? throw new ArgumentNullException("text");
                await _instance.Method12(@flag, @text, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_11, 11))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_11, 11);
            }
        }
        else if (methodId == 12)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_12, 12);
            try
            {
                if (args is null || args.Count != 2)
                    throw new ArgumentException("Invalid number of arguments");

                var @text1 = await FromBytesAsync<string>(args[0], cancellationToken).ConfigureAwait(false) ?? throw new ArgumentNullException("text1");
                var @text2 = await FromBytesAsync<string>(args[1], cancellationToken).ConfigureAwait(false) ?? throw new ArgumentNullException("text2");
                var @__res = await _instance.Method13(@text1, @text2, cancellationToken).ConfigureAwait(false);
                return (Result: await ToBytesAsync<string>(@__res, cancellationToken).ConfigureAwait(false), null);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_12, 12))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_12, 12);
            }
        }

        return (Result: null, Args: null);
    }

    public override Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        try
        {
            CheckPermissions([ MyCode.Permission.Perm1 ]);

            if (eventId == 0)
            {
                if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventAttach, @__event_0, 0);
                try
                {
                    CheckAuthenticated();
                    CheckPermissions([ MyCode.Permission.Perm2, MyCode.Permission.Perm3 ]);
                    _instance.Event1 += OnEvent1;
                }
                catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventAttach, @__event_0, 0))
                {
                    throw;
                }
                finally
                {
                    if (IsTraceEnabled) TraceExit(RpcTraceOp.EventAttach, @__event_0, 0);
                }
            }
            else if (eventId == 1)
            {
                if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventAttach, @__event_1, 1);
                try
                {
                    _instance.Event2 += OnEvent2;
                }
                catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventAttach, @__event_1, 1))
                {
                    throw;
                }
                finally
                {
                    if (IsTraceEnabled) TraceExit(RpcTraceOp.EventAttach, @__event_1, 1);
                }
            }

            return Task.CompletedTask;
        }
        catch(Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    public override Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        try
        {
            CheckPermissions([ MyCode.Permission.Perm1 ]);

            if (eventId == 0)
            {
                if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventDetach, @__event_0, 0);
                try
                {
                    CheckAuthenticated();
                    CheckPermissions([ MyCode.Permission.Perm2, MyCode.Permission.Perm3 ]);
                    _instance.Event1 -= OnEvent1;
                }
                catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventDetach, @__event_0, 0))
                {
                    throw;
                }
                finally
                {
                    if (IsTraceEnabled) TraceExit(RpcTraceOp.EventDetach, @__event_0, 0);
                }
            }
            else if (eventId == 1)
            {
                if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventDetach, @__event_1, 1);
                try
                {
                    _instance.Event2 -= OnEvent2;
                }
                catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventDetach, @__event_1, 1))
                {
                    throw;
                }
                finally
                {
                    if (IsTraceEnabled) TraceExit(RpcTraceOp.EventDetach, @__event_1, 1);
                }
            }

            return Task.CompletedTask;
        }
        catch(Exception ex)
        {
            return Task.FromException(ex);
        }
    }


    private void OnEvent1(object? sender, System.EventArgs e)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventRaise, @__event_0, 0);
        try
        {
            SendEvent(0, ToBytes<System.EventArgs>(e));
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventRaise, @__event_0, 0))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.EventRaise, @__event_0, 0);
        }
    }

    private void OnEvent2(object? sender, string e)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventRaise, @__event_1, 1);
        try
        {
            SendEvent(1, ToBytes<string>(e));
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventRaise, @__event_1, 1))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.EventRaise, @__event_1, 1);
        }
    }
}

sealed class ITestService_Proxy : Sigurn.Rpc.Infrastructure.InterfaceProxy, MyCode.ITestService
{
    [ModuleInitializer]
    internal static void Initializer()
    {
        RegisterProxy<MyCode.ITestService>(x => new ITestService_Proxy(x));
    }

    public ITestService_Proxy(Guid instanceId)
        : base(instanceId)
    {
    }

    private const string @__rpcInterfaceName = "MyCode.ITestService";
    protected override string RpcInterfaceName => @__rpcInterfaceName;

    private const string @__property_0 = "Prop1";
    private const string @__property_1 = "Prop2";
    private const string @__property_2 = "Prop3";
    private const string @__property_3 = "Prop4";
    private const string @__property_4 = "Prop5";
    private const string @__method_0 = "Method1()";
    private const string @__method_1 = "Method2()";
    private const string @__method_2 = "Method3(string?)";
    private const string @__method_3 = "Method4(string)";
    private const string @__method_4 = "Method5(out string)";
    private const string @__method_5 = "Method6(ref string)";
    private const string @__method_6 = "Method7(ref int, out string[])";
    private const string @__method_7 = "Method8()";
    private const string @__method_8 = "Method9()";
    private const string @__method_9 = "Method10()";
    private const string @__method_10 = "Method11(System.Threading.CancellationToken)";
    private const string @__method_11 = "Method12(bool, string, System.Threading.CancellationToken)";
    private const string @__method_12 = "Method13(string, string, System.Threading.CancellationToken)";
    private const string @__event_0 = "Event1";
    private const string @__event_1 = "Event2";

    string? MyCode.ITestService.Prop1
    {
        get
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertyGet, @__property_0, 0);
            try
            {
                return GetProperty<string?>(0);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertyGet, @__property_0, 0))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertyGet, @__property_0, 0);
            }
        }
        set
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertySet, @__property_0, 0);
            try
            {
                SetProperty<string?>(0, value);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertySet, @__property_0, 0))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertySet, @__property_0, 0);
            }
        }
    }

    int MyCode.ITestService.Prop2
    {
        get
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertyGet, @__property_1, 1);
            try
            {
                return GetProperty<int>(1);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertyGet, @__property_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertyGet, @__property_1, 1);
            }
        }
    }

    int MyCode.ITestService.Prop3
    {
        set
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertySet, @__property_2, 2);
            try
            {
                SetProperty<int>(2, value);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertySet, @__property_2, 2))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertySet, @__property_2, 2);
            }
        }
    }

    System.Collections.Generic.IList<System.Guid> MyCode.ITestService.Prop4
    {
        get
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertyGet, @__property_3, 3);
            try
            {
                return GetProperty<System.Collections.Generic.IList<System.Guid>>(3) ?? throw new InvalidOperationException("Property value cannot be null");
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertyGet, @__property_3, 3))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertyGet, @__property_3, 3);
            }
        }
        set
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertySet, @__property_3, 3);
            try
            {
                SetProperty<System.Collections.Generic.IList<System.Guid>>(3, value);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertySet, @__property_3, 3))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertySet, @__property_3, 3);
            }
        }
    }

    bool? MyCode.ITestService.Prop5
    {
        get
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertyGet, @__property_4, 4);
            try
            {
                return GetProperty<bool?>(4);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertyGet, @__property_4, 4))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertyGet, @__property_4, 4);
            }
        }
        set
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.PropertySet, @__property_4, 4);
            try
            {
                SetProperty<bool?>(4, value);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.PropertySet, @__property_4, 4))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.PropertySet, @__property_4, 4);
            }
        }
    }

    void MyCode.ITestService.Method1()
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_0, 0);
        try
        {
            InvokeMethod(0, [], false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_0, 0))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_0, 0);
        }
    }

    bool MyCode.ITestService.Method2()
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_1, 1);
        try
        {
            var (res, _) = InvokeMethod(1, [], false);

            return FromBytes<bool>(@res);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_1, 1))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_1, 1);
        }
    }

    void MyCode.ITestService.Method3(string? text)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_2, 2);
        try
        {
            IReadOnlyList<byte[]> @args =
            [
                ToBytes<string?>(text),
            ];

            InvokeMethod(2, @args, false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_2, 2))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_2, 2);
        }
    }

    string MyCode.ITestService.Method4(string text)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_3, 3);
        try
        {
            IReadOnlyList<byte[]> @args =
            [
                ToBytes<string>(text),
            ];

            var (res, _) = InvokeMethod(3, @args, false);

            return FromBytes<string>(@res) ?? throw new InvalidOperationException("Method return value cannot be null.");
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_3, 3))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_3, 3);
        }
    }

    void MyCode.ITestService.Method5(out string text)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_4, 4);
        try
        {
            var (_, @outArgs) = InvokeMethod(4, [], false);
            text = FromBytes<string>(@outArgs[0]);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_4, 4))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_4, 4);
        }
    }

    void MyCode.ITestService.Method6(ref string text)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_5, 5);
        try
        {
            IReadOnlyList<byte[]> @args =
            [
                ToBytes<string>(text),
            ];

            var (_, @outArgs) = InvokeMethod(5, @args, false);
            text = FromBytes<string>(@outArgs[0]);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_5, 5))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_5, 5);
        }
    }

    bool MyCode.ITestService.Method7(ref int n, out string[] outText)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_6, 6);
        try
        {
            IReadOnlyList<byte[]> @args =
            [
                ToBytes<int>(n),
            ];

            var (@res, @outArgs) = InvokeMethod(6, @args, false);

            n = FromBytes<int>(@outArgs[0]);
            outText = FromBytes<string[]>(@outArgs[1]) ?? throw new InvalidOperationException("Output value for argument 'outText' cannot be null.");

            return FromBytes<bool>(@res);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_6, 6))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_6, 6);
        }
    }

    string? MyCode.ITestService.Method8()
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_7, 7);
        try
        {
            var (res, _) = InvokeMethod(7, [], false);

            return FromBytes<string?>(@res);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_7, 7))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_7, 7);
        }
    }

    bool? MyCode.ITestService.Method9()
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_8, 8);
        try
        {
            var (res, _) = InvokeMethod(8, [], false);

            return FromBytes<bool?>(@res);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_8, 8))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_8, 8);
        }
    }

    async System.Threading.Tasks.Task MyCode.ITestService.Method10()
    {
        using var @_noTimeout = new Sigurn.Rpc.RpcContext { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_9, 9);
        try
        {
             await InvokeMethodAsync(9, [], false, System.Threading.CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_9, 9))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_9, 9);
        }
    }

    async System.Threading.Tasks.Task MyCode.ITestService.Method11(System.Threading.CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_10, 10);
        try
        {
             await InvokeMethodAsync(10, [], false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_10, 10))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_10, 10);
        }
    }

    async System.Threading.Tasks.Task MyCode.ITestService.Method12(bool flag, string text, System.Threading.CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_11, 11);
        try
        {
            IReadOnlyList<byte[]> @args =
            [
                ToBytes<bool>(flag),
                ToBytes<string>(text),
            ];

             await InvokeMethodAsync(11, @args, false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_11, 11))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_11, 11);
        }
    }

    async System.Threading.Tasks.Task<string> MyCode.ITestService.Method13(string text1, string text2, System.Threading.CancellationToken cancellationToken)
    {
        if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_12, 12);
        try
        {
            IReadOnlyList<byte[]> @args =
            [
                ToBytes<string>(text1),
                ToBytes<string>(text2),
            ];

            var (@res, _) = await InvokeMethodAsync(12, @args, false, cancellationToken).ConfigureAwait(false);
            return await FromBytesAsync<string>(@res, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Method return value cannot be null.");
        }
        catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_12, 12))
        {
            throw;
        }
        finally
        {
            if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_12, 12);
        }
    }

    private System.EventHandler? _Event1;
    event System.EventHandler MyCode.ITestService.Event1
    {
        add
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventAttach, @__event_0, 0);
            try
            {
                _Event1 += value;
                AttachEventHandler(0);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventAttach, @__event_0, 0))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.EventAttach, @__event_0, 0);
            }
        }
        remove
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventDetach, @__event_0, 0);
            try
            {
                _Event1 -= value;
                DetachEventHandler(0);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventDetach, @__event_0, 0))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.EventDetach, @__event_0, 0);
            }
        }
    }

    private System.EventHandler<string>? _Event2;
    event System.EventHandler<string> MyCode.ITestService.Event2
    {
        add
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventAttach, @__event_1, 1);
            try
            {
                _Event2 += value;
                AttachEventHandler(1);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventAttach, @__event_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.EventAttach, @__event_1, 1);
            }
        }
        remove
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventDetach, @__event_1, 1);
            try
            {
                _Event2 -= value;
                DetachEventHandler(1);
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventDetach, @__event_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.EventDetach, @__event_1, 1);
            }
        }
    }

    protected override void OnEvent(int eventId, IReadOnlyList<byte[]> args)
    {
        if (eventId == 0)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventRaise, @__event_0, 0);
            try
            {
                _Event1?.Invoke(this, FromBytes<System.EventArgs>(args[0]) ?? throw new ArgumentNullException("e"));
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventRaise, @__event_0, 0))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.EventRaise, @__event_0, 0);
            }
        }
        else if (eventId == 1)
        {
            if (IsTraceEnabled) TraceEnter(RpcTraceOp.EventRaise, @__event_1, 1);
            try
            {
                _Event2?.Invoke(this, FromBytes<string>(args[0]) ?? throw new ArgumentNullException("e"));
            }
            catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.EventRaise, @__event_1, 1))
            {
                throw;
            }
            finally
            {
                if (IsTraceEnabled) TraceExit(RpcTraceOp.EventRaise, @__event_1, 1);
            }
        }
    }
}
