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
            public ExportMethod(
                string methodName, string exportName,
                string accessibility)
            {
                MethodName = methodName;
                ExportName = exportName;
                Accessibility = accessibility;
            }
        }

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
                        exports.Add(new ExportMethod(
                            method.Name, exportName,
                            AccessibilityKeyword(method.DeclaredAccessibility)));
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
                sb.Append("        ");
                sb.Append(ex.Accessibility);
                sb.Append(" partial void ");
                sb.Append(ex.MethodName);
                sb.AppendLine("()");
                sb.AppendLine("        {");
                sb.Append(
                    "            _instance.InvokeCoreAsyncLift(\"");
                sb.Append(EscapeStringLiteral(ex.ExportName));
                sb.AppendLine("\");");
                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            if (hasNs) sb.AppendLine("}");
            return sb.ToString();
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
