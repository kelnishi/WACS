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
        // helpers (StringCoding + MemoryHelpers) and inline
        // byte-copy code.
        private const string StringType = "string";
        private const string StringTypeFq = "System.String";
        private const string ByteArrayType = "byte[]";
        private const string ByteArrayTypeFq = "System.Byte[]";

        private static bool IsPrimitive(string fqType) =>
            PrimitiveTypes.Contains(fqType);

        private static bool IsString(string fqType) =>
            fqType == StringType || fqType == StringTypeFq;

        private static bool IsByteArray(string fqType) =>
            fqType == ByteArrayType || fqType == ByteArrayTypeFq;

        // String + byte[] both flatten to (i32 ptr, i32 len)
        // — same canon-ABI shape with different encoding.
        // Treating them uniformly in the flat-signature
        // computation simplifies the generic-args build.
        private static bool IsPtrLenAggregate(string fqType) =>
            IsString(fqType) || IsByteArray(fqType);

        // Canon-ABI option<T> for primitive T. C# representation
        // is Nullable<T> — `int?`, `bool?`, `long?`, etc.
        // Flat lowering: (i32 disc, payloadSlot...). For
        // primitive T the payload is a single slot matching T's
        // canon-ABI slot kind (i32 for i8/i16/i32, i64 for i64,
        // f32/f64 for floats).
        private static readonly HashSet<string> NullablePrimitiveTypes =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "int?", "uint?", "long?", "ulong?",
                "byte?", "sbyte?", "short?", "ushort?",
                "bool?", "float?", "double?",
                "System.Int32?", "System.UInt32?",
                "System.Int64?", "System.UInt64?",
                "System.Byte?", "System.SByte?",
                "System.Int16?", "System.UInt16?",
                "System.Boolean?", "System.Single?",
                "System.Double?",
            };

        private static bool IsNullablePrimitive(string fqType) =>
            NullablePrimitiveTypes.Contains(fqType);

        // Strip the trailing '?' from `T?` to get the inner T
        // for codegen of the payload slot type.
        private static string InnerOfNullable(string fqType) =>
            fqType.EndsWith("?", StringComparison.Ordinal)
                ? fqType.Substring(0, fqType.Length - 1)
                : fqType;

        private static bool IsSupportedParam(string fqType) =>
            IsPrimitive(fqType)
            || IsString(fqType)
            || IsByteArray(fqType)
            || IsNullablePrimitive(fqType);

        private static bool IsSupportedReturn(string? fqType) =>
            fqType == null
            || IsPrimitive(fqType)
            || IsString(fqType)
            || IsByteArray(fqType)
            || IsNullablePrimitive(fqType);

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

            // Class-scope marshaling state for aggregate
            // (string / byte[] / option<T>) — memory +
            // optionally cabi_realloc invoker + per-method
            // cabi_post invokers.
            //
            // Splitting needs:
            //   * _memory — any aggregate (ptr/len OR
            //     option<T> with retArea-style return).
            //   * _reallocInvoke — only ptr/len aggregates
            //     (string / byte[]) need wasm-side allocation.
            //   * _post_<MethodName> — only string / byte[]
            //     returns need the post-return free hook.
            bool needsMemory = false;
            bool needsRealloc = false;
            foreach (var ex in cls.Exports)
            {
                if (ex.Kind != ExportKind.Sync) continue;
                if (AnyPtrLenAggregate(ex))
                {
                    needsMemory = true;
                    needsRealloc = true;
                }
                if (ex.ReturnType != null
                    && IsNullablePrimitive(ex.ReturnType))
                {
                    needsMemory = true;
                }
            }
            if (needsMemory)
            {
                sb.AppendLine(
                    "        private global::Wacs.Core.Runtime.Types" +
                    ".MemoryInstance? _memory;");
            }
            if (needsRealloc)
            {
                sb.AppendLine(
                    "        private System.Func<int, int, int, int, int>? " +
                    "_reallocInvoke;");
            }
            foreach (var ex in cls.Exports)
            {
                if (ex.Kind != ExportKind.Sync) continue;
                if (ex.ReturnType == null
                    || !IsPtrLenAggregate(ex.ReturnType))
                    continue;
                sb.Append(
                    "        private System.Action<int>? _post_");
                sb.Append(ex.MethodName);
                sb.AppendLine(";");
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
            // Memory + cabi_realloc state are needed for any
            // aggregate that lowers via memory (string,
            // byte[]). Option<T> for primitive T uses pure
            // value-flat lowering — no memory access needed
            // for params or return.
            bool needsMemory = AnyPtrLenAggregate(ex);
            if (needsMemory)
            {
                EmitSyncEnsureMemoryAndRealloc(sb);
            }
            // option<T> return goes through a retArea — we
            // need memory but no realloc (the callee owns
            // the retArea and we never free it for primitives
            // — bare flat reads).
            bool needsMemoryForOptionReturn =
                ex.ReturnType != null
                && IsNullablePrimitive(ex.ReturnType);
            if (needsMemoryForOptionReturn && !needsMemory)
            {
                EmitSyncEnsureMemoryOnly(sb);
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

            // Lower aggregate (string / byte[]) params into
            // wasm memory before the call. Each
            // `(string foo)` / `(byte[] foo)` becomes
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
                else if (IsByteArray(p.Type))
                {
                    // canon-ABI list<u8>: align 1, element
                    // size 1. cabi_realloc(0, 0, 1, len) → ptr;
                    // raw byte-copy in.
                    sb.Append("            int __");
                    sb.Append(p.Name);
                    sb.Append("_len = ");
                    sb.Append(p.Name);
                    sb.AppendLine(".Length;");
                    sb.Append("            int __");
                    sb.Append(p.Name);
                    sb.Append("_ptr = __");
                    sb.Append(p.Name);
                    sb.Append("_len == 0 ? 0 : _reallocInvoke!" +
                        "(0, 0, 1, __");
                    sb.Append(p.Name);
                    sb.AppendLine("_len);");
                    sb.Append("            if (__");
                    sb.Append(p.Name);
                    sb.Append("_ptr == 0 && __");
                    sb.Append(p.Name);
                    sb.AppendLine("_len != 0)");
                    sb.AppendLine("                throw new " +
                        "System.OutOfMemoryException(");
                    sb.Append("                    \"cabi_realloc " +
                        "returned 0 when lowering byte[] param '");
                    sb.Append(p.Name);
                    sb.AppendLine("'.\");");
                    sb.Append("            if (__");
                    sb.Append(p.Name);
                    sb.Append("_len > 0) System.Buffer.BlockCopy(");
                    sb.Append(p.Name);
                    sb.Append(", 0, _memory!.Data, __");
                    sb.Append(p.Name);
                    sb.Append("_ptr, __");
                    sb.Append(p.Name);
                    sb.AppendLine("_len);");
                }
                else if (IsNullablePrimitive(p.Type))
                {
                    // option<T> param lowering: (disc, payload).
                    // payload uses default(T) when value is
                    // absent — wasm-side ignores the slot per
                    // canon-ABI when disc=0.
                    string innerT = InnerOfNullable(p.Type);
                    sb.Append("            int __");
                    sb.Append(p.Name);
                    sb.Append("_disc = ");
                    sb.Append(p.Name);
                    sb.AppendLine(".HasValue ? 1 : 0;");
                    sb.Append("            ");
                    sb.Append(innerT);
                    sb.Append(" __");
                    sb.Append(p.Name);
                    sb.Append("_payload = ");
                    sb.Append(p.Name);
                    sb.AppendLine(".GetValueOrDefault();");
                }
            }

            // Call the underlying invoker with the flattened
            // args. ptr/len aggregates + option<T> expand
            // multi-slot; primitives pass straight through.
            sb.Append("            ");
            if (ex.ReturnType != null)
                sb.Append("var __raw = ");
            sb.Append(field);
            sb.Append('(');
            EmitFlatArgsList(sb, ex);
            sb.AppendLine(");");

            // If the export returned a primitive, just cast +
            // return. If it returned a multi-slot aggregate
            // (string / byte[] / option), read the tuple out
            // of the retArea, lift, and call cabi_post_<exp>
            // to release the retArea (only for ptr/len ags;
            // option<T> for primitive T uses inline retArea
            // — no allocation, no post-return needed).
            if (ex.ReturnType != null)
            {
                if (IsString(ex.ReturnType))
                    EmitSyncStringReturnLift(sb, ex);
                else if (IsByteArray(ex.ReturnType))
                    EmitSyncByteArrayReturnLift(sb, ex);
                else if (IsNullablePrimitive(ex.ReturnType))
                    EmitSyncOptionReturnLift(sb, ex);
                else
                    sb.AppendLine("            return __raw;");
            }
        }

        // option<T> return lift: __raw is the retArea pointer
        // produced by the callee. Read disc + payload at
        // canon-ABI offsets, return Nullable<T>.
        // Layout: disc:u8 at +0, padding to T's alignment,
        // payload at +align(1, sizeof(T)).
        private static void EmitSyncOptionReturnLift(
            StringBuilder sb, ExportMethod ex)
        {
            string innerT = InnerOfNullable(ex.ReturnType!);
            int payloadOff = NullablePayloadOffset(innerT);
            string readPayload = ReadMemoryExprForType(
                innerT, "_memory!", "__raw + " + payloadOff);
            sb.Append(
                "            byte __optDisc = global::Wacs" +
                ".ComponentModel.Harness.MemoryHelpers" +
                ".ReadU8(_memory!, __raw);");
            sb.AppendLine();
            sb.AppendLine("            if (__optDisc == 0) return null;");
            sb.Append("            return ");
            sb.Append(readPayload);
            sb.AppendLine(";");
        }

        // Canon-ABI alignment for primitives. Option<T>'s
        // discriminator sits at offset 0; payload starts at
        // align_to(1, alignof(T)).
        private static int NullablePayloadOffset(string innerT)
        {
            switch (innerT)
            {
                case "byte": case "sbyte": case "bool":
                case "System.Byte": case "System.SByte":
                case "System.Boolean":
                    return 1;
                case "short": case "ushort":
                case "System.Int16": case "System.UInt16":
                    return 2;
                case "int": case "uint": case "float":
                case "System.Int32": case "System.UInt32":
                case "System.Single":
                    return 4;
                case "long": case "ulong": case "double":
                case "System.Int64": case "System.UInt64":
                case "System.Double":
                    return 8;
                default:
                    return 4;
            }
        }

        // Generate the memory-read expression for a primitive
        // payload at a given offset.
        private static string ReadMemoryExprForType(
            string fqType, string memArg, string offArg)
        {
            string call;
            switch (fqType)
            {
                case "byte": case "System.Byte":
                    call = "ReadU8"; break;
                case "sbyte": case "System.SByte":
                    return "(sbyte)global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.ReadU8(" +
                        memArg + ", " + offArg + ")";
                case "bool": case "System.Boolean":
                    return "(global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.ReadU8(" +
                        memArg + ", " + offArg + ") != 0)";
                case "short": case "System.Int16":
                    call = "ReadI16LE"; break;
                case "ushort": case "System.UInt16":
                    return "(ushort)global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.ReadI16LE(" +
                        memArg + ", " + offArg + ")";
                case "int": case "System.Int32":
                    call = "ReadI32LE"; break;
                case "uint": case "System.UInt32":
                    return "(uint)global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.ReadI32LE(" +
                        memArg + ", " + offArg + ")";
                case "long": case "System.Int64":
                    call = "ReadI64LE"; break;
                case "ulong": case "System.UInt64":
                    return "(ulong)global::Wacs.ComponentModel" +
                        ".Harness.MemoryHelpers.ReadI64LE(" +
                        memArg + ", " + offArg + ")";
                case "float": case "System.Single":
                    call = "ReadF32LE"; break;
                case "double": case "System.Double":
                    call = "ReadF64LE"; break;
                default:
                    return "default(" + fqType + ")";
            }
            return "global::Wacs.ComponentModel.Harness" +
                ".MemoryHelpers." + call + "(" + memArg +
                ", " + offArg + ")";
        }

        // byte[] return lift: read (ptr, len) from retArea,
        // copy memory bytes into a fresh byte[], call
        // cabi_post_X to release.
        private static void EmitSyncByteArrayReturnLift(
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
                "            byte[] __result = new byte[__outLen];");
            sb.AppendLine(
                "            if (__outLen > 0)");
            sb.AppendLine(
                "                System.Buffer.BlockCopy(" +
                "_memory!.Data, __outPtr, __result, 0, __outLen);");
            // cabi_post lookup (same shape as string return).
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

        // Memory-only ensure for option<T> returns. Same
        // lazy-resolve pattern as the full memory+realloc
        // helper, just without the realloc lookup (option<T>
        // for primitive T doesn't allocate — the callee
        // writes its retArea result inline).
        private static void EmitSyncEnsureMemoryOnly(
            StringBuilder sb)
        {
            sb.AppendLine("            if (_memory == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (!_instance.CoreRuntime" +
                ".TryGetExportedMemory(\"memory\", out var __mem))");
            sb.AppendLine("                    throw new System" +
                ".InvalidOperationException(");
            sb.AppendLine(
                "                        \"Aggregate return " +
                "requires an exported memory named 'memory'.\");");
            sb.AppendLine("                _memory = __mem;");
            sb.AppendLine("            }");
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
                if (IsPtrLenAggregate(p.Type))
                {
                    sb.Append("__");
                    sb.Append(p.Name);
                    sb.Append("_ptr, __");
                    sb.Append(p.Name);
                    sb.Append("_len");
                }
                else if (IsNullablePrimitive(p.Type))
                {
                    sb.Append("__");
                    sb.Append(p.Name);
                    sb.Append("_disc, __");
                    sb.Append(p.Name);
                    sb.Append("_payload");
                }
                else
                {
                    sb.Append(p.Name);
                }
            }
        }

        private static bool AnyPtrLenAggregate(ExportMethod ex)
        {
            if (ex.ReturnType != null
                && IsPtrLenAggregate(ex.ReturnType))
                return true;
            foreach (var p in ex.Parameters)
                if (IsPtrLenAggregate(p.Type)) return true;
            return false;
        }

        // Flat (canon-ABI lowered) generic args for
        // CreateInvokerFunc<...> / CreateInvokerAction<...>.
        // Each ptr/len aggregate expands to two ints; each
        // option<T> expands to (disc:i32, T-slot); a
        // multi-slot return (string, byte[], option<T>) becomes
        // a single i32 retArea.
        private static string BuildFlatInvokerTypeArgs(
            ExportMethod ex)
        {
            int paramSlots = CountFlatSlots(ex);
            bool hasReturn = ex.ReturnType != null;
            if (paramSlots == 0 && !hasReturn) return "";

            var sb = new StringBuilder();
            sb.Append('<');
            bool first = true;
            foreach (var p in ex.Parameters)
            {
                if (IsPtrLenAggregate(p.Type))
                {
                    if (!first) sb.Append(", ");
                    sb.Append("int, int");
                    first = false;
                }
                else if (IsNullablePrimitive(p.Type))
                {
                    if (!first) sb.Append(", ");
                    sb.Append("int, ");
                    sb.Append(InnerOfNullable(p.Type));
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
                if (UsesRetArea(ex.ReturnType!))
                    sb.Append("int");
                else
                    sb.Append(ex.ReturnType);
            }
            sb.Append('>');
            return sb.ToString();
        }

        private static int CountFlatSlots(ExportMethod ex)
        {
            int slots = 0;
            foreach (var p in ex.Parameters)
            {
                if (IsPtrLenAggregate(p.Type)) slots += 2;
                else if (IsNullablePrimitive(p.Type)) slots += 2;
                else slots += 1;
            }
            return slots;
        }

        // Multi-slot return types (string, byte[], option<T>)
        // flatten to a single i32 retArea pointer at the wasm
        // calling-convention level — the callee writes the
        // tuple into linear memory and returns the address.
        private static bool UsesRetArea(string fqType) =>
            IsPtrLenAggregate(fqType)
            || IsNullablePrimitive(fqType);


        // Field type matching the FLAT (canon-ABI lowered)
        // signature — string → int, int; string return →
        // int retArea. Used for the memoized invoker field
        // declaration.
        private static string BuildInvokerDelegateType(
            ExportMethod ex)
        {
            int paramSlots = CountFlatSlots(ex);
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
            if (UsesRetArea(ex.ReturnType!))
                sb.Append("int");
            else
                sb.Append(ex.ReturnType);
            sb.Append(">?");
            return sb.ToString();
        }

        private static void AppendFlatParamTypes(
            StringBuilder sb, ExportMethod ex)
        {
            bool first = true;
            foreach (var p in ex.Parameters)
            {
                if (IsPtrLenAggregate(p.Type))
                {
                    if (!first) sb.Append(", ");
                    sb.Append("int, int");
                    first = false;
                }
                else if (IsNullablePrimitive(p.Type))
                {
                    // option<T> flat lowering: (i32 disc,
                    // payloadSlot). payloadSlot matches the
                    // inner T's natural canon-ABI flat type.
                    if (!first) sb.Append(", ");
                    sb.Append("int, ");
                    sb.Append(InnerOfNullable(p.Type));
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
