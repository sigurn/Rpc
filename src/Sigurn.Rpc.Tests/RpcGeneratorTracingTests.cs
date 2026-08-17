using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sigurn.Rpc.Generator;

namespace Sigurn.Rpc.Tests;

/// <summary>
/// The generator is the only place that knows member names, so it is what turns a bare member id in
/// the log into a readable interface and member name.
/// </summary>
public class RpcGeneratorTracingTests
{
    // The generated code relies on the implicit usings a normal consumer project has, so the test
    // compilation declares the same ones globally.
    private const string Source = """
#nullable enable

global using System.Collections.Generic;
global using System.Threading.Tasks;

using Sigurn.Rpc;
using System.Threading;
using System;

namespace MyCode
{
    [RemoteInterface()]
    public interface ITracedService
    {
        string? Prop1 { get; set; }
        int Prop2 { get; }

        void Method1();
        string Method2(string text);
        Task<int> Method3(int a, CancellationToken cancellationToken);
        void Method4(ref string text, out int n);

        event EventHandler Event1;
    }
}
""";

    private static (string Generated, IReadOnlyList<Diagnostic> Diagnostics) Generate() => Generate(Source);

    // Diagnostics are everything the generated code produces, warnings included: a warning in code
    // the user did not write is noise they cannot fix.
    private static (string Generated, IReadOnlyList<Diagnostic> Diagnostics) Generate(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator) ?? [];

        var references = trustedAssemblies
            .Select(x => MetadataReference.CreateFromFile(x))
            .Append(MetadataReference.CreateFromFile(typeof(RemoteInterfaceAttribute).Assembly.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "tracing-compilation",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        Assert.Empty(compilation.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new RpcGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var generated = driver.GetRunResult().Results
            .SelectMany(x => x.GeneratedSources)
            .Single()
            .SourceText.ToString();

        return (generated, [.. output.GetDiagnostics()
            .Where(x => x.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)]);
    }

    [Fact]
    public void GeneratedCode_Compiles()
    {
        var (_, diagnostics) = Generate();
        Assert.Empty(diagnostics);
    }

    // An interface can carry no members of its own - a marker, or one that only extends another.
    // The generated code must still be clean.
    [Fact]
    public void GeneratedCode_ForAnInterfaceWithoutMembers_IsWarningFree()
    {
        var (_, diagnostics) = Generate("""
#nullable enable

global using System.Collections.Generic;
global using System.Threading.Tasks;

using Sigurn.Rpc;
using System;

namespace MyCode
{
    [RemoteInterface()]
    public interface IMarker
    {
    }
}
""");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void GeneratedProxy_DeclaresFullInterfaceName()
    {
        var (generated, _) = Generate();

        Assert.Contains("private const string @__rpcInterfaceName = \"MyCode.ITracedService\";", generated);
        Assert.Contains("protected override string RpcInterfaceName => @__rpcInterfaceName;", generated);
    }

    [Fact]
    public void GeneratedCode_DeclaresMemberNamesWithSignatures()
    {
        var (generated, _) = Generate();

        Assert.Contains("private const string @__method_0 = \"Method1()\";", generated);
        Assert.Contains("private const string @__method_1 = \"Method2(string)\";", generated);
        Assert.Contains("private const string @__method_2 = \"Method3(int, System.Threading.CancellationToken)\";", generated);
        Assert.Contains("private const string @__method_3 = \"Method4(ref string, out int)\";", generated);
        Assert.Contains("private const string @__property_0 = \"Prop1\";", generated);
        Assert.Contains("private const string @__event_0 = \"Event1\";", generated);
    }

    [Fact]
    public void GeneratedProxy_TracesEveryOperationKind_BehindALevelGuard()
    {
        var (_, proxy) = GenerateParts();

        Assert.Contains("if (IsTraceEnabled) TraceEnter(RpcTraceOp.MethodCall, @__method_1, 1);", proxy);
        Assert.Contains("if (IsTraceEnabled) TraceExit(RpcTraceOp.MethodCall, @__method_1, 1);", proxy);
        Assert.Contains("catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.MethodCall, @__method_1, 1))", proxy);

        Assert.Contains("TraceEnter(RpcTraceOp.PropertyGet, @__property_0, 0);", proxy);
        Assert.Contains("TraceEnter(RpcTraceOp.PropertySet, @__property_0, 0);", proxy);
        Assert.Contains("TraceEnter(RpcTraceOp.EventAttach, @__event_0, 0);", proxy);
        Assert.Contains("TraceEnter(RpcTraceOp.EventDetach, @__event_0, 0);", proxy);
        Assert.Contains("TraceEnter(RpcTraceOp.EventRaise, @__event_0, 0);", proxy);
    }

    // The adapter owns neither the instance id nor the request, so it does not log. It only answers
    // what a member is called, and the session writes the line.
    [Fact]
    public void GeneratedAdapter_DoesNotTrace_AndOnlyResolvesMemberNames()
    {
        var (adapter, _) = GenerateParts();

        Assert.DoesNotContain("TraceEnter", adapter);
        Assert.DoesNotContain("TraceExit", adapter);
        Assert.DoesNotContain("TraceFailure", adapter);
        Assert.DoesNotContain("IsTraceEnabled", adapter);
        Assert.DoesNotContain("RpcInterfaceName", adapter);

        Assert.Contains("protected override string? GetMemberName(RpcTraceOp operation, int memberId)", adapter);
        Assert.Contains("case RpcTraceOp.MethodCall:", adapter);
        Assert.Contains("case RpcTraceOp.PropertyGet:", adapter);
        Assert.Contains("case RpcTraceOp.PropertySet:", adapter);
        Assert.Contains("case RpcTraceOp.EventAttach:", adapter);
        Assert.Contains("case RpcTraceOp.EventDetach:", adapter);
        Assert.Contains("case RpcTraceOp.EventRaise:", adapter);
    }

    private static (string Adapter, string Proxy) GenerateParts()
    {
        var (generated, _) = Generate();

        var adapterStart = generated.IndexOf("sealed class ITracedService_Adapter", StringComparison.Ordinal);
        var proxyStart = generated.IndexOf("sealed class ITracedService_Proxy", StringComparison.Ordinal);

        Assert.True(adapterStart >= 0 && proxyStart > adapterStart);

        return (generated[adapterStart..proxyStart], generated[proxyStart..]);
    }
}
