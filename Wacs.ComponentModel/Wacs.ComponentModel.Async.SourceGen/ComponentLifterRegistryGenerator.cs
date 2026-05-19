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
    /// Scans the current compilation for static methods marked
    /// <c>[ComponentLifter("wit-id")]</c> and emits a
    /// <c>[ModuleInitializer]</c>-decorated registration method
    /// that calls
    /// <c>Wacs.ComponentModel.Async.ComponentLifterRegistry.Register</c>
    /// once per match at assembly load.
    ///
    /// <para>Companion to
    /// <see cref="CanonOpRegistryGenerator"/>: each runs in a
    /// different assembly per its trigger.
    /// <see cref="CanonOpRegistryGenerator"/> emits the
    /// canon-op name set in <c>Wacs.ComponentModel</c> itself; this
    /// generator emits the registration in <i>every</i> consumer
    /// assembly that ships a <c>[ComponentLifter]</c>-marked
    /// method.</para>
    ///
    /// <para><b>Polyfill.</b> Module initializers require
    /// <c>System.Runtime.CompilerServices.ModuleInitializerAttribute</c>,
    /// shipped in <c>net5.0+</c>. For older targets the generator
    /// emits an internal polyfill alongside the initializer —
    /// safe because the attribute is matched by FQN, not identity.</para>
    /// </summary>
    [Generator]
    public sealed class ComponentLifterRegistryGenerator : IIncrementalGenerator
    {
        private const string AttributeFqn =
            "Wacs.ComponentModel.Async.ComponentLifterAttribute";
        private const string RegistryFqn =
            "Wacs.ComponentModel.Async.ComponentLifterRegistry";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var entriesProvider = context.CompilationProvider.Select(
                static (compilation, _) =>
                {
                    // Skip emission when the consumer can't see
                    // Wacs.ComponentModel.Async at all — the
                    // Register call site wouldn't compile.
                    var registry = compilation.GetTypeByMetadataName(RegistryFqn);
                    if (registry == null)
                        return default(ImmutableArray<LifterEntry>);
                    return CollectLifters(compilation);
                });

            context.RegisterSourceOutput(entriesProvider,
                static (spc, entries) =>
                {
                    if (entries.IsDefault) return;
                    if (entries.Length == 0) return;
                    spc.AddSource("ComponentLifterRegistration.g.cs",
                        EmitRegistration(entries));
                });
        }

        private readonly struct LifterEntry
        {
            public string WitIdent { get; }
            public string FullyQualifiedTypeName { get; }
            public string MethodName { get; }
            public ImmutableArray<string> ParamTypes { get; }
            public string? ReturnType { get; }
            public LifterEntry(
                string witIdent,
                string fqType,
                string methodName,
                ImmutableArray<string> paramTypes,
                string? returnType)
            {
                WitIdent = witIdent;
                FullyQualifiedTypeName = fqType;
                MethodName = methodName;
                ParamTypes = paramTypes;
                ReturnType = returnType;
            }
        }

        private static ImmutableArray<LifterEntry> CollectLifters(
            Compilation compilation)
        {
            var attrType = compilation.GetTypeByMetadataName(AttributeFqn);
            if (attrType == null) return ImmutableArray<LifterEntry>.Empty;

            var entries = ImmutableArray.CreateBuilder<LifterEntry>();
            foreach (var type in EnumerateAllTypes(compilation.Assembly.GlobalNamespace))
            {
                foreach (var member in type.GetMembers())
                {
                    if (member is not IMethodSymbol method) continue;
                    if (!method.IsStatic) continue;
                    foreach (var attr in method.GetAttributes())
                    {
                        if (!SymbolEqualityComparer.Default.Equals(
                                attr.AttributeClass, attrType))
                            continue;
                        if (attr.ConstructorArguments.Length == 0) continue;
                        if (attr.ConstructorArguments[0].Value
                                is not string witIdent
                            || string.IsNullOrEmpty(witIdent)) continue;

                        var fqType = type.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat);
                        var paramTypes = method.Parameters
                            .Select(p => p.Type.ToDisplayString(
                                SymbolDisplayFormat.FullyQualifiedFormat))
                            .ToImmutableArray();
                        string? returnType = method.ReturnsVoid
                            ? null
                            : method.ReturnType.ToDisplayString(
                                SymbolDisplayFormat.FullyQualifiedFormat);
                        entries.Add(new LifterEntry(
                            witIdent, fqType, method.Name,
                            paramTypes, returnType));
                    }
                }
            }
            // Deterministic ordering by WIT ident → consistent
            // generated source across recompiles.
            return entries
                .OrderBy(e => e.WitIdent, StringComparer.Ordinal)
                .ToImmutableArray();
        }

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

        private static string EmitRegistration(
            ImmutableArray<LifterEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// Source generator: " +
                "Wacs.ComponentModel.Async.SourceGen.ComponentLifterRegistryGenerator");
            sb.AppendLine("// Emits a [ModuleInitializer]-decorated method " +
                "that registers every [ComponentLifter] in this assembly");
            sb.AppendLine("// with Wacs.ComponentModel.Async.ComponentLifterRegistry.");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine();

            // Polyfill ModuleInitializerAttribute for targets that
            // don't ship it (netstandard2.0 / netstandard2.1).
            // The C# compiler matches the attribute by FQN, so a
            // user-defined attribute satisfies the requirement.
            sb.AppendLine("#if !NET5_0_OR_GREATER");
            sb.AppendLine("namespace System.Runtime.CompilerServices");
            sb.AppendLine("{");
            sb.AppendLine("    [AttributeUsage(AttributeTargets.Method, Inherited = false)]");
            sb.AppendLine("    internal sealed class ModuleInitializerAttribute : Attribute { }");
            sb.AppendLine("}");
            sb.AppendLine("#endif");
            sb.AppendLine();

            sb.AppendLine("namespace Wacs.ComponentModel.Async.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class ComponentLifterRegistration");
            sb.AppendLine("    {");
            sb.AppendLine("        [ModuleInitializer]");
            sb.AppendLine("        internal static void Register()");
            sb.AppendLine("        {");
            foreach (var entry in entries)
            {
                sb.Append("            global::Wacs.ComponentModel.Async.ComponentLifterRegistry.Register(\"");
                sb.Append(entry.WitIdent);
                sb.Append("\", (");
                sb.Append(EmitDelegateType(entry));
                sb.Append(")");
                sb.Append(entry.FullyQualifiedTypeName);
                sb.Append('.');
                sb.Append(entry.MethodName);
                sb.AppendLine(");");
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // Construct an Action<...> or Func<..., T> delegate type
        // matching the method signature. void return → Action,
        // otherwise Func<..., TReturn>.
        private static string EmitDelegateType(LifterEntry entry)
        {
            if (entry.ReturnType == null)
            {
                if (entry.ParamTypes.Length == 0) return "System.Action";
                return "System.Action<" +
                    string.Join(", ", entry.ParamTypes) + ">";
            }
            var sb = new StringBuilder();
            sb.Append("System.Func<");
            for (int i = 0; i < entry.ParamTypes.Length; i++)
            {
                sb.Append(entry.ParamTypes[i]);
                sb.Append(", ");
            }
            sb.Append(entry.ReturnType);
            sb.Append('>');
            return sb.ToString();
        }
    }
}
