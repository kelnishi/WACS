// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.ComponentModel.Types;
using Wacs.ComponentModel.WIT;
using Wacs.Core;
using Wacs.Core.Runtime;
using WacsCoreModule = Wacs.Core.Module;

namespace Wacs.Transpiler.AOT.Component
{
    /// <summary>
    /// Result of a component-binary transpilation pass — the
    /// parsed component plus per-inner-module transpilation
    /// results. Acts as the bridge between the component binary
    /// parser and the downstream IL-emit pipeline.
    /// </summary>
    public sealed class ComponentTranspilationResult
    {
        public ComponentModule Component { get; }

        /// <summary>
        /// Each embedded core module's parsed form — in file
        /// order. Per-module IL emission happens on demand via
        /// the standard <see cref="ModuleTranspiler"/>; this
        /// layer just holds the parsed <see cref="WacsCoreModule"/>
        /// so callers can instantiate + transpile at their
        /// cadence.
        /// </summary>
        public IReadOnlyList<WacsCoreModule> CoreModules { get; }

        /// <summary>
        /// Content of the <c>component-type:*</c> custom section,
        /// when present — a binary encoding of the component's
        /// WIT surface that <c>wasm-tools component embed</c> and
        /// <c>componentize-dotnet</c> write. The
        /// <see cref="Wacs.ComponentModel.Bindgen"/> reverse
        /// direction will decode this back to WIT text for
        /// inspection by downstream consumers of a transpiled
        /// <c>.dll</c>.
        /// </summary>
        public byte[]? EmbeddedWit { get; }

        /// <summary>
        /// Decoded form of <see cref="EmbeddedWit"/>, when the
        /// bytes parse cleanly through
        /// <see cref="BinaryWitDecoder"/>. Carries the WIT-level
        /// names (enum/record/variant/resource type names, their
        /// case/field names, the world's qualified package name)
        /// that the structural component sections drop. Null when
        /// no <c>component-type:*</c> section is present (e.g.
        /// <c>wasm-tools component new</c> output) or when the
        /// section's payload couldn't be decoded.
        /// </summary>
        public CtPackage? DecodedWit { get; }

        public ComponentTranspilationResult(
            ComponentModule component,
            IReadOnlyList<WacsCoreModule> coreModules,
            byte[]? embeddedWit,
            CtPackage? decodedWit = null)
        {
            Component = component;
            CoreModules = coreModules;
            EmbeddedWit = embeddedWit;
            DecodedWit = decodedWit;
        }
    }

    /// <summary>
    /// Phase 1b entry point: take a component binary, parse it,
    /// and hand each embedded core module back to the core-wasm
    /// transpiler pipeline. The component-level surface (WIT →
    /// C# interfaces, canonical-ABI adapters) lands in sibling
    /// emitters (<c>InterfaceEmit</c>, <c>CanonicalABIEmit</c>)
    /// as their functionality fills in.
    ///
    /// <para>Current v0 scope: binary parsing + per-inner-module
    /// parse-through. Enough to land tiny-component (trivial
    /// <c>greet() -&gt; u32</c> export with no canonical-ABI
    /// marshaling) end-to-end. Full canonical-ABI IL emit,
    /// resource handle wiring, and export trampoline generation
    /// are incremental follow-ups — each gated by the
    /// hello-world roundtrip test.</para>
    /// </summary>
    public static class ComponentTranspiler
    {
        /// <summary>
        /// Parse <paramref name="stream"/> as a component binary
        /// and pull each embedded core-module section into a
        /// <see cref="WacsCoreModule"/>. Also extracts the
        /// first <c>component-type:*</c> custom section as
        /// <see cref="ComponentTranspilationResult.EmbeddedWit"/>.
        /// </summary>
        public static ComponentTranspilationResult Parse(Stream stream)
        {
            var component = ComponentBinaryParser.Parse(stream);

            var cores = new List<WacsCoreModule>();
            foreach (var bytes in component.CoreModuleBinaries)
            {
                using var ms = new MemoryStream(bytes);
                cores.Add(BinaryModuleParser.ParseWasm(ms));
            }

            byte[]? wit = null;
            foreach (var cs in component.CustomSections)
            {
                if (cs.Name.StartsWith("component-type"))
                {
                    wit = cs.Data;
                    break;
                }
            }

            CtPackage? decoded = null;
            if (wit != null)
            {
                try { decoded = BinaryWitDecoder.DecodeComponentType(wit); }
                catch (System.FormatException) { decoded = null; }
            }

            return new ComponentTranspilationResult(component, cores, wit, decoded);
        }

        /// <summary>Convenience overload — read from a path.</summary>
        public static ComponentTranspilationResult ParseFile(string path)
        {
            using var fs = File.OpenRead(path);
            return Parse(fs);
        }

        /// <summary>
        /// Full component transpilation pass for single-core-module
        /// components (the common case — hello-world, tiny-component,
        /// any componentize-dotnet output). Parses the component,
        /// hands the embedded core module to
        /// <see cref="ModuleTranspiler"/>, and bakes the component's
        /// WIT metadata into the resulting assembly via
        /// <see cref="ComponentAssemblyEmit.EmitComponentMetadataClass"/>.
        ///
        /// <para>Multi-core-module components (nested composition,
        /// multiple inner modules) are a follow-up — each inner
        /// module needs its own type namespace within the shared
        /// assembly, and canonical-ABI adapters wire them together.</para>
        /// </summary>
        public static TranspilationResult TranspileSingleModule(
            Stream componentStream,
            string assemblyNamespace = "Wacs.Transpiled.Component",
            string moduleName = "ComponentModule",
            byte[]? componentTypeOverride = null)
        {
            var parsed = Parse(componentStream);

            // Composer mode: outer has zero core modules + at
            // least one nested component. Recursively transpile
            // each nested component into its own assembly, then
            // emit an outer ComponentExports class delegating to
            // the inner's via cross-assembly method calls. v1
            // covers the simplest shape (one nested, alias-only
            // re-exports, no instantiate args); broader cases
            // ride incrementally.
            if (parsed.CoreModules.Count == 0
                && parsed.Component.NestedComponentCount > 0)
            {
                return TranspileComposer(parsed, assemblyNamespace,
                    moduleName, componentTypeOverride);
            }

            if (parsed.CoreModules.Count != 1)
                throw new System.InvalidOperationException(
                    "TranspileSingleModule requires exactly one embedded core "
                    + "module; got " + parsed.CoreModules.Count
                    + ". Use the lower-level Parse + per-module transpile for "
                    + "multi-module components.");

            // The override path lets callers feed in a separately-
            // sourced `component-type:*` blob — useful for
            // `wasm-tools component new` output (which strips the
            // section) when the WIT-shape names are needed for the
            // emitted C# surface.
            var witBytes = componentTypeOverride ?? parsed.EmbeddedWit;
            CtPackage? decodedWit = parsed.DecodedWit;
            if (componentTypeOverride != null)
            {
                try { decodedWit = BinaryWitDecoder.DecodeComponentType(componentTypeOverride); }
                catch (System.FormatException) { decodedWit = null; }
            }

            var runtime = new WasmRuntime();
            var instance = runtime.InstantiateModule(parsed.CoreModules[0]);
            var transpiler = new ModuleTranspiler(assemblyNamespace);
            var result = transpiler.Transpile(instance, runtime, moduleName);

            // Bake the component-type:* bytes so the reverse
            // bindgen direction can recover the WIT from the
            // transpiled `.dll` without the original `.component.wasm`.
            ComponentAssemblyEmit.EmitComponentMetadataClass(
                result.ModuleBuilder, assemblyNamespace, witBytes);

            // Generate the component-level public surface — one
            // static method per exported function, typed per the
            // component's own type section (u32 vs int32 etc.).
            // When DecodedWit is available, named types
            // (enum / record / variant / resource) emit with their
            // wit-bindgen-csharp-shaped C# names.
            if (result.ExportsInterface != null && result.ModuleClass != null)
            {
                ComponentExportsEmit.EmitComponentExportsClass(
                    result.ModuleBuilder, assemblyNamespace,
                    parsed.Component, result.ExportsInterface,
                    result.ModuleClass, decodedWit);
            }

            return result;
        }

        /// <summary>
        /// Composer-mode transpilation: outer component has zero
        /// core modules + at least one nested component. Each
        /// nested component goes through <see cref="TranspileSingleModule"/>
        /// recursively into its own dynamic assembly under a sub-
        /// namespace; the outer's assembly hosts a
        /// <c>ComponentExports</c> class whose methods delegate
        /// to the matching nested ComponentExports method.
        ///
        /// <para>v1 covers (instantiate component) with no args
        /// + ComponentInstanceExport-target Func aliases — the
        /// shape <see cref="ComponentInstance"/>'s composer mode
        /// already handles. Wider composer support (instantiate
        /// args, InstantiateInline, multi-level nesting,
        /// CoreInstanceExport / Outer alias targets) ships
        /// incrementally as fixtures demand.</para>
        /// </summary>
        private static TranspilationResult TranspileComposer(
            ComponentTranspilationResult parsed,
            string assemblyNamespace,
            string moduleName,
            byte[]? componentTypeOverride)
        {
            var nested = new System.Collections.Generic.List<TranspilationResult>();
            int i = 0;
            foreach (var sub in parsed.Component.NestedComponentBinaries)
            {
                using var subStream = new MemoryStream(sub);
                var subResult = TranspileSingleModule(
                    subStream,
                    assemblyNamespace + ".Nested" + i,
                    moduleName + "Nested" + i);
                nested.Add(subResult);
                i++;
            }

            // Resolve outer component-func indices through the
            // alias chain to (nested-instance idx, export name)
            // pairs. Mirrors the interpreter's composer-mode
            // resolver in ComponentInstance.InstantiateComposer.
            var instances = new System.Collections.Generic.List<int>();
            foreach (var inst in parsed.Component.Instances)
            {
                if (inst is InstantiateComponent ic)
                {
                    if (ic.Args.Count != 0)
                        throw new System.NotSupportedException(
                            "Composer-mode transpile: instantiate "
                            + "with args is a follow-up.");
                    instances.Add((int)ic.ComponentIdx);
                }
                else
                {
                    throw new System.NotSupportedException(
                        "Composer-mode transpile: InstantiateInline "
                        + "is a follow-up.");
                }
            }

            var componentFuncResolver =
                new System.Collections.Generic.Dictionary<uint,
                    (TranspilationResult Inner, string ExportName)>();
            uint funcIdx = 0;
            foreach (var a in parsed.Component.Aliases)
            {
                if (!a.IsComponentFunc) continue;
                if (a.TargetKind != AliasTargetKind.ComponentInstanceExport
                    || !a.InstanceIdx.HasValue
                    || a.InstanceIdx.Value >= instances.Count)
                    throw new System.NotSupportedException(
                        "Composer-mode transpile: only Func aliases "
                        + "targeting ComponentInstanceExport with a "
                        + "valid instance idx are supported in v1.");
                var nestedIdx = instances[(int)a.InstanceIdx.Value];
                componentFuncResolver[funcIdx] = (
                    nested[nestedIdx], a.ExportName!);
                funcIdx++;
            }

            // Build the outer assembly. No core module to feed
            // through ModuleTranspiler — just stand up an empty
            // dynamic assembly to host the ComponentExports +
            // ComponentMetadata classes.
            var counter = System.Threading.Interlocked.Increment(
                ref _composerAssemblyCounter);
            var asmName = new System.Reflection.AssemblyName(
                assemblyNamespace + "." + moduleName + "_composer_" + counter);
            var asm = System.Reflection.Emit.AssemblyBuilder
                .DefineDynamicAssembly(asmName,
                    System.Reflection.Emit.AssemblyBuilderAccess.Run);
            var module = asm.DefineDynamicModule(asmName.Name!);

            // Emit outer ComponentMetadata if WIT bytes are
            // available (override > parsed > none).
            var witBytes = componentTypeOverride ?? parsed.EmbeddedWit;
            ComponentAssemblyEmit.EmitComponentMetadataClass(
                module, assemblyNamespace, witBytes);

            // Emit outer ComponentExports — one method per outer
            // export, body delegating to the resolved nested
            // ComponentExports method.
            EmitComposerExportsClass(module, assemblyNamespace,
                parsed.Component, componentFuncResolver);

            // Composer mode has no core module, so most fields of
            // TranspilationResult are null/empty. The Assembly +
            // ModuleBuilder are real — that's what tests probe.
            return new TranspilationResult(
                assembly: asm,
                moduleBuilder: module,
                functionsType: null!,
                methods: System.Array.Empty<System.Reflection.MethodInfo>(),
                manifest: new ModuleMetadata.Manifest
                {
                    ModuleName = moduleName,
                    Namespace = assemblyNamespace,
                },
                functionMethodMap: new System.Collections.Generic.Dictionary<int, System.Reflection.MethodInfo>(),
                diagnostics: System.Array.Empty<TranspilerDiagnostic>(),
                exportsInterface: null,
                importsInterface: null,
                moduleClass: null,
                exportMethods: System.Array.Empty<InterfaceMethod>(),
                importMethods: System.Array.Empty<InterfaceMethod>());
        }

        private static int _composerAssemblyCounter;

        /// <summary>Emit the outer composer's <c>ComponentExports</c>
        /// class. Each method takes the same args + return type as
        /// the corresponding nested ComponentExports method
        /// (resolved via <paramref name="resolver"/>) and tail-calls
        /// it via cross-assembly <c>call</c> — the dynamic-assembly
        /// CLR resolves the MethodInfo at JIT time.</summary>
        private static void EmitComposerExportsClass(
            System.Reflection.Emit.ModuleBuilder module,
            string @namespace,
            ComponentModule outer,
            System.Collections.Generic.Dictionary<uint,
                (TranspilationResult Inner, string ExportName)> resolver)
        {
            var typeName = @namespace + ".ComponentExports";
            var typeBuilder = module.DefineType(
                typeName,
                System.Reflection.TypeAttributes.Public
                    | System.Reflection.TypeAttributes.Abstract
                    | System.Reflection.TypeAttributes.Sealed);

            foreach (var ex in outer.Exports)
            {
                if (ex.Sort != ComponentSort.Func) continue;
                if (!resolver.TryGetValue(ex.Index, out var target))
                    continue;

                var innerExportsType = target.Inner.Assembly.GetType(
                    target.Inner.ModuleBuilder.Name + ".ComponentExports")
                    ?? FindComponentExportsType(target.Inner);
                if (innerExportsType == null)
                    throw new System.InvalidOperationException(
                        "Composer-mode transpile: nested assembly is "
                        + "missing a ComponentExports class for "
                        + "alias target '" + target.ExportName + "'.");

                var innerMethodName = PascalCase(target.ExportName);
                var innerMethod = innerExportsType.GetMethod(innerMethodName,
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Static);
                if (innerMethod == null)
                    throw new System.InvalidOperationException(
                        "Composer-mode transpile: nested ComponentExports "
                        + "has no public static method '" + innerMethodName
                        + "' for alias target.");

                var paramInfos = innerMethod.GetParameters();
                var paramTypes = new System.Type[paramInfos.Length];
                for (int p = 0; p < paramInfos.Length; p++)
                    paramTypes[p] = paramInfos[p].ParameterType;

                var outerName = PascalCase(ex.Name);
                var mb = typeBuilder.DefineMethod(
                    outerName,
                    System.Reflection.MethodAttributes.Public
                        | System.Reflection.MethodAttributes.Static,
                    innerMethod.ReturnType,
                    paramTypes);
                for (int p = 0; p < paramInfos.Length; p++)
                    mb.DefineParameter(p + 1,
                        System.Reflection.ParameterAttributes.None,
                        paramInfos[p].Name);

                var il = mb.GetILGenerator();
                for (int p = 0; p < paramInfos.Length; p++)
                    il.Emit(System.Reflection.Emit.OpCodes.Ldarg, p);
                il.EmitCall(System.Reflection.Emit.OpCodes.Call,
                    innerMethod, null);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
            }

            typeBuilder.CreateType();
        }

        /// <summary>Walk the nested assembly's types looking for
        /// the ComponentExports class. The expected name is
        /// <c>{namespace}.ComponentExports</c>; this fallback
        /// handles cases where the namespace bookkeeping in
        /// <see cref="TranspilationResult"/> doesn't match the
        /// assembly's runtime layout exactly.</summary>
        private static System.Type? FindComponentExportsType(
            TranspilationResult inner)
        {
            foreach (var t in inner.Assembly.GetTypes())
                if (t.Name == "ComponentExports") return t;
            return null;
        }

        /// <summary>WIT name → C# PascalCase. Mirrors the helper
        /// in ComponentExportsEmit but local copy avoids the
        /// internal-vs-private visibility wrinkle.</summary>
        private static string PascalCase(string witName)
        {
            if (string.IsNullOrEmpty(witName)) return witName;
            var sb = new System.Text.StringBuilder();
            bool capitalizeNext = true;
            foreach (var c in witName)
            {
                if (c == '-' || c == '_') { capitalizeNext = true; continue; }
                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }
            return sb.ToString();
        }
    }
}
