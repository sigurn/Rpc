using System;
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
    readonly record struct ArgInfo(string Name, IParameterSymbol Symbol, string[] Modifiers);
    readonly record struct TypePropertyInfo(string Name, IPropertySymbol Symbol, int Id);
    readonly record struct TypeMethodInfo(string Name, IMethodSymbol Symbol, int Id, bool oneWay, EquatableArray<ArgInfo> Args);
    readonly record struct TypeEventInfo(string Name, IEventSymbol Symbol, int Id, ITypeSymbol ReturnType, EquatableArray<ArgInfo> Args);
    readonly record struct RemoteInterfaceTypeInfo(string TypeNamespace, string TypeName, string AdapterName, string ProxyName, EquatableArray<TypePropertyInfo> Properties, EquatableArray<TypeMethodInfo> Methods, EquatableArray<TypeEventInfo> Events, bool IsAuthenticated, EquatableArray<string> Permissions);

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
        private const string _requireAuthenticatedAttributeName = "Sigurn.Rpc.RequireAuthenticatedAttribute";
        private const string _requirePermissionsAttributeName = "Sigurn.Rpc.RequirePermissionsAttribute";
        private const string _noRpcTimeoutAttributeName = "Sigurn.Rpc.NoRpcTimeoutAttribute";
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
            // Aliased rather than imported: the trace enum is the only thing needed from
            // Sigurn.Rpc.Infrastructure here, and an alias cannot collide with user code.
            sb.Append($"using RpcTraceOp = Sigurn.Rpc.Infrastructure.RpcTraceOperation;\n");
            sb.Append("\n");
            sb.Append($"namespace {riti.TypeNamespace}.Rpc.Infrastructure;\n");
            sb.Append("\n");

            sb.Append(GetAdapterCode(fullTypeName, riti, context));
            sb.Append("\n");
            sb.Append(GetProxyCode(fullTypeName, riti, context));

            context.AddSource($"{riti.TypeName}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        // "Method13(string, string, System.Threading.CancellationToken)" — the id alone is a
        // declaration ordinal and says nothing in a log, so traces carry the real signature.
        private static string GetMethodSignature(TypeMethodInfo m)
        {
            var args = string.Join(", ", m.Args.Select(a =>
            {
                var modifiers = string.Join(" ", a.Modifiers);
                return string.IsNullOrEmpty(modifiers) ? $"{a.Symbol.Type}" : $"{modifiers} {a.Symbol.Type}";
            }));

            return $"{m.Name}({args})";
        }

        // Names shared by the trace call sites of one generated class.
        private static StringBuilder GetTraceMetadata(string fullTypeName, RemoteInterfaceTypeInfo riti)
        {
            var sb = new StringBuilder();

            sb.Append($"    private const string @__rpcInterfaceName = \"{fullTypeName}\";\n");
            sb.Append("    protected override string RpcInterfaceName => @__rpcInterfaceName;\n");
            sb.Append("\n");

            foreach (var p in riti.Properties)
                sb.Append($"    private const string @__property_{p.Id} = \"{p.Name}\";\n");

            foreach (var m in riti.Methods)
                sb.Append($"    private const string @__method_{m.Id} = \"{GetMethodSignature(m)}\";\n");

            foreach (var e in riti.Events)
                sb.Append($"    private const string @__event_{e.Id} = \"{e.Name}\";\n");

            sb.Append("\n");

            return sb;
        }

        /// <summary>
        /// Emits <paramref name="body"/> wrapped into entry/exit/failure tracing. The body is
        /// re-indented one level, so call sites keep building it at their own indentation.
        /// The failure branch is an exception filter: it logs during the first pass and returns
        /// false, so the exception is neither caught nor its stack disturbed.
        /// </summary>
        private static void AppendTraced(StringBuilder sb, string indent, string operation, string nameConstant, int memberId, StringBuilder body)
        {
            sb.Append($"{indent}if (IsTraceEnabled) TraceEnter(RpcTraceOp.{operation}, {nameConstant}, {memberId});\n");
            sb.Append($"{indent}try\n");
            sb.Append($"{indent}{{\n");

            var text = body.ToString();
            if (text.EndsWith("\n"))
                text = text.Substring(0, text.Length - 1);

            foreach (var line in text.Split('\n'))
                sb.Append(line.Length == 0 ? "\n" : $"    {line}\n");

            sb.Append($"{indent}}}\n");
            sb.Append($"{indent}catch (Exception @__ex) when (TraceFailure(@__ex, RpcTraceOp.{operation}, {nameConstant}, {memberId}))\n");
            sb.Append($"{indent}{{\n");
            sb.Append($"{indent}    throw;\n");
            sb.Append($"{indent}}}\n");
            sb.Append($"{indent}finally\n");
            sb.Append($"{indent}{{\n");
            sb.Append($"{indent}    if (IsTraceEnabled) TraceExit(RpcTraceOp.{operation}, {nameConstant}, {memberId});\n");
            sb.Append($"{indent}}}\n");
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

            sb.Append(GetTraceMetadata(fullTypeName, riti));

            var gsb = new StringBuilder();
            var ssb = new StringBuilder();
            if (riti.Properties.Count != 0)
            {
                gsb.Append("    public override async Task<byte[]?> GetPropertyValueAsync(int propertyId, CancellationToken cancellationToken)\n");
                gsb.Append("    {\n");

                ssb.Append("    public override async Task SetPropertyValueAsync(int propertyId, byte[]? value, CancellationToken cancellationToken)\n");
                ssb.Append("    {\n");

                if (riti.IsAuthenticated)
                {
                    gsb.Append("        CheckAuthenticated();\n\n");
                    ssb.Append("        CheckAuthenticated();\n\n");
                }

                if (riti.Permissions.Count != 0)
                {
                    gsb.Append("        CheckPermissions([ ");
                    gsb.Append(string.Join(", ", riti.Permissions));
                    gsb.Append(" ]);\n\n");

                    ssb.Append("        CheckPermissions([ ");
                    ssb.Append(string.Join(", ", riti.Permissions));
                    ssb.Append(" ]);\n\n");
                }

                bool firstGetter = true;
                bool firstSetter = true;
                foreach (var p in riti.Properties)
                {
                    var attr = p.Symbol.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == _requireAuthenticatedAttributeName);
                    bool propIsAuthenticated = attr is not null && !riti.IsAuthenticated;

                    var propPerm = GetPermissions(p.Symbol);
    
                    if (p.Symbol.GetMethod is not null)
                    {
                        var getAttr = p.Symbol.GetMethod.GetAttributes()
                            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == _requireAuthenticatedAttributeName);
                        bool getIsAuthenticated = (propIsAuthenticated | getAttr is not null) && !riti.IsAuthenticated;
                        var getPerm = propPerm.Concat(GetPermissions(p.Symbol.GetMethod))
                            .Distinct().Except(riti.Permissions).ToArray();

                        if (firstGetter)
                            gsb.Append($"        if (propertyId == {p.Id})\n");
                        else
                            gsb.Append($"        else if (propertyId == {p.Id})\n");
                        gsb.Append("        {\n");

                        var gbody = new StringBuilder();
                        if (getIsAuthenticated)
                            gbody.Append("            CheckAuthenticated();\n");
                        if (getPerm is not null && getPerm.Length > 0)
                        {
                            gbody.Append($"            CheckPermissions([ ");
                            gbody.Append(string.Join(", ", getPerm));
                            gbody.Append($" ]);\n");
                        }
                        gbody.Append($"            return await ToBytesAsync<{p.Symbol.Type}>(_instance.{p.Name}, cancellationToken).ConfigureAwait(false)");
                        if (p.Symbol.Type.IsReferenceType && p.Symbol.NullableAnnotation == NullableAnnotation.NotAnnotated)
                            gbody.Append(" ?? throw new InvalidOperationException(\"Property value cannot be null\")");
                        gbody.Append(";\n");

                        AppendTraced(gsb, "            ", "PropertyGet", $"@__property_{p.Id}", p.Id, gbody);

                        gsb.Append("        }\n");
                        firstGetter = false;
                    }

                    if (p.Symbol.SetMethod is not null)
                    {
                        var setAttr = p.Symbol.SetMethod.GetAttributes()
                            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == _requireAuthenticatedAttributeName);
                        bool setIsAuthenticated = (propIsAuthenticated | setAttr is not null) && !riti.IsAuthenticated;
                        var setPerm = propPerm.Concat(GetPermissions(p.Symbol.SetMethod))
                            .Distinct().Except(riti.Permissions).ToArray();

                        if (firstSetter)
                            ssb.Append($"        if (propertyId == {p.Id})\n");
                        else
                            ssb.Append($"        else if (propertyId == {p.Id})\n");
                        ssb.Append("        {\n");

                        var sbody = new StringBuilder();
                        if (setIsAuthenticated)
                            sbody.Append("            CheckAuthenticated();\n");
                        if (setPerm is not null && setPerm.Length > 0)
                        {
                            sbody.Append($"            CheckPermissions([ ");
                            sbody.Append(string.Join(", ", setPerm));
                            sbody.Append($" ]);\n");
                        }
                        sbody.Append($"            _instance.{p.Name} = await FromBytesAsync<{p.Symbol.Type}>(value, cancellationToken).ConfigureAwait(false)");
                        if (p.Symbol.Type.IsReferenceType && p.Symbol.NullableAnnotation == NullableAnnotation.NotAnnotated)
                            sbody.Append(" ?? throw new InvalidOperationException(\"Property value cannot be null\")");
                        sbody.Append(";\n");
                        sbody.Append("            return;\n");

                        AppendTraced(ssb, "            ", "PropertySet", $"@__property_{p.Id}", p.Id, sbody);

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

                if (riti.IsAuthenticated)
                    sb.Append("        CheckAuthenticated();\n\n");

                if (riti.Permissions.Count != 0)
                {
                    sb.Append("        CheckPermissions([ ");
                    sb.Append(string.Join(", ", riti.Permissions));
                    sb.Append(" ]);\n\n");
                }

                bool firstMethod = true;
                foreach (var m in riti.Methods)
                {
                    var attr = m.Symbol.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == _requireAuthenticatedAttributeName);
                    bool methodIsAuthenticated = attr is not null && !riti.IsAuthenticated;
                    var methodPerm = GetPermissions(m.Symbol)
                        .Distinct().Except(riti.Permissions).ToArray();

                    if (firstMethod)
                        sb.Append($"        if (methodId == {m.Id})\n");
                    else
                        sb.Append($"        else if (methodId == {m.Id})\n");
                    sb.Append("        {\n");

                    var mbody = new StringBuilder();

                    if (methodIsAuthenticated)
                        mbody.Append("            CheckAuthenticated();\n\n");
                    if (methodPerm is not null && methodPerm.Length > 0)
                    {
                        mbody.Append($"            CheckPermissions([ ");
                        mbody.Append(string.Join(", ", methodPerm));
                        mbody.Append($" ]);\n");
                    }

                    // An out parameter has no value on the way in, so the proxy does not send one and
                    // the adapter must not expect one. It still comes back through the out args.
                    var count = m.Args.Count(x => x.Symbol.Type.ToString() != _cancellationTokenName
                        && !x.Modifiers.Contains("out"));
                    if (count != 0)
                    {
                        mbody.Append($"            if (args is null || args.Count != {count})\n");
                        mbody.Append("                throw new ArgumentException(\"Invalid number of arguments\");\n");
                        mbody.Append("\n");
                    }

                    var args = string.Empty;
                    int n = 0;
                    bool outArgs = false;
                    foreach (var a in m.Args)
                    {
                        var argName = $"@{a.Name}";
                        if (a.Symbol.Type.ToString() == _cancellationTokenName)
                        {
                            argName = "cancellationToken";
                        }
                        else if (a.Modifiers.Contains("out"))
                        {
                            // Declared, not read: the call below is what assigns it.
                            mbody.Append($"            {a.Symbol.Type} {argName};\n");
                        }
                        else
                        {
                            mbody.Append($"            var {argName} = await FromBytesAsync<{a.Symbol.Type}>(args[{n++}], cancellationToken).ConfigureAwait(false)");
                            if (a.Symbol.Type.IsReferenceType && a.Symbol.NullableAnnotation == NullableAnnotation.NotAnnotated)
                                mbody.Append($" ?? throw new ArgumentNullException(\"{a.Symbol.Name}\")");
                            mbody.Append(";\n");
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

                    if (m.Symbol.ReturnType.ToString() == "void")
                    {
                        mbody.Append($"            _instance.{m.Name}({args});\n");
                        if (outArgs)
                        {
                            var outArgsString = string.Join(", ", m.Args
                                .Where(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out"))
                                .Select(x => $"await ToBytesAsync<{x.Symbol.Type}>(@{x.Symbol.Name}, cancellationToken).ConfigureAwait(false)"));
                            mbody.Append($"            return (Result: null, [{outArgsString}]);\n");
                        }
                    }
                    else if (m.Symbol.ReturnType.ToString() == _taskName)
                    {
                        mbody.Append($"            await _instance.{m.Name}({args}).ConfigureAwait(false);\n");
                    }
                    else if (m.Symbol.ReturnType is INamedTypeSymbol nts &&
                        nts.IsGenericType && nts.ConstructedFrom.ToString() == _genericTaskName)
                    {
                        mbody.Append($"            var @__res = await _instance.{m.Name}({args}).ConfigureAwait(false);\n");
                        mbody.Append($"            return (Result: await ToBytesAsync<{nts.TypeArguments[0]}>(@__res, cancellationToken).ConfigureAwait(false), null);\n");
                    }
                    else
                    {
                        mbody.Append($"            {m.Symbol.ReturnType} @__res = _instance.{m.Name}({args});\n");
                        if (outArgs)
                        {
                            var outArgsString = string.Join(", ", m.Args
                                .Where(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out"))
                                .Select(x => $"await ToBytesAsync<{x.Symbol.Type}>(@{x.Symbol.Name}, cancellationToken).ConfigureAwait(false)"));
                            mbody.Append($"            return (Result: await ToBytesAsync<{m.Symbol.ReturnType}>(@__res, cancellationToken).ConfigureAwait(false), [{outArgsString}]);\n");
                        }
                        else
                        {
                            mbody.Append($"            return (Result: await ToBytesAsync<{m.Symbol.ReturnType}>(@__res, cancellationToken).ConfigureAwait(false), null);\n");
                        }
                    }

                    AppendTraced(sb, "            ", "MethodCall", $"@__method_{m.Id}", m.Id, mbody);

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

                if (riti.IsAuthenticated)
                {
                    aesb.Append("            CheckAuthenticated();\n\n");
                    desb.Append("            CheckAuthenticated();\n\n");
                }

                if (riti.Permissions.Count != 0)
                {
                    aesb.Append("            CheckPermissions([ ");
                    aesb.Append(string.Join(", ", riti.Permissions));
                    aesb.Append(" ]);\n\n");

                    desb.Append("            CheckPermissions([ ");
                    desb.Append(string.Join(", ", riti.Permissions));
                    desb.Append(" ]);\n\n");
                }

                bool firstEvent = true;
                foreach (var e in riti.Events)
                {
                    var attr = e.Symbol.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == _requireAuthenticatedAttributeName);
                    bool eventIsAuthenticated = attr is not null && !riti.IsAuthenticated;
                    var eventPerm = GetPermissions(e.Symbol)
                        .Distinct().Except(riti.Permissions).ToArray();

                    var args = string.Join(", ", e.Args.Select(x => $"{x.Symbol.Type} {x.Name}"));

                    ehsb.Append("\n");
                    ehsb.Append($"    private void On{e.Name}({args})\n");
                    ehsb.Append("    {\n");
                    var eventArgs = e.Args
                        .Where(x => x.Name != "sender" && x.Symbol.Type.ToString() != "object?")
                        .ToArray();
                    if (eventArgs.Length != 0)
                    {
                        var rbody = new StringBuilder();
                        rbody.Append($"        SendEvent({e.Id}");
                        foreach (var ea in eventArgs)
                            rbody.Append($", ToBytes<{ea.Symbol.Type}>({ea.Name})");
                        rbody.Append(");\n");

                        AppendTraced(ehsb, "        ", "EventRaise", $"@__event_{e.Id}", e.Id, rbody);
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
                    aesb.Append("            {\n");
                    desb.Append("            {\n");

                    var abody = new StringBuilder();
                    var dbody = new StringBuilder();
                    if (eventIsAuthenticated)
                    {
                        abody.Append("                CheckAuthenticated();\n");
                        dbody.Append("                CheckAuthenticated();\n");
                    }
                    if (eventPerm is not null && eventPerm.Length > 0)
                    {
                        abody.Append($"                CheckPermissions([ ");
                        abody.Append(string.Join(", ", eventPerm));
                        abody.Append($" ]);\n");

                        dbody.Append($"                CheckPermissions([ ");
                        dbody.Append(string.Join(", ", eventPerm));
                        dbody.Append($" ]);\n");
                    }

                    abody.Append($"                _instance.{e.Name} += On{e.Name};\n");
                    dbody.Append($"                _instance.{e.Name} -= On{e.Name};\n");

                    AppendTraced(aesb, "                ", "EventAttach", $"@__event_{e.Id}", e.Id, abody);
                    AppendTraced(desb, "                ", "EventDetach", $"@__event_{e.Id}", e.Id, dbody);

                    aesb.Append("            }\n");
                    desb.Append("            }\n");
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

            sb.Append(GetTraceMetadata(fullTypeName, riti));

            if (riti.Properties.Count != 0)
            {
                foreach (var p in riti.Properties)
                {
                    sb.Append($"    {p.Symbol.Type}");
                    sb.Append($" {fullTypeName}.{p.Name}\n");
                    sb.Append("    {\n");
                    if (p.Symbol.GetMethod is not null)
                    {
                        var gbody = new StringBuilder();
                        gbody.Append($"            return GetProperty<{p.Symbol.Type}>({p.Id})");
                        if (p.Symbol.Type.IsReferenceType && p.Symbol.NullableAnnotation == NullableAnnotation.NotAnnotated)
                        // if (p.Symbol.NullableAnnotation == NullableAnnotation.NotAnnotated)
                            gbody.Append(" ?? throw new InvalidOperationException(\"Property value cannot be null\")");
                        gbody.Append(";\n");

                        sb.Append("        get\n");
                        sb.Append("        {\n");
                        AppendTraced(sb, "            ", "PropertyGet", $"@__property_{p.Id}", p.Id, gbody);
                        sb.Append("        }\n");
                    }

                    if (p.Symbol.SetMethod is not null)
                    {
                        var sbody = new StringBuilder();
                        sbody.Append($"            SetProperty<{p.Symbol.Type}>({p.Id}, value);\n");

                        sb.Append("        set\n");
                        sb.Append("        {\n");
                        AppendTraced(sb, "            ", "PropertySet", $"@__property_{p.Id}", p.Id, sbody);
                        sb.Append("        }\n");
                    }
                    sb.Append("    }\n");
                    sb.Append("\n");
                }
            }

            if (riti.Methods.Count != 0)
            {
                foreach (var m in riti.Methods)
                {
                    {
                        bool isAsync = m.Symbol.ReturnType.ToString() == _taskName ||
                            (m.Symbol.ReturnType is INamedTypeSymbol nts &&
                            nts.IsGenericType && nts.ConstructedFrom.ToString() == _genericTaskName);
                        if (isAsync)
                            sb.Append($"    async {m.Symbol.ReturnType} {fullTypeName}.{m.Name}(");
                        else
                            sb.Append($"    {m.Symbol.ReturnType} {fullTypeName}.{m.Name}(");
                    }
                    sb.Append(string.Join(", ", m.Args.Select(a =>
                    {
                        var modifiers = string.Join(" ", a.Modifiers);
                        return string.IsNullOrEmpty(modifiers) ? $"{a.Symbol.Type} {a.Symbol.Name}" : $"{modifiers} {a.Symbol.Type} {a.Symbol.Name}";
                    })));
                    sb.Append(")\n");
                    sb.Append("    {\n");
                    bool noTimeout = m.Symbol.GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == _noRpcTimeoutAttributeName);
                    if (noTimeout)
                        sb.Append("        using var @_noTimeout = new Sigurn.Rpc.RpcContext { Timeout = System.Threading.Timeout.InfiniteTimeSpan };\n");
                    var pbody = new StringBuilder();
                    bool args = false;
                    // Includes out parameters even though they are not sent: they still come back.
                    bool outArgs = m.Args.Any(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out"));
                    string? cancellationToken = m.Args
                        .Where(x => x.Symbol.Type.ToString() == _cancellationTokenName)
                        .Select(x => x.Name)
                        .FirstOrDefault() ?? $"{_cancellationTokenName}.None";
                    // An out parameter is unassigned at this point, so there is nothing to serialize
                    // and nothing the server could use — it is left out of the request.
                    var realArgs = m.Args
                        .Where(x => x.Symbol.Type.ToString() != _cancellationTokenName
                            && !x.Modifiers.Contains("out"))
                        .ToArray();
                    if (realArgs.Any())
                    {
                        pbody.Append("        IReadOnlyList<byte[]> @args =\n");
                        pbody.Append("        [\n");

                        foreach (var a in realArgs)
                            pbody.Append($"            ToBytes<{a.Symbol.Type}>({a.Symbol.Name}),\n");

                        pbody.Append("        ];\n");
                        pbody.Append("\n");
                        args = true;
                    }
                    var argsText = args ? "@args" : "[]";
                    if (m.Symbol.ReturnType.ToString() == "void")
                    {
                        var resText = outArgs ? "var (_, @outArgs) = " : "";
                        bool oneWay = !outArgs && m.oneWay;
                        pbody.Append($"        {resText}InvokeMethod({m.Id}, {argsText}, {oneWay.ToString().ToLower()});\n");

                        if (outArgs)
                        {
                            var an = 0;
                            foreach (var oa in m.Args.Where(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out")))
                                pbody.Append($"        {oa.Name} = FromBytes<{oa.Symbol.Type}>(@outArgs[{an++}]);\n");
                        }
                    }
                    else if (m.Symbol.ReturnType.ToString() == _taskName)
                    {
                        var resText = outArgs ? "var (_, @outArgs) = " : "";
                        bool oneWay = !outArgs && m.oneWay;

                        pbody.Append($"        {resText} await InvokeMethodAsync({m.Id}, {argsText}, {oneWay.ToString().ToLower()}, {cancellationToken}).ConfigureAwait(false);\n");

                        if (outArgs)
                        {
                            var an = 0;
                            foreach (var oa in m.Args.Where(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out")))
                            {
                                pbody.Append($"        {oa.Name} = await FromBytesAsync<{oa.Symbol.Type}>(@outArgs[{an++}], {cancellationToken}).ConfigureAwait(false)");
                                if (oa.Symbol.Type.IsReferenceType && oa.Symbol.Type.NullableAnnotation == NullableAnnotation.NotAnnotated)
                                    pbody.Append($" ?? throw new InvalidOperationException(\"Output argument '{oa.Symbol.Name}' value cannot be null\")");
                                pbody.Append(";\n");
                            }
                        }
                    }
                    else if (m.Symbol.ReturnType is INamedTypeSymbol nts &&
                        nts.IsGenericType && nts.ConstructedFrom.ToString() == _genericTaskName)
                    {
                        pbody.Append($"        var (@res, _) = await InvokeMethodAsync({m.Id}, {argsText}, false, {cancellationToken}).ConfigureAwait(false);\n");
                        pbody.Append($"        return await FromBytesAsync<{nts.TypeArguments[0]}>(@res, {cancellationToken}).ConfigureAwait(false)");
                        if (nts.TypeArguments[0].IsReferenceType && nts.TypeArguments[0].NullableAnnotation == NullableAnnotation.NotAnnotated)
                            pbody.Append(" ?? throw new InvalidOperationException(\"Method return value cannot be null.\")");
                        pbody.Append(";\n");
                    }
                    else
                    {
                        var resText = outArgs ? "(@res, @outArgs)" : "(res, _)";
                        pbody.Append($"        var {resText} = InvokeMethod({m.Id}, {argsText}, false);\n");
                        pbody.Append("\n");
                        if (outArgs)
                        {
                            var an = 0;
                            foreach (var oa in m.Args.Where(x => x.Modifiers.Contains("ref") || x.Modifiers.Contains("out")))
                            {
                                pbody.Append($"        {oa.Name} = FromBytes<{oa.Symbol.Type}>(@outArgs[{an++}])");
                                if (oa.Symbol.Type.IsReferenceType && oa.Symbol.NullableAnnotation == NullableAnnotation.NotAnnotated)
                                    pbody.Append($" ?? throw new InvalidOperationException(\"Output value for argument '{oa.Symbol.Name}' cannot be null.\")");
                                pbody.Append(";\n");
                            }
                            pbody.Append("\n");
                        }

                        pbody.Append($"        return FromBytes<{m.Symbol.ReturnType}>(@res)");
                        if (m.Symbol.ReturnType.IsReferenceType && m.Symbol.ReturnNullableAnnotation == NullableAnnotation.NotAnnotated)
                            pbody.Append(" ?? throw new InvalidOperationException(\"Method return value cannot be null.\")");
                        pbody.Append(";\n");
                    }


                    AppendTraced(sb, "        ", "MethodCall", $"@__method_{m.Id}", m.Id, pbody);

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
                    sb.Append($"    private {e.Symbol.Type}? _{e.Name};\n");
                    sb.Append($"    event {e.Symbol.Type} {fullTypeName}.{e.Name}\n");
                    sb.Append("    {\n");
                    var abody = new StringBuilder();
                    abody.Append($"            _{e.Name} += value;\n");
                    abody.Append($"            AttachEventHandler({e.Id});\n");

                    var dbody = new StringBuilder();
                    dbody.Append($"            _{e.Name} -= value;\n");
                    dbody.Append($"            DetachEventHandler({e.Id});\n");

                    sb.Append($"        add\n");
                    sb.Append("        {\n");
                    AppendTraced(sb, "            ", "EventAttach", $"@__event_{e.Id}", e.Id, abody);
                    sb.Append("        }\n");
                    sb.Append($"        remove\n");
                    sb.Append("        {\n");
                    AppendTraced(sb, "            ", "EventDetach", $"@__event_{e.Id}", e.Id, dbody);
                    sb.Append("        }\n");
                    sb.Append("    }\n");
                    sb.Append("\n");

                    if (firstEvent)
                        ehsp.Append($"        if (eventId == {e.Id})\n");
                    else
                        ehsp.Append($"        else if (eventId == {e.Id})\n");
                    ehsp.Append("        {\n");

                    var rbody = new StringBuilder();
                    rbody.Append($"            _{e.Name}?.Invoke(");
                    int an = 0;
                    bool firstArg = true;
                    foreach (var a in e.Args)
                    {
                        if (!firstArg)
                            rbody.Append(", ");
                        if (a.Symbol.Type.ToString().StartsWith("object") && a.Name == "sender")
                            rbody.Append("this");
                        else
                        {
                            rbody.Append($"FromBytes<{a.Symbol.Type}>(args[{an}])");
                            if (a.Symbol.Type.IsReferenceType && a.Symbol.NullableAnnotation == NullableAnnotation.NotAnnotated)
                                rbody.Append($" ?? throw new ArgumentNullException(\"{a.Symbol.Name}\")");
                            an++;
                        }
                        firstArg = false;
                    }
                    rbody.Append(");\n");

                    AppendTraced(ehsp, "            ", "EventRaise", $"@__event_{e.Id}", e.Id, rbody);

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

            var authenticatedAttr = GetAttribute(syntaxNode, semanticModel, _requireAuthenticatedAttributeName);
            var permissionAttrs = GetAttributes(syntaxNode, semanticModel, _requirePermissionsAttributeName).ToArray();
            var format = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.None);
            EquatableArray<string> interfacePermissions = new ([.. permissionAttrs
                .SelectMany(x => x.ArgumentList is not null ? x.ArgumentList.Arguments : [])
                .Select(x =>
                {
                    var symbol = semanticModel.GetSymbolInfo(x.Expression).Symbol!;
                    if (symbol is IFieldSymbol fs)
                        return $"{fs.ContainingType.ToDisplayString(format)}.{fs.Name}";
                    return symbol.ToDisplayString(format);
                })
                .Distinct()]);

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

                    return true;
                });

            var props = new EquatableArray<TypePropertyInfo>(publicProps.Select((p, i) =>
            {
                var name = p.Identifier.Text;
                var symbol = semanticModel.GetDeclaredSymbol(p) ?? throw new InvalidOperationException("Cannot get proprty symbol");
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

                return new TypePropertyInfo(name, symbol, orderId);
            }).ToArray());

            var methods = new EquatableArray<TypeMethodInfo>(syntaxNode.Members
                .OfType<MethodDeclarationSyntax>()
                .Select((x, i) =>
                {
                    var name = x.Identifier.Text;
                    var returnType = semanticModel.GetTypeInfo(x.ReturnType);
                    var symbol = semanticModel.GetDeclaredSymbol(x);
                    if (symbol is null)
                        throw new InvalidOperationException($"Cannot get symbol for '{x.Identifier.Text}' method.");
                    int orderId = i;
                    var args = new EquatableArray<ArgInfo>(x.ParameterList.Parameters
                        .Select(p =>
                        {
                            var argName = p.Identifier.Text;
                            var modifiers = p.Modifiers.Select(m => m.Text).ToArray();
                            if (p.Type is null)
                                throw new NullReferenceException("Method argument type cannot be null");
                            var argSymbol = semanticModel.GetDeclaredSymbol(p);
                            if (argSymbol is null)
                                throw new InvalidOperationException($"Cannot get symbol for method argument '{p.Identifier.Text}'");
                            return new ArgInfo(argName, argSymbol, modifiers);
                        }).ToArray());
                    return new TypeMethodInfo(name, symbol, orderId, false, args);
                }).ToArray());

            var events = new EquatableArray<TypeEventInfo>(syntaxNode.Members
                .OfType<EventFieldDeclarationSyntax>()
                .Select((x, i) =>
                {
                    var name = x.Declaration.Variables.First().Identifier.Text;
                    var symbol = (IEventSymbol?)semanticModel.GetDeclaredSymbol(x.Declaration.Variables.First());
                    if (symbol is null)
                        throw new InvalidOperationException("Cannot get symbol for event declaration");
                    int orderId = i;
                    var delegateType = (INamedTypeSymbol)symbol.Type;
                    if (delegateType.DelegateInvokeMethod is null)
                        throw new InvalidOperationException("Cannot get information about event filed delegate");
                    var retType = delegateType.DelegateInvokeMethod?.ReturnType ?? throw new NullReferenceException("Event return type cannot be null");
                    var args = delegateType.DelegateInvokeMethod is null ? [] :
                        new EquatableArray<ArgInfo>(delegateType.DelegateInvokeMethod.Parameters
                        .Select(p => new ArgInfo(p.Name, p, [])).ToArray());
                    return new TypeEventInfo(name, symbol, orderId, retType, args);
                }).ToArray());

            return new RemoteInterfaceTypeInfo(typeNamespace, typeName, adapterName, proxyName, props, methods, events, authenticatedAttr is not null, interfacePermissions);
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

        private IEnumerable<AttributeSyntax> GetAttributes(MemberDeclarationSyntax memberDeclarartion, SemanticModel model, string fullAttrName)
        {
            foreach (AttributeListSyntax attributeListSyntax in memberDeclarartion.AttributeLists)
            {
                foreach (AttributeSyntax attributeSyntax in attributeListSyntax.Attributes)
                {
                    var si = model.GetSymbolInfo(attributeSyntax);
                    var attributeSymbol = si.Symbol;
                    if (attributeSymbol == null)
                        continue;

                    var format = new SymbolDisplayFormat(
                        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                        genericsOptions: SymbolDisplayGenericsOptions.None);
                    INamedTypeSymbol attributeContainingTypeSymbol = attributeSymbol.ContainingType;
                    string fullName = attributeContainingTypeSymbol.IsGenericType ?
                        attributeContainingTypeSymbol.ConstructedFrom.ToDisplayString(format) : 
                        attributeContainingTypeSymbol.ToDisplayString();

                    if (fullName == fullAttrName)
                        yield return attributeSyntax;
                }
            }
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

        private static string[] GetPermissions(ISymbol symbol)
        {
            var attrs = symbol.GetAttributes()
                .Where(x => x?.AttributeClass?.IsGenericType ?? false)
                .Where(x => $"{x.AttributeClass?.ContainingNamespace.ToDisplayString()}.{x.AttributeClass?.Name}" == _requirePermissionsAttributeName)
                .ToArray();
            
            return [.. attrs.SelectMany(x => {
                return x.ConstructorArguments.First().Values
                    .Select(v =>
                    {
                        var field = ((INamedTypeSymbol)v.Type!).GetMembers()
                            .OfType<IFieldSymbol>()
                            .First(f => Equals(f.ConstantValue, v.Value));
                        return $"{field.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}.{field.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}";
                    });
            })
            .Distinct()];
        }
    }
}

