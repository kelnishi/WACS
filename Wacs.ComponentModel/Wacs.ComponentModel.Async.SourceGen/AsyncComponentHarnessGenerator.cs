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
        private const string ComponentInstanceFqn =
            "Wacs.ComponentModel.Runtime.ComponentInstance";

        public void Initialize(
            IncrementalGeneratorInitializationContext context)
        {
            var harnessClasses = context.CompilationProvider.Select(
                static (compilation, _) =>
                {
                    // Skip emission when the consumer doesn't
                    // reference Wacs.ComponentModel (the
                    // attribute / target class wouldn't be
                    // resolvable). This makes the generator
                    // safe to ship in every consumer assembly.
                    var attr = compilation.GetTypeByMetadataName(
                        HarnessAttributeFqn);
                    if (attr == null)
                        return default(
                            ImmutableArray<HarnessClass>);
                    return CollectHarnessClasses(compilation);
                });

            context.RegisterSourceOutput(harnessClasses,
                static (spc, classes) =>
                {
                    if (classes.IsDefault) return;
                    foreach (var cls in classes)
                        spc.AddSource(
                            cls.GeneratedFileName,
                            EmitHarness(cls));
                });
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

        private readonly struct ExportMethod
        {
            public string MethodName { get; }
            public string ExportName { get; }
            public string Accessibility { get; }
            public ImmutableArray<ExportParam> Parameters { get; }
            public string? ReturnType { get; }
            public ExportMethod(
                string methodName, string exportName,
                string accessibility,
                ImmutableArray<ExportParam> parameters,
                string? returnType)
            {
                MethodName = methodName;
                ExportName = exportName;
                Accessibility = accessibility;
                Parameters = parameters;
                ReturnType = returnType;
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

        // Primitive (canon-ABI flat) types the MVP marshals
        // directly through InvokeCoreAsyncLift. Aggregates
        // (string / list / record / variant / option / result)
        // need cabi-realloc + per-shape lift/lower — punted to
        // a follow-up slice.
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

        private static bool IsPrimitive(string fqType) =>
            PrimitiveTypes.Contains(fqType);

        private static ImmutableArray<HarnessClass> CollectHarnessClasses(
            Compilation compilation)
        {
            var harnessAttr = compilation.GetTypeByMetadataName(
                HarnessAttributeFqn);
            var exportAttr = compilation.GetTypeByMetadataName(
                ExportAttributeFqn);
            if (harnessAttr == null || exportAttr == null)
                return ImmutableArray<HarnessClass>.Empty;

            var entries = ImmutableArray.CreateBuilder<HarnessClass>();
            foreach (var type in EnumerateAllTypes(
                compilation.Assembly.GlobalNamespace))
            {
                if (!HasAttribute(type, harnessAttr)) continue;

                var exports = ImmutableArray.CreateBuilder<ExportMethod>();
                foreach (var member in type.GetMembers())
                {
                    if (member is not IMethodSymbol method) continue;
                    if (!method.IsPartialDefinition) continue;
                    foreach (var attr in method.GetAttributes())
                    {
                        if (!SymbolEqualityComparer.Default.Equals(
                                attr.AttributeClass, exportAttr))
                            continue;
                        if (attr.ConstructorArguments.Length == 0) continue;
                        if (attr.ConstructorArguments[0].Value
                                is not string exportName
                            || string.IsNullOrEmpty(exportName))
                            continue;

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
                            parameters, returnType));
                        break;
                    }
                }

                string ns = type.ContainingNamespace.IsGlobalNamespace
                    ? ""
                    : type.ContainingNamespace.ToDisplayString();
                entries.Add(new HarnessClass(
                    ns, type.Name,
                    AccessibilityKeyword(type.DeclaredAccessibility),
                    exports.ToImmutable()));
            }
            return entries
                .OrderBy(e => e.GeneratedFileName, StringComparer.Ordinal)
                .ToImmutableArray();
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
            foreach (var p in ex.Parameters)
            {
                if (!IsPrimitive(p.Type))
                {
                    sb.Append("        #error ");
                    sb.Append(
                        $"[AsyncExport] parameter '{p.Name}' on " +
                        $"{ex.MethodName} has unsupported type " +
                        $"'{p.Type}'. The MVP generator marshals " +
                        "only canon-ABI primitive (int/uint/long/" +
                        "ulong/short/ushort/byte/sbyte/bool/float/" +
                        "double) param + return types. Hand-write " +
                        "this method until the string/list/" +
                        "aggregate lift-lower codegen ships.");
                    sb.AppendLine();
                    return;
                }
            }
            if (ex.ReturnType != null && !IsPrimitive(ex.ReturnType))
            {
                sb.Append("        #error ");
                sb.Append(
                    $"[AsyncExport] return type '{ex.ReturnType}' " +
                    $"on {ex.MethodName} is not a canon-ABI " +
                    "primitive. Hand-write this method until the " +
                    "string/list/aggregate lift-lower codegen " +
                    "ships.");
                sb.AppendLine();
                return;
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
                // task.return delivers the boxed primitive; the
                // canon-async dispatcher's TaskCompletionSource<object?>
                // stores it as-is so a direct cast is sound.
                sb.Append("            return (");
                sb.Append(ex.ReturnType);
                sb.AppendLine(")__result!;");
            }
            sb.AppendLine("        }");
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
