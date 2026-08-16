using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Sigurn.Rpc.Infrastructure;

namespace Sigurn.Rpc.Tests;

// Adapters and proxies are registered from module initializers, which can still run while other
// code is already resolving interfaces (the serializer asks the registry on every marshaled
// interface value). A lookup that overlaps a registration must never claim a registered type is
// missing — when it does, marshaling fails with "Cannot find serializer for type ...".
public class InterfaceRegistryConcurrencyTests
{
    private const int RegistrationCount = 300;

    [Fact(Timeout = 30000)]
    public void AdapterLookup_IsNotDisruptedByConcurrentRegistrations()
    {
        AssertLookupSurvivesConcurrentRegistrations(
            () => InterfaceAdapter.IsThereAdapterFor<ITestNotification>(),
            RegisterAdapterFor);
    }

    [Fact(Timeout = 30000)]
    public void ProxyLookup_IsNotDisruptedByConcurrentRegistrations()
    {
        AssertLookupSurvivesConcurrentRegistrations(
            () => InterfaceProxy.IsThereProxyFor<ITestNotification>(),
            RegisterProxyFor);
    }

    private static void AssertLookupSurvivesConcurrentRegistrations(Func<bool> lookup, Action<Type> register)
    {
        // ITestNotification is registered by a module initializer long before this test runs, so
        // every lookup below must succeed.
        Assert.True(lookup(), "The probe type is not registered — the test cannot detect anything");

        var types = EmitInterfaces(RegistrationCount);

        var stop = false;
        var misses = 0;
        var readerFailure = (Exception?)null;

        var reader = Task.Run(() =>
        {
            try
            {
                while (!Volatile.Read(ref stop))
                {
                    if (!lookup())
                        Interlocked.Increment(ref misses);
                }
            }
            catch (Exception ex)
            {
                readerFailure = ex;
            }
        });

        foreach (var type in types)
            register(type);

        Volatile.Write(ref stop, true);
        reader.Wait(TimeSpan.FromSeconds(10));

        Assert.Null(readerFailure);
        Assert.Equal(0, Volatile.Read(ref misses));
    }

    // Interfaces created at run time, so each test run registers types nothing else has claimed.
    private static IReadOnlyList<Type> EmitInterfaces(int count)
    {
        var assemblyName = new AssemblyName($"RegistryRaceProbes_{Guid.NewGuid():N}");
        var assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Main");

        var types = new List<Type>(count);
        for (var i = 0; i < count; i++)
        {
            var builder = module.DefineType($"IRegistryRaceProbe{i}",
                TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            types.Add(builder.CreateType());
        }

        return types;
    }

    // RegisterAdapter<T>(Func<T, InterfaceAdapter>) / RegisterProxy<T>(Func<Guid, T>) for a type known
    // only at run time. The factories are never invoked here — they only have to be non-null delegates
    // of the expected shape.
    private static void RegisterAdapterFor(Type interfaceType)
    {
        var factory = MakeFactory(interfaceType, typeof(InterfaceAdapter));
        typeof(InterfaceAdapter).GetMethod(nameof(InterfaceAdapter.RegisterAdapter))!
            .MakeGenericMethod(interfaceType)
            .Invoke(null, [factory]);
    }

    private static void RegisterProxyFor(Type interfaceType)
    {
        var factory = MakeFactory(typeof(Guid), interfaceType);
        typeof(InterfaceProxy).GetMethod(nameof(InterfaceProxy.RegisterProxy))!
            .MakeGenericMethod(interfaceType)
            .Invoke(null, [factory]);
    }

    private static Delegate MakeFactory(Type argumentType, Type resultType)
    {
        var delegateType = typeof(Func<,>).MakeGenericType(argumentType, resultType);
        var parameter = Expression.Parameter(argumentType, "x");
        return Expression.Lambda(delegateType, Expression.Default(resultType), parameter).Compile();
    }
}
