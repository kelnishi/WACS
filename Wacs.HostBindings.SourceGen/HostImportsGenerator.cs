// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Wacs.HostBindings.SourceGen
{
    /// <summary>
    /// Emits <c>GeneratedHostImports</c> partial classes implementing the
    /// <c>IImports</c> interfaces named by <c>[WacsTranspiledImports]</c>
    /// attributes on referenced assemblies. Each method body delegates to a
    /// matching <c>[WacsImport(module, name)]</c>-annotated static method
    /// found in any referenced assembly's exported types.
    ///
    /// <para>Per-call signature contract: the matched static method's
    /// parameters must be <c>(WacsHostMemory mem, [TState... state]?, T1 arg1,
    /// ..., TN argN)</c> where the trailing args match the IImports method's
    /// parameters by type, in order. Any leading non-mem, non-primitive
    /// parameters are treated as shared state and threaded through from a
    /// constructor on the generated class.</para>
    /// </summary>
    [Generator]
    public sealed class HostImportsGenerator : IIncrementalGenerator
    {
        private const string WacsImportAttrFqn = "Wacs.HostBindings.WacsImportAttribute";
        private const string WacsTranspiledImportsAttrFqn = "Wacs.HostBindings.WacsTranspiledImportsAttribute";
        private const string WacsHostMemoryFqn = "Wacs.HostBindings.WacsHostMemory";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 1. Pull all (module, name) → IMethodSymbol bindings from the
            //    consumer compilation's referenced assemblies. We scan the
            //    Compilation directly (not via ForAttributeWithMetadataName,
            //    which only sees source-defined symbols, not metadata).
            var bindingsProvider = context.CompilationProvider.Select(
                static (compilation, _) => CollectBindings(compilation));

            // 2. Pull all [WacsTranspiledImports]-tagged IImports interface
            //    types from the consumer's referenced assemblies + own
            //    compilation.
            var importsProvider = context.CompilationProvider.Select(
                static (compilation, _) => CollectTranspiledImports(compilation));

            // 3. Combine and emit.
            var combined = bindingsProvider.Combine(importsProvider);
            context.RegisterSourceOutput(combined, static (spc, source) =>
            {
                var (bindings, importsList) = source;
                foreach (var imports in importsList)
                {
                    EmitOneAdapter(spc, imports, bindings);
                }
            });
        }

        // -----------------------------------------------------------------
        // Discovery
        // -----------------------------------------------------------------

        /// <summary>
        /// Walk every assembly referenced by the compilation (plus the
        /// compilation's own source) and collect every public/internal
        /// static method tagged with <c>[WacsImport(module, name)]</c>.
        /// Keyed by <c>(module, name)</c>.
        /// </summary>
        private static ImmutableDictionary<(string Module, string Name), ImmutableArray<BindingDescriptor>>
            CollectBindings(Compilation compilation)
        {
            var attrSym = compilation.GetTypeByMetadataName(WacsImportAttrFqn);
            if (attrSym is null)
                return ImmutableDictionary<(string, string), ImmutableArray<BindingDescriptor>>.Empty;

            var byKey = new Dictionary<(string, string), List<BindingDescriptor>>();

            // Scan referenced assemblies.
            foreach (var asmRef in compilation.SourceModule.ReferencedAssemblySymbols)
                Walk(asmRef.GlobalNamespace);
            // Scan the compilation's own source.
            Walk(compilation.Assembly.GlobalNamespace);

            return byKey.ToImmutableDictionary(
                kv => kv.Key,
                kv => kv.Value.ToImmutableArray());

            void Walk(INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                {
                    if (member is INamespaceSymbol child) Walk(child);
                    else if (member is INamedTypeSymbol type) WalkType(type);
                }
            }

            void WalkType(INamedTypeSymbol type)
            {
                foreach (var nested in type.GetTypeMembers()) WalkType(nested);
                foreach (var member in type.GetMembers())
                {
                    if (member is not IMethodSymbol method) continue;
                    if (!method.IsStatic) continue;
                    foreach (var attr in method.GetAttributes())
                    {
                        if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrSym))
                            continue;
                        if (attr.ConstructorArguments.Length != 2) continue;
                        if (attr.ConstructorArguments[0].Value is not string mod) continue;
                        if (attr.ConstructorArguments[1].Value is not string name) continue;
                        var key = (mod, name);
                        if (!byKey.TryGetValue(key, out var list))
                            byKey[key] = list = new List<BindingDescriptor>();
                        list.Add(new BindingDescriptor(mod, name, method));
                    }
                }
            }
        }

        /// <summary>
        /// Find every <c>[WacsTranspiledImports("Ns.IImports")]</c> entry
        /// across the compilation's referenced assemblies + own source, and
        /// resolve each name back to its INamedTypeSymbol. The attribute's
        /// arg is a string (not <c>typeof(T)</c>) so Lokad.ILPack can
        /// serialize the attribute blob without resolving a self-referential
        /// Type — see WacsTranspiledImportsAttribute.cs.
        /// </summary>
        private static ImmutableArray<INamedTypeSymbol> CollectTranspiledImports(Compilation compilation)
        {
            var attrSym = compilation.GetTypeByMetadataName(WacsTranspiledImportsAttrFqn);
            if (attrSym is null) return ImmutableArray<INamedTypeSymbol>.Empty;

            var found = new List<INamedTypeSymbol>();

            void Scan(IAssemblySymbol asm)
            {
                foreach (var attr in asm.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrSym))
                        continue;
                    if (attr.ConstructorArguments.Length != 1) continue;
                    var argVal = attr.ConstructorArguments[0].Value;
                    if (argVal is string fqn)
                    {
                        // Resolve in the carrying assembly first (the common
                        // case — saved transpiler .dll naming its own
                        // IImports). Falls back to the whole compilation
                        // if the type lives elsewhere.
                        var iface = asm.GetTypeByMetadataName(fqn)
                                    ?? compilation.GetTypeByMetadataName(fqn);
                        if (iface != null) found.Add(iface);
                    }
                    else if (argVal is INamedTypeSymbol typeArg)
                    {
                        // Fallback: tolerate legacy typeof()-shaped attrs.
                        found.Add(typeArg);
                    }
                }
            }

            Scan(compilation.Assembly);
            foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
                Scan(refAsm);

            return found.ToImmutableArray();
        }

        // -----------------------------------------------------------------
        // Emission
        // -----------------------------------------------------------------

        private static void EmitOneAdapter(
            SourceProductionContext spc,
            INamedTypeSymbol importsInterface,
            ImmutableDictionary<(string Module, string Name), ImmutableArray<BindingDescriptor>> bindings)
        {
            // Discover all shared-state types across this interface's methods'
            // matched bindings, so the generated ctor can request them once.
            var stateTypes = new List<ITypeSymbol>();
            var resolvedMethods = new List<(IMethodSymbol Iface, BindingDescriptor? Binding, ITypeSymbol[] StateParams)>();

            foreach (var ifaceMethod in importsInterface.GetMembers().OfType<IMethodSymbol>())
            {
                if (ifaceMethod.MethodKind != MethodKind.Ordinary) continue;
                var (mod, name) = ParseImportModuleAndName(ifaceMethod);
                if (!bindings.TryGetValue((mod, name), out var matches))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.UnmatchedImport, location: null,
                        mod, name, ifaceMethod.Name));
                    resolvedMethods.Add((ifaceMethod, null, Array.Empty<ITypeSymbol>()));
                    continue;
                }
                if (matches.Length > 1)
                {
                    var locations = string.Join(", ", matches.Select(m =>
                        m.Method.ContainingType.ToDisplayString() + "." + m.Method.Name));
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.AmbiguousBinding, location: null,
                        mod, name, locations));
                    resolvedMethods.Add((ifaceMethod, null, Array.Empty<ITypeSymbol>()));
                    continue;
                }
                var binding = matches[0];

                // Validate: binding params = (WacsHostMemory, [state0..stateN-1], wasm0..wasmK-1)
                // where wasm0..wasmK-1 type-match ifaceMethod's params in order.
                var bp = binding.Method.Parameters;
                if (bp.Length < 1 || bp[0].Type.ToDisplayString() != WacsHostMemoryFqn)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.SignatureMismatch, location: null,
                        mod, name, binding.Method.Name, ifaceMethod.Name,
                        FormatParamTypes(ifaceMethod.Parameters),
                        FormatParamTypes(binding.Method.Parameters)));
                    resolvedMethods.Add((ifaceMethod, null, Array.Empty<ITypeSymbol>()));
                    continue;
                }

                int wasmArgCount = ifaceMethod.Parameters.Length;
                int stateCount = bp.Length - 1 - wasmArgCount;
                if (stateCount < 0)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.SignatureMismatch, location: null,
                        mod, name, binding.Method.Name, ifaceMethod.Name,
                        FormatParamTypes(ifaceMethod.Parameters),
                        FormatParamTypes(binding.Method.Parameters)));
                    resolvedMethods.Add((ifaceMethod, null, Array.Empty<ITypeSymbol>()));
                    continue;
                }

                // Trailing wasm args type-match.
                bool ok = true;
                for (int i = 0; i < wasmArgCount; i++)
                {
                    var bindingParam = bp[1 + stateCount + i].Type;
                    var ifaceParam = ifaceMethod.Parameters[i].Type;
                    if (!SymbolEqualityComparer.Default.Equals(bindingParam, ifaceParam))
                    {
                        ok = false; break;
                    }
                }
                if (!ok)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.SignatureMismatch, location: null,
                        mod, name, binding.Method.Name, ifaceMethod.Name,
                        FormatParamTypes(ifaceMethod.Parameters),
                        FormatParamTypes(binding.Method.Parameters)));
                    resolvedMethods.Add((ifaceMethod, null, Array.Empty<ITypeSymbol>()));
                    continue;
                }

                var stateParams = new ITypeSymbol[stateCount];
                for (int i = 0; i < stateCount; i++) stateParams[i] = bp[1 + i].Type;

                // Dedupe state types into the ctor's parameter list (one ctor
                // arg per distinct state type, regardless of how many bindings
                // need it).
                foreach (var st in stateParams)
                    if (!stateTypes.Any(t => SymbolEqualityComparer.Default.Equals(t, st)))
                        stateTypes.Add(st);

                resolvedMethods.Add((ifaceMethod, binding, stateParams));
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.MatchedBinding, location: null,
                    mod, name,
                    binding.Method.ContainingType.ToDisplayString() + "." + binding.Method.Name));
            }

            // -------------------------------------------------------------
            // Code emission
            // -------------------------------------------------------------
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable disable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using Wacs.HostBindings;");
            sb.AppendLine();

            string adapterNs = importsInterface.ContainingNamespace?.IsGlobalNamespace == true
                ? "WacsGenerated"
                : importsInterface.ContainingNamespace!.ToDisplayString() + ".WacsGenerated";
            sb.Append("namespace ").AppendLine(adapterNs);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>Auto-generated host imports adapter for "
                          + importsInterface.ToDisplayString() + ".</summary>");
            sb.Append("    public sealed class GeneratedHostImports : ")
              .Append(importsInterface.ToDisplayString())
              .AppendLine();
            sb.AppendLine("    {");

            // Per-state-type fields.
            for (int i = 0; i < stateTypes.Count; i++)
                sb.Append("        private readonly ").Append(stateTypes[i].ToDisplayString())
                  .Append(' ').Append(StateFieldName(i)).AppendLine(";");
            sb.AppendLine();

            // Constructor.
            sb.Append("        public GeneratedHostImports(");
            sb.Append(string.Join(", ",
                stateTypes.Select((t, i) => t.ToDisplayString() + " state" + i)));
            sb.AppendLine(")");
            sb.AppendLine("        {");
            for (int i = 0; i < stateTypes.Count; i++)
                sb.Append("            ").Append(StateFieldName(i)).Append(" = state").Append(i).AppendLine(";");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Memory accessor — the consumer must wire this. Default impl
            // throws; embedders override via a partial method or by passing
            // a memory provider through the ctor in a future revision.
            sb.AppendLine("        /// <summary>Override to provide the live wasm linear memory at call time. " +
                          "The default throws — embedders must wire this before invoking any binding.</summary>");
            sb.AppendLine("        public Func<WacsHostMemory> MemoryProvider { get; set; }");
            sb.AppendLine("            = () => throw new InvalidOperationException(" +
                          "\"GeneratedHostImports.MemoryProvider not set — assign before invoking the wasm module.\");");
            sb.AppendLine();

            // Per-method delegating implementations.
            foreach (var (iface, binding, stateParams) in resolvedMethods)
            {
                EmitDelegate(sb, iface, binding, stateParams, stateTypes);
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            // Filename: namespace-stripped IImports name.
            var fileName = importsInterface.ContainingNamespace?.IsGlobalNamespace == true
                ? "GeneratedHostImports.g.cs"
                : importsInterface.ContainingNamespace!.ToDisplayString().Replace(".", "_")
                    + ".GeneratedHostImports.g.cs";
            spc.AddSource(fileName, sb.ToString());
        }

        private static void EmitDelegate(
            StringBuilder sb,
            IMethodSymbol iface,
            BindingDescriptor? binding,
            ITypeSymbol[] stateParams,
            List<ITypeSymbol> allStateTypes)
        {
            sb.Append("        public ").Append(iface.ReturnType.ToDisplayString()).Append(' ');
            sb.Append(iface.Name).Append('(');
            sb.Append(string.Join(", ",
                iface.Parameters.Select(p => p.Type.ToDisplayString() + " " + p.Name)));
            sb.AppendLine(")");
            sb.AppendLine("        {");

            if (binding is null)
            {
                sb.AppendLine("            throw new WacsHostFault(\"no host binding registered for "
                              + iface.Name + "\");");
            }
            else
            {
                bool isVoid = iface.ReturnsVoid;
                sb.Append("            ");
                if (!isVoid) sb.Append("return ");
                sb.Append(binding.Value.Method.ContainingType.ToDisplayString()).Append('.');
                sb.Append(binding.Value.Method.Name).Append('(');
                var args = new List<string> { "MemoryProvider()" };
                foreach (var st in stateParams)
                {
                    int idx = allStateTypes.FindIndex(t =>
                        SymbolEqualityComparer.Default.Equals(t, st));
                    args.Add(StateFieldName(idx));
                }
                foreach (var p in iface.Parameters) args.Add(p.Name);
                sb.Append(string.Join(", ", args));
                sb.AppendLine(");");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        private static string StateFieldName(int i) => "_state" + i;

        private static (string Module, string Name) ParseImportModuleAndName(IMethodSymbol m)
        {
            // The transpiler-generated IImports method names use the pattern
            // <module>_<name>, with separators sanitized to '_'. Heuristic:
            // split on the LAST '_' to get name, treat the rest as module.
            // For wasi imports: "wasi_snapshot_preview1_fd_write" → module
            // "wasi_snapshot_preview1", name "fd_write". The transpiler's
            // sanitize collapses every non-alphanumeric to '_', so this
            // heuristic isn't perfect — Phase 4b will switch to a structured
            // [WacsImportName] attribute the transpiler emits per method.
            var n = m.Name;
            // Recognize known WASI prefix as a special case for now.
            const string wasiPrefix = "wasi_snapshot_preview1_";
            if (n.StartsWith(wasiPrefix))
                return ("wasi_snapshot_preview1", n.Substring(wasiPrefix.Length));
            int lastUnderscore = n.LastIndexOf('_');
            if (lastUnderscore > 0)
                return (n.Substring(0, lastUnderscore), n.Substring(lastUnderscore + 1));
            return ("", n);
        }

        private static string FormatParamTypes(ImmutableArray<IParameterSymbol> ps)
            => string.Join(", ", ps.Select(p => p.Type.ToDisplayString()));
    }

    internal readonly struct BindingDescriptor
    {
        public string Module { get; }
        public string Name { get; }
        public IMethodSymbol Method { get; }

        public BindingDescriptor(string module, string name, IMethodSymbol method)
        {
            Module = module; Name = name; Method = method;
        }
    }
}
