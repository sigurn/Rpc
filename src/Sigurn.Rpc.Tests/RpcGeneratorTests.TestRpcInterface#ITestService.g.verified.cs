//HintName: ITestService.g.cs
#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices;

using Sigurn.Rpc;

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

    public override async Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken)
    {
        if (propertyId == 0)
        {
            return await ToBytesAsync<string?>(_instance.Prop1, cancellationToken);
        }
        else if (propertyId == 1)
        {
            return await ToBytesAsync<int>(_instance.Prop2, cancellationToken);
        }
        else if (propertyId == 3)
        {
            return await ToBytesAsync<System.Collections.Generic.IList<System.Guid>>(_instance.Prop4, cancellationToken) ?? throw new InvalidOperationException("Property value cannot be null");
        }
        else if (propertyId == 4)
        {
            return await ToBytesAsync<bool?>(_instance.Prop5, cancellationToken);
        }

        throw new Exception("Unknown property");
    }

    public override async Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken)
    {
        if (propertyId == 0)
        {
            _instance.Prop1 = await FromBytesAsync<string?>(value, cancellationToken);
            return;
        }
        else if (propertyId == 2)
        {
            _instance.Prop3 = await FromBytesAsync<int>(value, cancellationToken);
            return;
        }
        else if (propertyId == 3)
        {
            _instance.Prop4 = await FromBytesAsync<System.Collections.Generic.IList<System.Guid>>(value, cancellationToken) ?? throw new InvalidOperationException("Property value cannot be null");
            return;
        }
        else if (propertyId == 4)
        {
            _instance.Prop5 = await FromBytesAsync<bool?>(value, cancellationToken);
            return;
        }

        throw new Exception("Unknown property");
    }

    public override async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)
    {
        if (methodId == 0)
        {
            _instance.Method1();
        }
        else if (methodId == 1)
        {
            bool @__res = _instance.Method2();
            return (Result: await ToBytesAsync<bool>(@__res, cancellationToken), null);
        }
        else if (methodId == 2)
        {
            if (args is null || args.Count != 1)
                throw new ArgumentException("Invalid number of arguments");

            var @text = await FromBytesAsync<string?>(args[0], cancellationToken);
            _instance.Method3(@text);
        }
        else if (methodId == 3)
        {
            if (args is null || args.Count != 1)
                throw new ArgumentException("Invalid number of arguments");

            var @text = await FromBytesAsync<string>(args[0], cancellationToken) ?? throw new ArgumentNullException("text");
            string @__res = _instance.Method4(@text);
            return (Result: await ToBytesAsync<string>(@__res, cancellationToken), null);
        }
        else if (methodId == 4)
        {
            if (args is null || args.Count != 1)
                throw new ArgumentException("Invalid number of arguments");

            var @text = await FromBytesAsync<string>(args[0], cancellationToken) ?? throw new ArgumentNullException("text");
            _instance.Method5(out @text);
            return (Result: null, [await ToBytesAsync<string>(@text, cancellationToken)]);
        }
        else if (methodId == 5)
        {
            if (args is null || args.Count != 1)
                throw new ArgumentException("Invalid number of arguments");

            var @text = await FromBytesAsync<string>(args[0], cancellationToken) ?? throw new ArgumentNullException("text");
            _instance.Method6(ref @text);
            return (Result: null, [await ToBytesAsync<string>(@text, cancellationToken)]);
        }
        else if (methodId == 6)
        {
            if (args is null || args.Count != 2)
                throw new ArgumentException("Invalid number of arguments");

            var @n = await FromBytesAsync<int>(args[0], cancellationToken);
            var @outText = await FromBytesAsync<string[]>(args[1], cancellationToken) ?? throw new ArgumentNullException("outText");
            bool @__res = _instance.Method7(ref @n, out @outText);
            return (Result: await ToBytesAsync<bool>(@__res, cancellationToken), [await ToBytesAsync<int>(@n, cancellationToken), await ToBytesAsync<string[]>(@outText, cancellationToken)]);
        }
        else if (methodId == 7)
        {
            string? @__res = _instance.Method8();
            return (Result: await ToBytesAsync<string?>(@__res, cancellationToken), null);
        }
        else if (methodId == 8)
        {
            bool? @__res = _instance.Method9();
            return (Result: await ToBytesAsync<bool?>(@__res, cancellationToken), null);
        }
        else if (methodId == 9)
        {
            await _instance.Method10();
        }
        else if (methodId == 10)
        {
            await _instance.Method11(cancellationToken);
        }
        else if (methodId == 11)
        {
            if (args is null || args.Count != 2)
                throw new ArgumentException("Invalid number of arguments");

            var @flag = await FromBytesAsync<bool>(args[0], cancellationToken);
            var @text = await FromBytesAsync<string>(args[1], cancellationToken) ?? throw new ArgumentNullException("text");
            await _instance.Method12(@flag, @text, cancellationToken);
        }
        else if (methodId == 12)
        {
            if (args is null || args.Count != 2)
                throw new ArgumentException("Invalid number of arguments");

            var @text1 = await FromBytesAsync<string>(args[0], cancellationToken) ?? throw new ArgumentNullException("text1");
            var @text2 = await FromBytesAsync<string>(args[1], cancellationToken) ?? throw new ArgumentNullException("text2");
            var @__res = await _instance.Method13(@text1, @text2, cancellationToken);
            return (Result: await ToBytesAsync<string>(@__res, cancellationToken), null);
        }

        return (Result: null, Args: null);
    }

    public override Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)
    {
        try
        {
            if (eventId == 0)
                _instance.Event1 += OnEvent1;
            else if (eventId == 1)
                _instance.Event2 += OnEvent2;

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
            if (eventId == 0)
                _instance.Event1 -= OnEvent1;
            else if (eventId == 1)
                _instance.Event2 -= OnEvent2;

            return Task.CompletedTask;
        }
        catch(Exception ex)
        {
            return Task.FromException(ex);
        }
    }


    private void OnEvent1(object? sender, System.EventArgs e)
    {
        SendEvent(0, ToBytes<System.EventArgs>(e));
    }

    private void OnEvent2(object? sender, string e)
    {
        SendEvent(1, ToBytes<string>(e));
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

    string? MyCode.ITestService.Prop1
    {
        get => GetProperty<string?>(0);
        set => SetProperty<string?>(0, value);
    }

    int MyCode.ITestService.Prop2
    {
        get => GetProperty<int>(1);
    }

    int MyCode.ITestService.Prop3
    {
        set => SetProperty<int>(2, value);
    }

    System.Collections.Generic.IList<System.Guid> MyCode.ITestService.Prop4
    {
        get => GetProperty<System.Collections.Generic.IList<System.Guid>>(3) ?? throw new InvalidOperationException("Property value cannot be null");
        set => SetProperty<System.Collections.Generic.IList<System.Guid>>(3, value);
    }

    bool? MyCode.ITestService.Prop5
    {
        get => GetProperty<bool?>(4);
        set => SetProperty<bool?>(4, value);
    }

    void MyCode.ITestService.Method1()
    {
        InvokeMethod(0, [], false);
    }

    bool MyCode.ITestService.Method2()
    {
        var (res, _) = InvokeMethod(1, [], false);

        return FromBytes<bool>(@res);
    }

    void MyCode.ITestService.Method3(string? text)
    {
        IReadOnlyList<byte[]> @args =
        [
            ToBytes<string?>(text),
        ];

        InvokeMethod(2, @args, false);
    }

    string MyCode.ITestService.Method4(string text)
    {
        IReadOnlyList<byte[]> @args =
        [
            ToBytes<string>(text),
        ];

        var (res, _) = InvokeMethod(3, @args, false);

        return FromBytes<string>(@res) ?? throw new InvalidOperationException("Method return value cannot be null.");
    }

    void MyCode.ITestService.Method5(out string text)
    {
        IReadOnlyList<byte[]> @args =
        [
            ToBytes<string>(text),
        ];

        var (_, @outArgs) = InvokeMethod(4, @args, false);
        text = FromBytes<string>(@outArgs[0]);
    }

    void MyCode.ITestService.Method6(ref string text)
    {
        IReadOnlyList<byte[]> @args =
        [
            ToBytes<string>(text),
        ];

        var (_, @outArgs) = InvokeMethod(5, @args, false);
        text = FromBytes<string>(@outArgs[0]);
    }

    bool MyCode.ITestService.Method7(ref int n, out string[] outText)
    {
        IReadOnlyList<byte[]> @args =
        [
            ToBytes<int>(n),
            ToBytes<string[]>(outText),
        ];

        var (@res, @outArgs) = InvokeMethod(6, @args, false);

        n = FromBytes<int>(@outArgs[0]);
        outText = FromBytes<string[]>(@outArgs[1]) ?? throw new InvalidOperationException("Output value for argument 'outText' cannot be null.");

        return FromBytes<bool>(@res);
    }

    string? MyCode.ITestService.Method8()
    {
        var (res, _) = InvokeMethod(7, [], false);

        return FromBytes<string?>(@res);
    }

    bool? MyCode.ITestService.Method9()
    {
        var (res, _) = InvokeMethod(8, [], false);

        return FromBytes<bool?>(@res);
    }

    async System.Threading.Tasks.Task MyCode.ITestService.Method10()
    {
         await InvokeMethodAsync(9, [], false, System.Threading.CancellationToken.None);
    }

    async System.Threading.Tasks.Task MyCode.ITestService.Method11(System.Threading.CancellationToken cancellationToken)
    {
         await InvokeMethodAsync(10, [], false, cancellationToken);
    }

    async System.Threading.Tasks.Task MyCode.ITestService.Method12(bool flag, string text, System.Threading.CancellationToken cancellationToken)
    {
        IReadOnlyList<byte[]> @args =
        [
            ToBytes<bool>(flag),
            ToBytes<string>(text),
        ];

         await InvokeMethodAsync(11, @args, false, cancellationToken);
    }

    async System.Threading.Tasks.Task<string> MyCode.ITestService.Method13(string text1, string text2, System.Threading.CancellationToken cancellationToken)
    {
        IReadOnlyList<byte[]> @args =
        [
            ToBytes<string>(text1),
            ToBytes<string>(text2),
        ];

        var (@res, _) = await InvokeMethodAsync(12, @args, false, cancellationToken);
        return await FromBytesAsync<string>(@res, cancellationToken) ?? throw new InvalidOperationException("Method return value cannot be null.");
    }

    private System.EventHandler? _Event1;
    event System.EventHandler MyCode.ITestService.Event1
    {
        add
        {
            _Event1 += value;
            AttachEventHandler(0);
        }
        remove
        {
            _Event1 -= value;
            DetachEventHandler(0);
        }
    }

    private System.EventHandler<string>? _Event2;
    event System.EventHandler<string> MyCode.ITestService.Event2
    {
        add
        {
            _Event2 += value;
            AttachEventHandler(1);
        }
        remove
        {
            _Event2 -= value;
            DetachEventHandler(1);
        }
    }

    protected override void OnEvent(int eventId, IReadOnlyList<byte[]> args)
    {
        if (eventId == 0)
        {
            _Event1?.Invoke(this, FromBytes<System.EventArgs>(args[0]) ?? throw new ArgumentNullException("e"));
        }
        else if (eventId == 1)
        {
            _Event2?.Invoke(this, FromBytes<string>(args[0]) ?? throw new ArgumentNullException("e"));
        }
    }
}
