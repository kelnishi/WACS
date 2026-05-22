// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Wacs.ComponentModel.Async.SourceGen
{
    /// <summary>
    /// Scans the current compilation for partial classes marked
    /// <c>[AsyncComponentHarness]</c> and emits the missing
    /// constructor + the implementation of each
    /// <c>[AsyncExport("export-name")]</c>-marked partial method.
    /// Wires every emitted method through
    /// <c>ComponentInstance.InstantiateAot</c> +
    /// <c>InvokeCoreAsyncLift</c> so the consumer's code stays
    /// on the AOT-safe primitive surface (never reaches the
    /// reflective typed-bridge in <c>ComponentInstance.Invoke</c>).
    ///
    /// <para>Companion to
    /// <see cref="CanonOpRegistryGenerator"/> /
    /// <see cref="ComponentLifterRegistryGenerator"/>: each
    /// trigger is independent. This generator runs in every
    /// consumer assembly that declares an
    /// <c>[AsyncComponentHarness]</c> class.</para>
    ///
    /// <para><b>Spec:</b> wit-component's lift naming convention
    /// has stabilized on <c>[async-lift]&lt;qualified-export&gt;</c>
    /// for async-lifted exports; sync exports use the plain export
    /// name. The <c>[AsyncExport]</c> string is opaque to the
    /// generator — we just emit it as the
    /// <c>InvokeCoreAsyncLift</c> argument.</para>
    /// </summary>
    [Generator]
    public sealed class AsyncComponentHarnessGenerator
        : IIncrementalGenerator
    {
        private const string HarnessAttributeFqn =
            "Wacs.ComponentModel.Async.AsyncComponentHarnessAttribute";
        private const string ExportAttributeFqn =
            "Wacs.ComponentModel.Async.AsyncExportAttribute";
        private const string SyncExportAttributeFqn =
            "Wacs.ComponentModel.Async.SyncExportAttribute";
        private const string ComponentInstanceFqn =
            "Wacs.ComponentModel.Runtime.ComponentInstance";

        // Diagnostic descriptors. Surface generator-level
        // misuse to the consumer at build time with clear,
        // actionable messages — the alternative (the generator
        // silently skipping or emitting code that fails to
        // compile downstream) is hostile to users debugging
        // attribute placement.
        private static readonly DiagnosticDescriptor
            HarnessClassMustBePartial = new(
                id: "WACSCM001",
                title: "AsyncComponentHarness class must be partial",
                messageFormat:
                    "[AsyncComponentHarness] is applied to " +
                    "'{0}' but the class isn't declared partial. " +
                    "Add the 'partial' keyword so the generator " +
                    "can emit the constructor + InvokeCoreAsyncLift " +
                    "wiring as a partial-class body.",
                category: "Wacs.ComponentModel.Async.SourceGen",
                defaultSeverity: DiagnosticSeverity.Error,
                isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor
            ExportMethodMustBePartial = new(
                id: "WACSCM002",
                title: "AsyncExport method must be a partial definition",
                messageFormat:
                    "[AsyncExport] is applied to '{0}' on '{1}' " +
                    "but the method isn't a partial definition. " +
                    "Declare it as `partial` so the generator can " +
                    "emit the body.",
                category: "Wacs.ComponentModel.Async.SourceGen",
                defaultSeverity: DiagnosticSeverity.Error,
                isEnabledByDefault: true);

        public void Initialize(
            IncrementalGeneratorInitializationContext context)
        {
            var scanResults = context.CompilationProvider.Select(
                static (compilation, _) =>
                {
                    var attr = compilation.GetTypeByMetadataName(
                        HarnessAttributeFqn);
                    if (attr == null)
                        return default(ScanResult);
                    return CollectAndDiagnose(compilation);
                });

            context.RegisterSourceOutput(scanResults,
                static (spc, scan) =>
                {
                    if (scan.IsDefault) return;
                    foreach (var diag in scan.Diagnostics)
                        spc.ReportDiagnostic(diag);
                    foreach (var cls in scan.Classes)
                        spc.AddSource(
                            cls.GeneratedFileName,
                            EmitHarness(cls));
                });
        }

        private readonly struct ScanResult
        {
            public ImmutableArray<HarnessClass> Classes { get; }
            public ImmutableArray<Diagnostic> Diagnostics { get; }
            public bool IsDefault =>
                Classes.IsDefault && Diagnostics.IsDefault;
            public ScanResult(
                ImmutableArray<HarnessClass> classes,
                ImmutableArray<Diagnostic> diagnostics)
            {
                Classes = classes;
                Diagnostics = diagnostics;
            }
        }

        private readonly struct HarnessClass
        {
            public string Namespace { get; }
            public string ClassName { get; }
            public string Accessibility { get; }
            public ImmutableArray<ExportMethod> Exports { get; }
            public string GeneratedFileName { get; }
            public HarnessClass(
                string ns, string className, string accessibility,
                ImmutableArray<ExportMethod> exports)
            {
                Namespace = ns;
                ClassName = className;
                Accessibility = accessibility;
                Exports = exports;
                GeneratedFileName =
                    (string.IsNullOrEmpty(ns) ? "" : ns + ".")
                    + className + ".Harness.g.cs";
            }
        }

        private enum ExportKind { Async, Sync }

        private readonly struct ExportMethod
        {
            public string MethodName { get; }
            public string ExportName { get; }
            public string Accessibility { get; }
            public ImmutableArray<ExportParam> Parameters { get; }
            public string? ReturnType { get; }
            public ExportKind Kind { get; }
            public ExportMethod(
                string methodName, string exportName,
                string accessibility,
                ImmutableArray<ExportParam> parameters,
                string? returnType,
                ExportKind kind)
            {
                MethodName = methodName;
                ExportName = exportName;
                Accessibility = accessibility;
                Parameters = parameters;
                ReturnType = returnType;
                Kind = kind;
            }
        }

        private readonly struct ExportParam
        {
            public string Name { get; }
            public string Type { get; }
            public ExportParam(string name, string type)
            {
                Name = name; Type = type;
            }
        }

        // Canon-ABI primitive types — pass straight through
        // the canon-async lift adapter (boxed for object[]
        // calls; statically-known for sync CreateInvokerFunc).
        private static readonly HashSet<string> PrimitiveTypes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "int", "uint", "long", "ulong",
                "byte", "sbyte", "short", "ushort",
                "bool", "float", "double",
                "System.Int32", "System.UInt32",
                "System.Int64", "System.UInt64",
                "System.Byte", "System.SByte",
                "System.Int16", "System.UInt16",
                "System.Boolean", "System.Single",
                "System.Double",
            };

        // Aggregate types we know how to marshal at the
        // generated-code layer via Wacs.ComponentModel.Harness
        // helpers (StringCoding + MemoryHelpers).
        private const string StringType = "string";
        private const string StringTypeFq = "System.String";

        private static bool IsPrimitive(string fqType) =>
            PrimitiveTypes.Contains(fqType);

        private static bool IsString(string fqType) =>
            fqType == StringType || fqType == StringTypeFq;

        private static bool IsSupportedParam(string fqType) =>
            IsPrimitive(fqType) || IsString(fqType);

        private static bool IsSupportedReturn(string? fqType) =>
            fqType == null
            || IsPrimitive(fqType) || IsString(fqType);

        private static ScanResult CollectAndDiagnose(
            Compilation compilation)
        {
            var harnessAttr = compilation.GetTypeByMetadataName(
                HarnessAttributeFqn);
            var exportAttr = compilation.GetTypeByMetadataName(
                ExportAttributeFqn);
            var syncExportAttr =
                compilation.GetTypeByMetadataName(
                    SyncExportAttributeFqn);
            if (harnessAttr == null || exportAttr == null)
                return new ScanResult(
                    ImmutableArray<HarnessClass>.Empty,
                    ImmutableArray<Diagnostic>.Empty);

            var entries = ImmutableArray.CreateBuilder<HarnessClass>();
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

            foreach (var type in EnumerateAllTypes(
                compilation.Assembly.GlobalNamespace))
            {
                if (!HasAttribute(type, harnessAttr)) continue;

                // Diagnostic: class must be declared `partial`
                // so the emitted body can compile alongside the
                // user's declaration. Roslyn surfaces this via
                // each declaration's syntax — if any declaration
                // is missing `partial` we error.
                bool anyNonPartial = false;
                foreach (var declRef in type.DeclaringSyntaxReferences)
                {
                    var node = declRef.GetSyntax();
                    if (node is Microsoft.CodeAnalysis.CSharp.Syntax
                        .ClassDeclarationSyntax cls)
                    {
                        if (!cls.Modifiers.Any(m =>
                            m.IsKind(Microsoft.CodeAnalysis.CSharp
                                .SyntaxKind.PartialKeyword)))
                        {
                            anyNonPartial = true;
                            diagnostics.Add(Diagnostic.Create(
                                HarnessClassMustBePartial,
                                cls.Identifier.GetLocation(),
                                type.ToDisplayString()));
                        }
                    }
                }
                if (anyNonPartial) continue;

                var exports = ImmutableArray.CreateBuilder<ExportMethod>();
                foreach (var member in type.GetMembers())
                {
                    if (member is not IMethodSymbol method) continue;
                    ExportKind kind = ExportKind.Async;
                    string? exportName = null;
                    bool found = false;
                    foreach (var attr in method.GetAttributes())
                    {
                        bool isAsync = SymbolEqualityComparer.Default
                            .Equals(attr.AttributeClass, exportAttr);
                        bool isSync = syncExportAttr != null
                            && SymbolEqualityComparer.Default.Equals(
                                attr.AttributeClass, syncExportAttr);
                        if (!isAsync && !isSync) continue;
                        found = true;
                        kind = isSync
                            ? ExportKind.Sync : ExportKind.Async;
                        if (attr.ConstructorArguments.Length > 0
                            && attr.ConstructorArguments[0].Value
                                is string en
                            && !string.IsNullOrEmpty(en))
                        {
                            exportName = en;
                        }
                        break;
                    }
                    if (!found) continue;
                    if (exportName == null) continue;

                    // Diagnostic: method must be a partial
                    // definition so the generator can emit the
                    // body. Non-partial methods already have a
                    // user-provided body that the generator
                    // would conflict with.
                    if (!method.IsPartialDefinition)
                    {
                        var loc = method.Locations.Length > 0
                            ? method.Locations[0] : Location.None;
                        diagnostics.Add(Diagnostic.Create(
                            ExportMethodMustBePartial, loc,
                            method.Name,
                            type.ToDisplayString()));
                        continue;
                    }

                    var parameters = method.Parameters
                        .Select(p => new ExportParam(
                            p.Name,
                            p.Type.ToDisplayString(
                                SymbolDisplayFormat.FullyQualifiedFormat)))
                        .ToImmutableArray();
                    string? returnType = method.ReturnsVoid
                        ? null
                        : method.ReturnType.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat);

                    exports.Add(new ExportMethod(
                        method.Name, exportName,
                        AccessibilityKeyword(method.DeclaredAccessibility),
                        parameters, returnType, kind));
                }

                string ns = type.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : type.ContainingNamespace.ToDisplayString();
                entries.Add(new HarnessClass(
                    ns, type.Name,
                    AccessibilityKeyword(type.DeclaredAccessibility),
                    exports.ToImmutable()));
            }

            return new ScanResult(
                entries
                    .OrderBy(e => e.GeneratedFileName,
                        StringComparer.Ordinal)
                    .ToImmutableArray(),
                diagnostics.ToImmutable());
        }

        private static bool HasAttribute(
            INamedTypeSymbol type, INamedTypeSymbol attrSymbol)
        {
            foreach (var attr in type.GetAttributes())
                if (SymbolEqualityComparer.Default.Equals(
                        attr.AttributeClass, attrSymbol))
                    return true;
            return false;
        }

        private static string AccessibilityKeyword(
            Accessibility access) => access switch
            {
                Accessibility.Public => "public",
                Accessibility.Internal => "internal",
                Accessibility.Private => "private",
                Accessibility.Protected => "protected",
                Accessibility.ProtectedAndInternal =>
                    "private protected",
                Accessibility.ProtectedOrInternal =>
                    "protected internal",
                _ => "internal",
            };

        private static IEnumerable<INamedTypeSymbol> EnumerateAllTypes(
            INamespaceSymbol ns)
        {
            foreach (var t in ns.GetTypeMembers())
            {
                yield return t;
                foreach (var nested in EnumerateNested(t))
                    yield return nested;
            }
            foreach (var sub in ns.GetNamespaceMembers())
                foreach (var t in EnumerateAllTypes(sub))
                    yield return t;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateNested(
            INamedTypeSymbol type)
        {
            foreach (var nested in type.GetTypeMembers())
            {
                yield return nested;
                foreach (var grandchild in EnumerateNested(nested))
                    yield return grandchild;
            }
        }

        private static string EmitHarness(HarnessClass cls)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Source generator: " +
                "Wacs.ComponentModel.Async.SourceGen" +
                ".AsyncComponentHarnessGenerator");
            sb.AppendLine("// Emits AOT-safe constructor + " +
                "InvokeCoreAsyncLift wiring for each [AsyncExport].");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using Wacs.Core.Runtime;");
            sb.AppendLine();

            bool hasNs = !string.IsNullOrEmpty(cls.Namespace);
            if (hasNs)
            {
                sb.Append("namespace ");
                sb.AppendLine(cls.Namespace);
                sb.AppendLine("{");
            }

            sb.Append("    ");
            sb.Append(cls.Accessibility);
            sb.Append(" partial class ");
            sb.AppendLine(cls.ClassName);
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly global::"
                + ComponentInstanceFqn + " _instance;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>The underlying " +
                "<see cref=\"global::"
                + ComponentInstanceFqn + "\"/> — surfaced so " +
                "consumers can reach the dispatcher / host " +
                "directly when needed.</summary>");
            sb.AppendLine("        public global::"
                + ComponentInstanceFqn + " Instance => _instance;");
            sb.AppendLine();
            sb.Append("        ");
            sb.Append(cls.Accessibility);
            sb.Append(' ');
            sb.Append(cls.ClassName);
            sb.AppendLine("(byte[] componentBytes,");
            sb.AppendLine("            System.Action<global::Wacs" +
                ".Core.Runtime.WasmRuntime>? configureImports = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            _instance = global::"
                + ComponentInstanceFqn
                + ".InstantiateAot(componentBytes, configureImports);");
            sb.AppendLine("        }");

            // Memoized invoker fields for sync exports — one
            // per method, lazily resolved on first invocation.
            // Class-scope so they live as long as the harness
            // instance.
            foreach (var ex in cls.Exports)
            {
                if (ex.Kind != ExportKind.Sync) continue;
                sb.Append("        private ");
                sb.Append(BuildInvokerDelegateType(ex));
                sb.Append(" _invoker_");
                sb.Append(ex.MethodName);
                sb.AppendLine(";");
            }

            // String marshaling state — memory + cabi_realloc
            // invoker + per-method cabi_post invokers. Only
            // emitted when at least one sync export references
            // a string param or return; otherwise these would
            // be dead-fielding.
            bool anyString = false;
            foreach (var ex in cls.Exports)
            {
                if (ex.Kind == ExportKind.Sync && AnyString(ex))
                {
                    anyString = true;
                    break;
                }
            }
            if (anyString)
            {
                sb.AppendLine(
                    "        private global::Wacs.Core.Runtime.Types" +
                    ".MemoryInstance? _memory;");
                sb.AppendLine(
                    "        private System.Func<int, int, int, int, int>? " +
                    "_reallocInvoke;");
                foreach (var ex in cls.Exports)
                {
                    if (ex.Kind != ExportKind.Sync) continue;
                    if (ex.ReturnType == null
                        || !IsString(ex.ReturnType)) continue;
                    sb.Append(
                        "        private System.Action<int>? _post_");
                    sb.Append(ex.MethodName);
                    sb.AppendLine(";");
                }
            }

            foreach (var ex in cls.Exports)
            {
                sb.AppendLine();
                EmitExportMethod(sb, ex);
            }

            sb.AppendLine("    }");
            if (hasNs) sb.AppendLine("}");
            return sb.ToString();
        }

        // Emit one partial method body. Supports void return
        // and primitive (canon-ABI flat) return types; primitive
        // (canon-ABI flat) parameter types are boxed and passed
        // verbatim to InvokeCoreAsyncLift. Non-primitive types
        // emit a `#error` directive that fires at consumer
        // compile time — points at the partial method that
        // needs hand-marshaling until the typed lift/lower
        // codegen extension lands.
        private static void EmitExportMethod(
            StringBuilder sb, ExportMethod ex)
        {
            // Validate types up front so the error message
            // identifies the offending parameter / return type.
            // Async exports currently only handle primitives —
            // string lower-into-args + lift-from-task-return
            // requires task.return wiring not yet emitted by
            // the generator. Sync exports get the full
            // primitive + string surface via
            // StringCoding.LowerUtf8 / LiftUtf8 + cabi_realloc.
            bool isSync = ex.Kind == ExportKind.Sync;
            foreach (var p in ex.Parameters)
            {
                bool ok = isSync
                    ? IsSupportedParam(p.Type)
                    : IsPrimitive(p.Type);
                if (!ok)
                {
                    sb.Append("        #error ");
                    sb.Append(
                        $"[{(isSync ? "SyncExport" : "AsyncExport")}] " +
                        $"parameter '{p.Name}' on {ex.MethodName} " +
                        $"has unsupported type '{p.Type}'. " +
                        (isSync
                            ? "Sync exports support primitives + " +
                              "string today; list/aggregate lift-" +
                              "lower codegen lands next."
                            : "Async exports only support canon-" +
                              "ABI primitives today (task.return " +
                              "string codegen pending)."));
                    sb.AppendLine();
                    return;
                }
            }
            if (ex.ReturnType != null)
            {
                bool ok = isSync
                    ? IsSupportedReturn(ex.ReturnType)
                    : IsPrimitive(ex.ReturnType);
                if (!ok)
                {
                    sb.Append("        #error ");
                    sb.Append(
                        $"[{(isSync ? "SyncExport" : "AsyncExport")}] " +
                        $"return type '{ex.ReturnType}' on " +
                        $"{ex.MethodName} is unsupported. " +
                        (isSync
                            ? "Sync exports support primitives + " +
                              "string today."
                            : "Async exports only support canon-" +
                              "ABI primitives today."));
                    sb.AppendLine();
                    return;
                }
            }

            sb.Append("        ");
            sb.Append(ex.Accessibility);
            sb.Append(" partial ");
            sb.Append(ex.ReturnType ?? "void");
            sb.Append(' ');
            sb.Append(ex.MethodName);
            sb.Append('(');
            for (int i = 0; i < ex.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(ex.Parameters[i].Type);
                sb.Append(' ');
                sb.Append(ex.Parameters[i].Name);
            }
            sb.AppendLine(")");
            sb.AppendLine("        {");

            // The args array dance: InvokeCoreAsyncLift takes
            // `params object?[]`. Primitive args box. For zero
            // args we pass Array.Empty<object?>() to avoid an
            // allocation on every call.
            string argsExpr;
            if (ex.Parameters.Length == 0)
            {
                argsExpr = "System.Array.Empty<object?>()";
            }
            else
            {
                var argsBuilder = new StringBuilder();
                argsBuilder.Append("new object?[] { ");
                for (int i = 0; i < ex.Parameters.Length; i++)
                {
                    if (i > 0) argsBuilder.Append(", ");
                    argsBuilder.Append(ex.Parameters[i].Name);
                }
                argsBuilder.Append(" }");
                argsExpr = argsBuilder.ToString();
            }

            if (ex.Kind == ExportKind.Async)
            {
                EmitAsyncExportBody(sb, ex, argsExpr);
            }
            else
            {
                EmitSyncExportBody(sb, ex);
            }
            sb.AppendLine("        }");
        }

        private static void EmitAsyncExportBody(
            StringBuilder sb, ExportMethod ex, string argsExpr)
        {
            if (ex.ReturnType == null)
            {
                sb.Append("            _instance.InvokeCoreAsyncLift(\"");
                sb.Append(EscapeStringLiteral(ex.ExportName));
                sb.Append("\", ");
                sb.Append(argsExpr);
                sb.AppendLine(");");
            }
            else
            {
                sb.Append("            var __result = ");
                sb.Append("_instance.InvokeCoreAsyncLift(\"");
                sb.Append(EscapeStringLiteral(ex.ExportName));
                sb.Append("\", ");
                sb.Append(argsExpr);
                sb.AppendLine(");");
                sb.Append("            return (");
                sb.Append(ex.ReturnType);
                sb.AppendLine(")__result!;");
            }
        }

        // Sync exports route through
        // <c>WasmRuntime.CreateInvokerFunc&lt;...&gt;</c> /
        // <c>CreateInvokerAction&lt;...&gt;</c> with statically
        // generic type args derived from the partial method's
        // canon-ABI flat signature (per-parameter ints for
        // strings; statically-known primitive types otherwise).
        // String params lower via cabi_realloc + UTF-8 encode;
        // string returns lift via memory-read + UTF-8 decode +
        // cabi_post_X cleanup.
        private static void EmitSyncExportBody(
            StringBuilder sb, ExportMethod ex)
        {
            bool hasString = AnyString(ex);
            if (hasString)
            {
                EmitSyncEnsureMemoryAndRealloc(sb);
            }

            string field = "_invoker_" + ex.MethodName;
            string createMethod = ex.ReturnType == null
                ? "CreateInvokerAction"
                : "CreateInvokerFunc";

            // Lazy resolve the core invoker. For string-bearing
            // methods the flat signature differs from the
            // declared C# signature — strings flatten to (ptr,
            // len) pairs and a string return flattens to (ptr,
            // len) emitted into the retArea (returning a single
            // i32 retArea pointer).
            sb.Append("            if (");
            sb.Append(field);
            sb.AppendLine(" == null)");
            sb.AppendLine("            {");
            sb.Append("                if (!_instance.CoreRuntime" +
                ".TryGetExportedFunction(\"");
            sb.Append(EscapeStringLiteral(ex.ExportName));
            sb.AppendLine("\", out var __addr))");
            sb.AppendLine("                    throw new System" +
                ".InvalidOperationException(");
            sb.Append("                        \"Missing export '");
            sb.Append(EscapeStringLiteral(ex.ExportName));
            sb.AppendLine("'.\");");
            sb.Append("                ");
            sb.Append(field);
            sb.Append(" = _instance.CoreRuntime.");
            sb.Append(createMethod);
            sb.Append(BuildFlatInvokerTypeArgs(ex));
            sb.AppendLine("(__addr);");
            sb.AppendLine("            }");

            // Lower string params into wasm memory before the
            // call. Each `(string foo)` becomes
            // `(int __foo_ptr, int __foo_len)`.
            foreach (var p in ex.Parameters)
            {
                if (IsString(p.Type))
                {
                    sb.Append(
                        "            global::Wacs.ComponentModel" +
                        ".Harness.StringCoding.LowerUtf8(_memory!, ");
                    sb.Append(p.Name);
                    sb.Append(
                        ", _reallocInvoke!, out int __");
                    sb.Append(p.Name);
                    sb.Append("_ptr, out int __");
                    sb.Append(p.Name);
                    sb.AppendLine("_len);");
                }
            }

            // Call the underlying invoker with the flattened
            // args. String args expand to (ptr, len); primitives
            // pass straight through.
            bool returnsString = ex.ReturnType != null
                && IsString(ex.ReturnType);
            sb.Append("            ");
            if (ex.ReturnType != null)
                sb.Append("var __raw = ");
            sb.Append(field);
            sb.Append('(');
            EmitFlatArgsList(sb, ex);
            sb.AppendLine(");");

            // If the export returned a primitive, just cast +
            // return. If it returned a string, read the (ptr,
            // len) tuple out of the retArea, lift, and call
            // cabi_post_<exportName> to release the retArea.
            if (returnsString)
            {
                EmitSyncStringReturnLift(sb, ex);
            }
            else if (ex.ReturnType != null)
            {
                sb.Append("            return __raw;");
                sb.AppendLine();
            }
        }

        // String params + return all need access to the wasm
        // memory + an invoker for cabi_realloc. Emitted once
        // per call as guarded init — cheap branch on hot path.
        private static void EmitSyncEnsureMemoryAndRealloc(
            StringBuilder sb)
        {
            sb.AppendLine("            if (_memory == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (!_instance.CoreRuntime" +
                ".TryGetExportedMemory(\"memory\", out var __mem))");
            sb.AppendLine("                    throw new System" +
                ".InvalidOperationException(");
            sb.AppendLine(
                "                        \"String marshaling " +
                "requires an exported memory named 'memory'.\");");
            sb.AppendLine("                _memory = __mem;");
            sb.AppendLine("            }");
            sb.AppendLine("            if (_reallocInvoke == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (!_instance.CoreRuntime" +
                ".TryGetExportedFunction(\"cabi_realloc\", out var __reallocAddr))");
            sb.AppendLine("                    throw new System" +
                ".InvalidOperationException(");
            sb.AppendLine(
                "                        \"String marshaling " +
                "requires the component to export cabi_realloc.\");");
            sb.AppendLine("                _reallocInvoke = _instance" +
                ".CoreRuntime.CreateInvokerFunc<int, int, int, int, int>" +
                "(__reallocAddr);");
            sb.AppendLine("            }");
        }

        // String return lift: memory[retArea] = ptr,
        // memory[retArea+4] = len; UTF-8 decode + cabi_post.
        private static void EmitSyncStringReturnLift(
            StringBuilder sb, ExportMethod ex)
        {
            string postExport = "cabi_post_" + ex.ExportName;
            string postField = "_post_" + ex.MethodName;
            sb.AppendLine(
                "            int __outPtr = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadI32LE(_memory!, __raw);");
            sb.AppendLine(
                "            int __outLen = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadI32LE(_memory!, __raw + 4);");
            sb.AppendLine(
                "            string __result = global::Wacs" +
                ".ComponentModel.Harness.StringCoding" +
                ".LiftUtf8(_memory!, __outPtr, __outLen);");
            // Lazy cabi_post resolve. Some components don't
            // emit cabi_post_X; we tolerate that by leaving
            // the retArea unfreed (small leak per call,
            // matches wasmtime's behavior when the post-return
            // function is absent).
            sb.Append("            if (");
            sb.Append(postField);
            sb.AppendLine(" == null)");
            sb.AppendLine("            {");
            sb.Append(
                "                if (_instance.CoreRuntime" +
                ".TryGetExportedFunction(\"");
            sb.Append(EscapeStringLiteral(postExport));
            sb.AppendLine("\", out var __postAddr))");
            sb.Append("                    ");
            sb.Append(postField);
            sb.AppendLine(" = _instance.CoreRuntime" +
                ".CreateInvokerAction<int>(__postAddr);");
            sb.AppendLine("            }");
            sb.Append("            ");
            sb.Append(postField);
            sb.AppendLine("?.Invoke(__raw);");
            sb.AppendLine("            return __result;");
        }

        private static void EmitFlatArgsList(
            StringBuilder sb, ExportMethod ex)
        {
            bool first = true;
            foreach (var p in ex.Parameters)
            {
                if (!first) sb.Append(", ");
                first = false;
                if (IsString(p.Type))
                {
                    sb.Append("__");
                    sb.Append(p.Name);
                    sb.Append("_ptr, __");
                    sb.Append(p.Name);
                    sb.Append("_len");
                }
                else
                {
                    sb.Append(p.Name);
                }
            }
        }

        // Flat (canon-ABI lowered) generic args for
        // CreateInvokerFunc<...> / CreateInvokerAction<...>.
        // Each string expands to two ints; primitives stay; a
        // string return flattens to a single int retArea.
        private static string BuildFlatInvokerTypeArgs(
            ExportMethod ex)
        {
            int paramSlots = 0;
            foreach (var p in ex.Parameters)
                paramSlots += IsString(p.Type) ? 2 : 1;
            bool hasReturn = ex.ReturnType != null;
            if (paramSlots == 0 && !hasReturn) return "";

            var sb = new StringBuilder();
            sb.Append('<');
            bool first = true;
            foreach (var p in ex.Parameters)
            {
                if (IsString(p.Type))
                {
                    if (!first) sb.Append(", ");
                    sb.Append("int");
                    sb.Append(", int");
                    first = false;
                }
                else
                {
                    if (!first) sb.Append(", ");
                    sb.Append(p.Type);
                    first = false;
                }
            }
            if (hasReturn)
            {
                if (!first) sb.Append(", ");
                if (IsString(ex.ReturnType!))
                    sb.Append("int");
                else
                    sb.Append(ex.ReturnType);
            }
            sb.Append('>');
            return sb.ToString();
        }

        private static bool AnyString(ExportMethod ex)
        {
            if (ex.ReturnType != null && IsString(ex.ReturnType))
                return true;
            foreach (var p in ex.Parameters)
                if (IsString(p.Type)) return true;
            return false;
        }

        // Field type matching the FLAT (canon-ABI lowered)
        // signature — string → int, int; string return →
        // int retArea. Used for the memoized invoker field
        // declaration.
        private static string BuildInvokerDelegateType(
            ExportMethod ex)
        {
            int paramSlots = 0;
            foreach (var p in ex.Parameters)
                paramSlots += IsString(p.Type) ? 2 : 1;
            bool hasReturn = ex.ReturnType != null;

            var sb = new StringBuilder();
            if (!hasReturn)
            {
                if (paramSlots == 0)
                    return "System.Action?";
                sb.Append("System.Action<");
                AppendFlatParamTypes(sb, ex);
                sb.Append(">?");
                return sb.ToString();
            }
            sb.Append("System.Func<");
            if (paramSlots > 0)
            {
                AppendFlatParamTypes(sb, ex);
                sb.Append(", ");
            }
            if (IsString(ex.ReturnType!)) sb.Append("int");
            else sb.Append(ex.ReturnType);
            sb.Append(">?");
            return sb.ToString();
        }

        private static void AppendFlatParamTypes(
            StringBuilder sb, ExportMethod ex)
        {
            bool first = true;
            foreach (var p in ex.Parameters)
            {
                if (IsString(p.Type))
                {
                    if (!first) sb.Append(", ");
                    sb.Append("int, int");
                    first = false;
                }
                else
                {
                    if (!first) sb.Append(", ");
                    sb.Append(p.Type);
                    first = false;
                }
            }
        }

        // Build the <T1,...> chunk for the CreateInvokerFunc /
        // CreateInvokerAction generic args.
        private static string BuildInvokerTypeArgs(
            ExportMethod ex)
        {
            if (ex.Parameters.Length == 0 && ex.ReturnType == null)
                return "";
            var sb = new StringBuilder();
            sb.Append('<');
            if (ex.Parameters.Length > 0)
            {
                AppendParamTypes(sb, ex);
                if (ex.ReturnType != null) sb.Append(", ");
            }
            if (ex.ReturnType != null) sb.Append(ex.ReturnType);
            sb.Append('>');
            return sb.ToString();
        }

        private static void AppendParamTypes(
            StringBuilder sb, ExportMethod ex)
        {
            for (int i = 0; i < ex.Parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(ex.Parameters[i].Type);
            }
        }

        private static string EscapeStringLiteral(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
