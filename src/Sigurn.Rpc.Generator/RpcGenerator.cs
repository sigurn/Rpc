using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    static class IsExternalInit
    {
    }
}

namespace Sigurn.Rpc.Generator
{
    readonly record struct ArgInfo(string Name, ITypeSymbol Type, bool IsNullable, string[] Modifiers);
    readonly record struct TypePropertyInfo(string Name, TypeInfo Type, int Id, bool isGettable, bool isSettable);
    readonly record struct TypeMethodInfo(string Name, TypeInfo ReturnType, int Id, bool oneWay, EquatableArray<ArgInfo> Args);
    readonly record struct TypeEventInfo(string Name, TypeInfo Type, int Id, ITypeSymbol ReturnType, EquatableArray<ArgInfo> Args);
    readonly record struct RemoteInterfaceTypeInfo(string TypeNamespace, string TypeName, string AdapterName, string ProxyName, EquatableArray<TypePropertyInfo> Properties, EquatableArray<TypeMethodInfo> Methods, EquatableArray<TypeEventInfo> Events);

    /// <summary>
    /// Rpc generator.
    /// </summary>
    [Generator]
    public class RpcGenerator : IIncrementalGenerator
    {
        private const string _remoteInterfaceAttributeName = "Sigurn.Rpc.RemoteInterfaceAttribute";
        private const string _taskName = "System.Threading.Tasks.Task";
        private const string _genericTaskName = "System.Threading.Tasks.Task<TResult>";
        private const string _cancellationTokenName = "System.Threading.CancellationToken";
        //private const string _serializationIgnoreAttributeName = "Sigurn.Serialize.SerializeIgnoreAttribute";
        //private const string _serializationOrderIdAttributeName = "Sigurn.Serialize.SerializeOrderAttribute";

        /// <inheritdoc/>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<RemoteInterfaceTypeInfo> typesToGenerateInfrastructureClasses =
            context.SyntaxProvider.ForAttributeWithMetadataName
            (
                _remoteInterfaceAttributeName,
                predicate: (s, _) => s is InterfaceDeclarationSyntax,
                transform: (ctx, _) => GetRemoteInterfaceTypeInfo(ctx.SemanticModel, (InterfaceDeclarationSyntax)ctx.TargetNode)
            );

            context.RegisterSourceOutput(typesToGenerateInfrastructureClasses, (spc, source) => Execute(source, spc));
        }

        private void Execute(RemoteInterfaceTypeInfo riti, SourceProductionContext context)
        {
            // StringBuilder toStreamStringBuilder = new StringBuilder();
            // StringBuilder fromStreamStringBuilder = new StringBuilder();
            // if (riti.Properties.Count != 0)
            //     toStreamStringBuilder.Append($"    public async Task ToStreamAsync(Stream stream, {tti.TypeNamespace}.{tti.TypeName} value, SerializationContext context, CancellationToken cancellationToken)\n");
            // else
            //     toStreamStringBuilder.Append($"    public Task ToStreamAsync(Stream stream, {tti.TypeNamespace}.{tti.TypeName} value, SerializationContext context, CancellationToken cancellationToken)\n");
            // toStreamStringBuilder.Append("    {\n");
            // toStreamStringBuilder.Append("        ArgumentNullException.ThrowIfNull(stream);\n");
            // toStreamStringBuilder.Append("        ArgumentNullException.ThrowIfNull(context);\n");
            // toStreamStringBuilder.Append("\n");

            // if (tti.Properties.Count != 0)
            //     fromStreamStringBuilder.Append($"    public async Task<{tti.TypeNamespace}.{tti.TypeName}> FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)\n");
            // else
            //     fromStreamStringBuilder.Append($"    public Task<{tti.TypeNamespace}.{tti.TypeName}> FromStreamAsync(Stream stream, SerializationContext context, CancellationToken cancellationToken)\n");
            // fromStreamStringBuilder.Append("    {\n");
            // fromStreamStringBuilder.Append("        ArgumentNullException.ThrowIfNull(stream);\n");
            // fromStreamStringBuilder.Append("        ArgumentNullException.ThrowIfNull(context);\n");
            // fromStreamStringBuilder.Append("\n");
            // if (tti.Properties.Count != 0)
            //     fromStreamStringBuilder.Append($"        return new {tti.TypeNamespace}.{tti.TypeName}()\n");
            // else
            //     fromStreamStringBuilder.Append($"        return Task.FromResult(new {tti.TypeNamespace}.{tti.TypeName}()\n");
            // fromStreamStringBuilder.Append("        {\n");

            // foreach (var p in tti.Properties.OrderBy(x => x.OrderId))
            // {
            //     toStreamStringBuilder.Append($"        await Serializer.ToStreamAsync<{p.Type}>(stream, value.{p.Name}, context, cancellationToken);\n");
            //     fromStreamStringBuilder.Append($"            {p.Name} = await Serializer.FromStreamAsync<{p.Type}>(stream, context, cancellationToken),\n");
            // }

            // if (tti.Properties.Count == 0)
            //     toStreamStringBuilder.Append($"        return Task.CompletedTask;\n");

            // toStreamStringBuilder.Append("    }\n");

            // if (tti.Properties.Count == 0)
            //     fromStreamStringBuilder.Append("        });\n");
            // else
            //     fromStreamStringBuilder.Append("        };\n");
            // fromStreamStringBuilder.Append("    }\n");

            string fullTypeName = $"{riti.TypeNamespace}.{riti.TypeName}";

            StringBuilder sb = new StringBuilder();
            // var useGlobally = tti.UseGlobally ? "true" : "false";
            sb.Append("#nullable enable\n");
            sb.Append("\n");
            sb.Append($"using System;\n");
            sb.Append($"using System.IO;\n");
            sb.Append($"using System.Threading;\n");
            sb.Append($"using System.Runtime.CompilerServices;\n");
            sb.Append("\n");
            sb.Append($"using Sigurn.Rpc;\n");
            sb.Append("\n");
            sb.Append($"namespace {riti.TypeNamespace}.Rpc.Infrastructure;\n");
            sb.Append("\n");

            sb.Append(GetAdapterCode(fullTypeName, riti, context));
            sb.Append("\n");
            sb.Append(GetProxyCode(fullTypeName, riti, context));

            context.AddSource($"{riti.TypeName}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        private StringBuilder GetAdapterCode(string fullTypeName, RemoteInterfaceTypeInfo riti, SourceProductionContext context)
        {
            var sb = new StringBuilder();

            sb.Append($"sealed class {riti.AdapterName} : Sigurn.Rpc.Infrastructure.InterfaceAdapter\n");
            sb.Append("{\n");

            sb.Append($"    [ModuleInitializer]\n");
            sb.Append($"    internal static void Initializer()\n");
            sb.Append("    {\n");
            sb.Append($"        RegisterAdapter<{fullTypeName}>(x => new {riti.AdapterName}(x));\n");
            sb.Append("    }\n");
            sb.Append("\n");

            sb.Append($"    private readonly {fullTypeName} _instance;\n");
            sb.Append("\n");

            sb.Append($"    public {riti.AdapterName}({fullTypeName} instance)\n");
            sb.Append($"        : base(typeof({fullTypeName}), instance)\n");
            sb.Append("    {\n");
            sb.Append("        _instance = instance;\n");
            sb.Append("    }\n");
            sb.Append("\n");

            var gsb = new StringBuilder();
            var ssb = new StringBuilder();
            if (riti.Properties.Count != 0)
            {
                gsb.Append("    public override async Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken)\n");
                gsb.Append("    {\n");

                ssb.Append("    public override async Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken)\n");
                ssb.Append("    {\n");

                bool firstGetter = true;
                bool firstSetter = true;
                foreach (var p in riti.Properties)
                {
                    if (p.isGettable)
                    {
                        if (firstGetter)
                            gsb.Append($"        if (propertyId == {p.Id})\n");
                        else
                            gsb.Append($"        else if (propertyId == {p.Id})\n");
                        gsb.Append("        {\n");
                        gsb.Append($"            return await ToBytesAsync<{p.Type.Type}>(_instance.{p.Name}, cancellationToken);\n");
                        gsb.Append("        }\n");
                        firstGetter = false;
                    }

                    if (p.isSettable)
                    {
                        if (firstSetter)
                            ssb.Append($"        if (propertyId == {p.Id})\n");
                        else
                            ssb.Append($"        else if (propertyId == {p.Id})\n");
                        ssb.Append("        {\n");
                        ssb.Append($"            _instance.{p.Name} = await FromBytesAsync<{p.Type.Type}>(value, cancellationToken);\n");
                        ssb.Append("            return;\n");
                        ssb.Append("        }\n");
                        firstSetter = false;
                    }
                }
                gsb.Append("\n");
                gsb.Append("        throw new Exception(\"Unknown property\");\n");
                gsb.Append("    }\n");

                ssb.Append("\n");
                ssb.Append("        throw new Exception(\"Unknown property\");\n");
                ssb.Append("    }\n");
            }

            sb.Append(gsb);
            sb.Append("\n");
            sb.Append(ssb);

            if (riti.Methods.Count != 0)
            {
                sb.Append("\n");
                sb.Append("    public override async Task<(byte[]? Result, IReadOnlyList<byte[]>? Args)> InvokeMethodAsync(int methodId, IReadOnlyList<byte[]>? args, bool oneWay, CancellationToken cancellationToken)\n");
                sb.Append("    {\n");
                bool firstMethod = true;
                foreach (var m in riti.Methods)
                {
                    if (firstMethod)
                        sb.Append($"        if (methodId == {m.Id})\n");
                    else
                        sb.Append($"        else if (methodId == {m.Id})\n");
                    sb.Append("        {\n");
                    var count = m.Args.Where(x => x.Type.ToString() != _cancellationTokenName).Count();
                    if (count != 0)
                    {
                        sb.Append($"            if (args is null || args.Count != {count})\n");
                        sb.Append("                throw new ArgumentException(\"Invalid number of arguments\");\n");
                        sb.Append("\n");
                    }

                    var args = string.Empty;
                    int n = 0;
                    bool outArgs = false;
                    foreach (var a in m.Args)
                    {
                        var argName = $"@{a.Name}";
                        if (a.Type.ToString() == _cancellationTokenName)
                        {
                            argName = "cancellationToken";
                        }
                        else
                        {
                            sb.Append($"            var {argName} = await FromBytesAsync<{a.Type}>(args[{n++}], cancellationToken);\n");
                        }

                        if (args != string.Empty)
                            args += ", ";

                        if (a.Modifiers.Contains("out") || a.Modifiers.Contains("ref"))
                        {
                            args += string.Join(" ", a.Modifiers);
                            args += " ";
                            outArgs = true;
                        }
                        args += $"{argName}";
                    }

                    if (m.ReturnType.Type?.ToString() == "void")
                    {
                        sb.Append($"            _instance.{m.Name}({args});\n");
                        if (outArgs)
                        {
                            var outArgsString = string.Join(", ", m.Args
                                .Where(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out"))
                                .Select(x => $"await ToBytesAsync<{x.Type}>(@{x.Name}, cancellationToken)"));
                            sb.Append($"            return (Result: null, [{outArgsString}]);\n");
                        }
                    }
                    else if (m.ReturnType.Type?.ToString() == _taskName)
                    {
                        sb.Append($"            await _instance.{m.Name}({args});\n");
                    }
                    else if (m.ReturnType.Type is INamedTypeSymbol nts &&
                        nts.IsGenericType && nts.ConstructedFrom.ToString() == _genericTaskName)
                    {
                        sb.Append($"            var @__res = await _instance.{m.Name}({args});\n");
                        sb.Append($"            return (Result: await ToBytesAsync<{nts.TypeArguments[0]}>(@__res, cancellationToken), null);\n");
                    }
                    else
                    {
                        sb.Append($"            {m.ReturnType.Type} @__res = _instance.{m.Name}({args});\n");
                        if (outArgs)
                        {
                            var outArgsString = string.Join(", ", m.Args
                                .Where(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out"))
                                .Select(x => $"await ToBytesAsync<{x.Type}>(@{x.Name}, cancellationToken)"));
                            sb.Append($"            return (Result: await ToBytesAsync<{m.ReturnType.Type}>(@__res, cancellationToken), [{outArgsString}]);\n");
                        }
                        else
                        {
                            sb.Append($"            return (Result: await ToBytesAsync<{m.ReturnType.Type}>(@__res, cancellationToken), null);\n");
                        }
                    }
                    sb.Append("        }\n");
                    firstMethod = false;
                }
                sb.Append("\n");
                sb.Append("        return (Result: null, Args: null);\n");
                sb.Append("    }\n");
            }

            if (riti.Events.Count != 0)
            {
                StringBuilder aesb = new StringBuilder();
                StringBuilder desb = new StringBuilder();
                StringBuilder ehsb = new StringBuilder();
                aesb.Append("    public override Task AttachEventHandlerAsync(int eventId, CancellationToken cancellationToken)\n");
                aesb.Append("    {\n");
                aesb.Append("        try\n");
                aesb.Append("        {\n");

                desb.Append("    public override Task DetachEventHandlerAsync(int eventId, CancellationToken cancellationToken)\n");
                desb.Append("    {\n");
                desb.Append("        try\n");
                desb.Append("        {\n");

                bool firstEvent = true;
                foreach (var e in riti.Events)
                {
                    var args = string.Join(", ", e.Args.Select(x => $"{x.Type} {x.Name}"));

                    ehsb.Append("\n");
                    ehsb.Append($"    private void On{e.Name}({args})\n");
                    ehsb.Append("    {\n");
                    var eventArgs = e.Args
                        .Where(x => x.Name != "sender" && x.Type.ToString() != "object?")
                        .ToArray();
                    if (eventArgs.Length != 0)
                    {
                        ehsb.Append($"        SendEvent({e.Id}");
                        foreach (var ea in eventArgs)
                            ehsb.Append($", ToBytes<{ea.Type}>({ea.Name})");
                        ehsb.Append(");\n");
                    }
                    ehsb.Append("    }\n");
                    if (firstEvent)
                    {
                        aesb.Append($"            if (eventId == {e.Id})\n");
                        desb.Append($"            if (eventId == {e.Id})\n");
                    }
                    else
                    {
                        aesb.Append($"            else if (eventId == {e.Id})\n");
                        desb.Append($"            else if (eventId == {e.Id})\n");
                    }
                    aesb.Append($"                _instance.{e.Name} += On{e.Name};\n");
                    desb.Append($"                _instance.{e.Name} -= On{e.Name};\n");
                    firstEvent = false;
                }
                aesb.Append("\n");
                aesb.Append("            return Task.CompletedTask;\n");
                aesb.Append("        }\n");
                aesb.Append("        catch(Exception ex)\n");
                aesb.Append("        {\n");
                aesb.Append("            return Task.FromException(ex);\n");
                aesb.Append("        }\n");
                aesb.Append("    }\n");

                desb.Append("\n");
                desb.Append("            return Task.CompletedTask;\n");
                desb.Append("        }\n");
                desb.Append("        catch(Exception ex)\n");
                desb.Append("        {\n");
                desb.Append("            return Task.FromException(ex);\n");
                desb.Append("        }\n");
                desb.Append("    }\n");

                sb.Append("\n");
                sb.Append(aesb);
                sb.Append("\n");
                sb.Append(desb);
                sb.Append("\n");
                sb.Append(ehsb);
            }

            sb.Append("}\n");

            return sb;
        }

        private StringBuilder GetProxyCode(string fullTypeName, RemoteInterfaceTypeInfo riti, SourceProductionContext context)
        {
            var sb = new StringBuilder();
            sb.Append($"sealed class {riti.ProxyName} : Sigurn.Rpc.Infrastructure.InterfaceProxy, {fullTypeName}\n");
            sb.Append("{\n");

            sb.Append($"    [ModuleInitializer]\n");
            sb.Append($"    internal static void Initializer()\n");
            sb.Append("    {\n");
            sb.Append($"        RegisterProxy<{fullTypeName}>(x => new {riti.ProxyName}(x));\n");
            sb.Append("    }\n");
            sb.Append("\n");


            sb.Append($"    public {riti.ProxyName}(Guid instanceId)\n");
            sb.Append("        : base(instanceId)\n");
            sb.Append("    {\n");
            sb.Append("    }\n");
            sb.Append("\n");

            if (riti.Properties.Count != 0)
            {
                foreach (var p in riti.Properties)
                {
                    sb.Append($"    {p.Type.Type} {fullTypeName}.{p.Name}\n");
                    sb.Append("    {\n");
                    if (p.isGettable)
                        sb.Append($"        get => GetProperty<{p.Type.Type}>({p.Id});\n");
                    if (p.isSettable)
                        sb.Append($"        set => SetProperty<{p.Type.Type}>({p.Id}, value);\n");
                    sb.Append("    }\n");
                    sb.Append("\n");
                }
            }

            if (riti.Methods.Count != 0)
            {
                foreach (var m in riti.Methods)
                {
                    {
                        bool isAsync = m.ReturnType.Type?.ToString() == _taskName ||
                            (m.ReturnType.Type is INamedTypeSymbol nts &&
                            nts.IsGenericType && nts.ConstructedFrom.ToString() == _genericTaskName);
                        if (isAsync)
                            sb.Append($"    async {m.ReturnType.Type} {fullTypeName}.{m.Name}(");
                        else
                            sb.Append($"    {m.ReturnType.Type} {fullTypeName}.{m.Name}(");
                    }
                    sb.Append(string.Join(", ", m.Args.Select(a =>
                    {
                        var modifiers = string.Join(" ", a.Modifiers);
                        return string.IsNullOrEmpty(modifiers) ? $"{a.Type} {a.Name}" : $"{modifiers} {a.Type} {a.Name}";
                    })));
                    sb.Append(")\n");
                    sb.Append("    {\n");
                    bool args = false;
                    bool outArgs = false;
                    string? cancellationToken = m.Args
                        .Where(x => x.Type.ToString() == _cancellationTokenName)
                        .Select(x => x.Name)
                        .FirstOrDefault() ?? $"{_cancellationTokenName}.None";
                    var realArgs = m.Args
                        .Where(x => x.Type.ToString() != _cancellationTokenName)
                        .ToArray();
                    if (realArgs.Any())
                    {
                        sb.Append("        IReadOnlyList<byte[]> args =\n");
                        sb.Append("        [\n");

                        foreach (var a in realArgs)
                        {
                            if (a.Modifiers.Contains("ref") || a.Modifiers.Contains("out")) outArgs = true;
                            sb.Append($"            ToBytes<{a.Type}>({a.Name}),\n");
                        }
                        sb.Append("        ];\n");
                        sb.Append("\n");
                        args = true;
                    }
                    var argsText = args ? "args" : "[]";
                    if (m.ReturnType.Type?.ToString() == "void")
                    {
                        var resText = outArgs ? "var (_, outArgs) = " : "";
                        bool oneWay = !outArgs && m.oneWay;
                        sb.Append($"        {resText}InvokeMethod({m.Id}, {argsText}, {oneWay.ToString().ToLower()});\n");

                        if (outArgs)
                        {
                            var an = 0;
                            foreach (var oa in m.Args.Where(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out")))
                                sb.Append($"        {oa.Name} = FromBytes<{oa.Type}>(outArgs[{an++}]);\n");
                        }
                    }
                    else if (m.ReturnType.Type?.ToString() == _taskName)
                    {
                        var resText = outArgs ? "var (_, outArgs) = " : "";
                        bool oneWay = !outArgs && m.oneWay;

                        sb.Append($"        {resText} await InvokeMethodAsync({m.Id}, {argsText}, {oneWay.ToString().ToLower()}, {cancellationToken});\n");

                        if (outArgs)
                        {
                            var an = 0;
                            foreach (var oa in m.Args.Where(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out")))
                                sb.Append($"        {oa.Name} = await FromBytesAsync<{oa.Type}>(outArgs[{an++}], {cancellationToken});\n");
                        }
                    }
                    else if (m.ReturnType.Type is INamedTypeSymbol nts &&
                        nts.IsGenericType && nts.ConstructedFrom.ToString() == _genericTaskName)
                    {
                        sb.Append($"        var (res, _) = await InvokeMethodAsync({m.Id}, {argsText}, false, {cancellationToken});\n");
                        sb.Append($"        return await FromBytesAsync<{nts.TypeArguments[0]}>(res, {cancellationToken});\n");
                    }
                    else
                    {
                        var resText = outArgs ? "(res, outArgs)" : "(res, _)";
                        sb.Append($"        var {resText} = InvokeMethod({m.Id}, {argsText}, false);\n");
                        sb.Append("\n");
                        if (outArgs)
                        {
                            var an = 0;
                            foreach (var oa in m.Args.Where(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out")))
                                sb.Append($"        {oa.Name} = FromBytes<{oa.Type}>(outArgs[{an++}]);\n");
                            sb.Append("\n");
                        }

                        sb.Append($"        return FromBytes<{m.ReturnType.Type}>(res);\n");
                    }

                    sb.Append("    }\n");
                    sb.Append("\n");
                }
            }

            if (riti.Events.Count != 0)
            {
                StringBuilder ehsp = new StringBuilder();
                ehsp.Append("    protected override void OnEvent(int eventId, IReadOnlyList<byte[]> args)\n");
                ehsp.Append("    {\n");
                bool firstEvent = true;
                foreach (var e in riti.Events)
                {
                    sb.Append($"    private {e.Type.Type}? _{e.Name};\n");
                    sb.Append($"    event {e.Type.Type} {fullTypeName}.{e.Name}\n");
                    sb.Append("    {\n");
                    sb.Append($"        add\n");
                    sb.Append("        {\n");
                    sb.Append($"            _{e.Name} += value;\n");
                    sb.Append($"            AttachEventHandler({e.Id});\n");
                    sb.Append("        }\n");
                    sb.Append($"        remove\n");
                    sb.Append("        {\n");
                    sb.Append($"            _{e.Name} -= value;\n");
                    sb.Append($"            DetachEventHandler({e.Id});\n");
                    sb.Append("        }\n");
                    sb.Append("    }\n");
                    sb.Append("\n");

                    if (firstEvent)
                        ehsp.Append($"        if (eventId == {e.Id})\n");
                    else
                        ehsp.Append($"        else if (eventId == {e.Id})\n");
                    ehsp.Append("        {\n");
                    ehsp.Append($"            _{e.Name}?.Invoke(");
                    int an = 0;
                    bool firstArg = true;
                    foreach (var a in e.Args)
                    {
                        if (!firstArg)
                            ehsp.Append(", ");
                        if (a.Type.ToString().StartsWith("object") && a.Name == "sender")
                            ehsp.Append("this");
                        else
                        {
                            ehsp.Append($"FromBytes<{a.Type}>(args[{an}])");
                            an++;
                        }
                        firstArg = false;
                    }
                    ehsp.Append(");\n");
                    ehsp.Append("        }\n");
                    firstEvent = false;
                }
                ehsp.Append("    }\n");
                sb.Append(ehsp);
            }

            sb.Append("}\n");

            return sb;
        }

        private RemoteInterfaceTypeInfo GetRemoteInterfaceTypeInfo(SemanticModel semanticModel, InterfaceDeclarationSyntax syntaxNode)
        {
            var nullableContext = semanticModel.GetNullableContext(syntaxNode.SpanStart);
            var typeName = syntaxNode.Identifier.Text;

            var ns = syntaxNode.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();

            var typeNamespace = GetFullNamespace(ns);
            var infrastructureNamespace = $"{typeNamespace}.Rpc.Infrastructure";
            var adapterName = $"{typeName}_Adapter";
            var proxyName = $"{typeName}_Proxy";

            // var generateAttr = GetAttribute(syntaxNode, semanticModel, _generateSerializerAttributeName);
            // if (generateAttr != null && generateAttr.ArgumentList?.Arguments.Count != 0)
            // {
            //     var attrArg = generateAttr.ArgumentList?.Arguments[0];
            //     if (attrArg is not null)
            //     {
            //         var constantValue = semanticModel.GetConstantValue(attrArg.Expression);
            //         if (constantValue.HasValue && constantValue.Value is bool b)
            //             useGlobally = b;
            //     }
            // } 


            var publicProps = syntaxNode.Members.OfType<PropertyDeclarationSyntax>()
                .Where(x =>
                {
                    var getAccessor = x.AccessorList?.Accessors.Where(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)).FirstOrDefault();
                    var setAccessor = x.AccessorList?.Accessors.Where(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)).FirstOrDefault();
                    if (getAccessor is null && setAccessor is null) return false;

                    // var initAccessor = x.AccessorList?.Accessors.Where(a => a.IsKind(SyntaxKind.InitAccessorDeclaration)).FirstOrDefault();
                    //if (setAccessor is null) return false;
                    //if (setAccessor != null && setAccessor.Modifiers.Count != 0 && 
                    //     !setAccessor.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) return false;

                    // if (initAccessor != null && initAccessor.Modifiers.Count != 0 && 
                    //     !initAccessor.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)) &&
                    //     !initAccessor.Modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword))) return false;

                    return true;
                });

            var props = new EquatableArray<TypePropertyInfo>(publicProps.Select((p, i) =>
            {
                var name = p.Identifier.Text;
                var type = semanticModel.GetTypeInfo(p.Type);
                var getAccessor = p.AccessorList?.Accessors.Where(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)).FirstOrDefault();
                var setAccessor = p.AccessorList?.Accessors.Where(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)).FirstOrDefault();
                int orderId = i;

                // var orderAttr = GetAttribute(p, semanticModel, _serializationOrderIdAttributeName);
                // if (orderAttr != null && orderAttr.ArgumentList?.Arguments.Count != 0)
                // {
                //     var attrArg = orderAttr.ArgumentList?.Arguments[0];
                //     if (attrArg is not null)
                //     {
                //         var constantValue = semanticModel.GetConstantValue(attrArg.Expression);
                //         if (constantValue.HasValue && constantValue.Value is int n)
                //             orderId = n;
                //     }
                // } 

                return new TypePropertyInfo(name, type, orderId, getAccessor is not null, setAccessor is not null);
            }).ToArray());

            var methods = new EquatableArray<TypeMethodInfo>(syntaxNode.Members
                .OfType<MethodDeclarationSyntax>()
                .Select((x, i) =>
                {
                    var name = x.Identifier.Text;
                    var returnType = semanticModel.GetTypeInfo(x.ReturnType);
                    int orderId = i;
                    var args = new EquatableArray<ArgInfo>(x.ParameterList.Parameters
                        .Select(p =>
                        {
                            var argName = p.Identifier.Text;
                            var modifiers = p.Modifiers.Select(m => m.Text).ToArray();
                            var argType = semanticModel.GetTypeInfo(p.Type).Type;
                            bool isNullable = p.Type.Kind() == SyntaxKind.NullableType;
                            return new ArgInfo(argName, argType, isNullable, modifiers);
                        }).ToArray());
                    return new TypeMethodInfo(name, returnType, orderId, false, args);
                }).ToArray());

            var events = new EquatableArray<TypeEventInfo>(syntaxNode.Members
                .OfType<EventFieldDeclarationSyntax>()
                .Select((x, i) =>
                {
                    var name = x.Declaration.Variables.First().Identifier.Text;
                    var type = semanticModel.GetTypeInfo(x.Declaration.Type);
                    int orderId = i;
                    var eventSymbol = semanticModel.GetDeclaredSymbol(x.Declaration.Variables.First()) as IEventSymbol;
                    var delegateType = eventSymbol?.Type as INamedTypeSymbol;
                    var retType = delegateType.DelegateInvokeMethod.ReturnType;
                    var args = delegateType?.DelegateInvokeMethod is null ? [] :
                        new EquatableArray<ArgInfo>(delegateType.DelegateInvokeMethod.Parameters
                        .Select(p =>
                        {
                            var argName = p.Name;
                            var argType = p.Type;
                            return new ArgInfo(argName, argType, false, []);
                        }).ToArray());
                    return new TypeEventInfo(name, type, orderId, retType, args);
                }).ToArray());

            return new RemoteInterfaceTypeInfo(typeNamespace, typeName, adapterName, proxyName, props, methods, events);
        }

        private bool HasAttribute(MemberDeclarationSyntax memberDeclarartion, SemanticModel model, string fullAttrName)
        {
            foreach (AttributeListSyntax attributeListSyntax in memberDeclarartion.AttributeLists)
            {
                foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
                {
                    var si = model.GetSymbolInfo(attributeSyntax);
                    var attributeSymbol = si.Symbol;
                    if (attributeSymbol == null)
                        continue;

                    INamedTypeSymbol attributeContainingTypeSymbol = attributeSymbol.ContainingType;
                    string fullName = attributeContainingTypeSymbol.ToDisplayString();

                    if (fullName == fullAttrName)
                        return true;
                }
            }

            return false;
        }

        private AttributeSyntax? GetAttribute(MemberDeclarationSyntax memberDeclarartion, SemanticModel model, string fullAttrName)
        {
            foreach (AttributeListSyntax attributeListSyntax in memberDeclarartion.AttributeLists)
            {
                foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
                {
                    var si = model.GetSymbolInfo(attributeSyntax);
                    var attributeSymbol = si.Symbol;
                    if (attributeSymbol == null)
                        continue;

                    INamedTypeSymbol attributeContainingTypeSymbol = attributeSymbol.ContainingType;
                    string fullName = attributeContainingTypeSymbol.ToDisplayString();

                    if (fullName == fullAttrName)
                        return attributeSyntax;
                }
            }

            return null;
        }

        private static string GetFullTypeName(TypeDeclarationSyntax typeDeclaration)
        {
            var typeName = typeDeclaration.Identifier.Text;

            var namespaceNode = typeDeclaration
                .Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();

            var namespaceName = namespaceNode != null ? GetFullNamespace(namespaceNode) : string.Empty;

            var enclosingTypes = typeDeclaration
                .Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .Select(c => c.Identifier.Text)
                .Reverse();

            var fullNameParts = new List<string>();
            if (!string.IsNullOrEmpty(namespaceName))
                fullNameParts.Add(namespaceName);

            fullNameParts.AddRange(enclosingTypes);
            fullNameParts.Add(typeName);

            return string.Join(".", fullNameParts);
        }

        private static string GetFullNamespace(BaseNamespaceDeclarationSyntax? namespaceNode)
        {
            var names = new List<string>();

            while (!(namespaceNode is null))
            {
                names.Insert(0, namespaceNode.Name.ToString());
                namespaceNode = namespaceNode.Parent as BaseNamespaceDeclarationSyntax;
            }

            return string.Join(".", names);
        }

        private static ITypeSymbol? GetTaskResultType(IMethodSymbol methodSymbol, Compilation compilation)
        {
            var genericTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");

            if (methodSymbol.ReturnType is INamedTypeSymbol namedReturnType &&
                SymbolEqualityComparer.Default.Equals(namedReturnType.ConstructedFrom, genericTaskType))
            {
                return namedReturnType.TypeArguments[0];
            }

            return null;
        }

        public static bool IsReturnTypeTask(IMethodSymbol methodSymbol, Compilation compilation)
        {
            var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
            return SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, taskType);
        }

        public static bool IsReturnTypeGenericTask(IMethodSymbol methodSymbol, Compilation compilation)
        {
            var genericTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
            return methodSymbol.ReturnType is INamedTypeSymbol namedReturn &&
                SymbolEqualityComparer.Default.Equals(namedReturn.ConstructedFrom, genericTaskType);
        }

        public static bool IsCancellationToken(IParameterSymbol parameter, Compilation compilation)
        {
            var cancellationTokenType = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
            return SymbolEqualityComparer.Default.Equals(parameter.Type, cancellationTokenType);
        }
    }
}

