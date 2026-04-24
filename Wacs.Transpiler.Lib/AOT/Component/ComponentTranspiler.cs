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

        public ComponentTranspilationResult(
            ComponentModule component,
            IReadOnlyList<WacsCoreModule> coreModules,
            byte[]? embeddedWit)
        {
            Component = component;
            CoreModules = coreModules;
            EmbeddedWit = embeddedWit;
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

            return new ComponentTranspilationResult(component, cores, wit);
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
            string moduleName = "ComponentModule")
        {
            var parsed = Parse(componentStream);
            if (parsed.CoreModules.Count != 1)
                throw new System.InvalidOperationException(
                    "TranspileSingleModule requires exactly one embedded core "
                    + "module; got " + parsed.CoreModules.Count
                    + ". Use the lower-level Parse + per-module transpile for "
                    + "multi-module components.");

            var runtime = new WasmRuntime();
            var instance = runtime.InstantiateModule(parsed.CoreModules[0]);
            var transpiler = new ModuleTranspiler(assemblyNamespace);
            var result = transpiler.Transpile(instance, runtime, moduleName);

            // Bake the component-type:* bytes so the reverse
            // bindgen direction can recover the WIT from the
            // transpiled `.dll` without the original `.component.wasm`.
            ComponentAssemblyEmit.EmitComponentMetadataClass(
                result.ModuleBuilder, assemblyNamespace, parsed.EmbeddedWit);

            return result;
        }
    }
}
