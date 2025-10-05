using System.Runtime.CompilerServices;
using DiffEngine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sigurn.Rpc.Generator;

namespace Sigurn.Rpc.Tests;

public static class VerifyInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyDiffPlex.Initialize();
        VerifySourceGenerators.Initialize();
        DiffTools.UseOrder(DiffTool.VisualStudioCode, DiffTool.Rider);
    }
}

public class RpcGeneratorTests
{
    private static readonly string dotNetAssemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty;

    [Fact]
    public Task Run() => VerifyChecks.Run();

    [Fact]
    public async Task TestRpcInterface()
    {
        Compilation inputCompilation = CreateCompilation(
@"
#nullable enable

using Sigurn.Rpc;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System;

namespace MyCode
{
    [RemoteInterface()]
    public interface ITestService
    {
        string? Prop1 { get; set; }
        int Prop2 { get; }
        int Prop3 { set; }
        IList<Guid> Prop4 { get; init; }
        bool? Prop5 { get; set; }

        void Method1 ();
        bool Method2 ();
        void Method3(string? text);
        string Method4(string text);
        void Method5(out string text);
        void Method6(ref string text);
        bool Method7(ref int n, out string[] outText);
        string? Method8();
        bool? Method9();
        Task Method10();
        Task Method11(CancellationToken cancellationToken);
        Task Method12(bool flag, string text, CancellationToken cancellationToken);
        Task<string> Method13(string text1, string text2, CancellationToken cancellationToken);

        event EventHandler Event1;
        event EventHandler<string> Event2;
    }
}
");
        var diag = inputCompilation.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error);
        Assert.Empty(diag);

        RpcGenerator generator = new RpcGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(inputCompilation);
        await Verify(driver);
    }

    // private static Compilation CreateCompilation(string source)
    // {
    //     var parseOptions = CSharpParseOptions.Default
    //         .WithLanguageVersion(LanguageVersion.Latest)
    //         .WithFeatures(new[] { new KeyValuePair<string, string>("nullable", "enable") });

    //     return CSharpCompilation.Create("compilation",
    //         new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
    //         new[] {
    //             MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, "mscorlib.dll")),
    //             MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, "System.dll")),
    //             MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, "System.Private.Xml.dll")),
    //             MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, "System.Xml.ReaderWriter.dll")),
    //             MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, "System.Core.dll")),
    //             MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, "System.Private.CoreLib.dll")),
    //             MetadataReference.CreateFromFile(Path.Combine(dotNetAssemblyPath, "System.Runtime.dll")),
    //             MetadataReference.CreateFromFile(typeof(NullableAttribute).Assembly.Location),
    //             MetadataReference.CreateFromFile(typeof(System.Diagnostics.CodeAnalysis.NotNullAttribute).Assembly.Location),
    //             MetadataReference.CreateFromFile(typeof(RemoteInterfaceAttribute).Assembly.Location)
    //          },
    //         new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    //             .WithNullableContextOptions(NullableContextOptions.Enable));
    // }
    
    private static Compilation CreateCompilation(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest);

        // Получаем все "trusted" assemblies, которые .NET загружает для текущего рантайма
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            ?? Array.Empty<string>();

        var references = trustedAssemblies
            .Select(p => MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(RemoteInterfaceAttribute).Assembly.Location))
            .ToArray();

        return CSharpCompilation.Create(
            "compilation",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }
}

