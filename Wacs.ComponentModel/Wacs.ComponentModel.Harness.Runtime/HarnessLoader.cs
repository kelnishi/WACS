// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Linq;
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.ComponentModel.Harness
{
    /// <summary>
    /// Common load + export-resolution helpers that emitted harness
    /// <c>LoadFrom</c> methods call into. Centralizes the canonical-ABI
    /// boilerplate (parse the .component.wasm wrapper, extract the
    /// main core module, instantiate it on a fresh
    /// <see cref="WasmRuntime"/>, register it) so per-harness IL stays
    /// short — the emitter only has to thread through the harness-specific
    /// export names plus typed invoker creation.
    /// </summary>
    public static class HarnessLoader
    {
        /// <summary>
        /// Parse a <c>.component.wasm</c> wrapper, instantiate its first
        /// core module on a fresh runtime, and return the runtime +
        /// module instance for the emitted harness to walk for its
        /// bindings. The optional <paramref name="bindImports"/>
        /// callback runs after runtime construction but before
        /// instantiation — embedders wire WASI implementations
        /// (e.g. <c>Wacs.WASI.Preview2</c>) or import stubs through it.
        /// </summary>
        /// <param name="componentBytes">Raw <c>.component.wasm</c>
        /// bytes. The component must contain at least one core wasm
        /// module; the first is taken as the harness's binding target.</param>
        /// <param name="bindImports">Optional import-binding hook —
        /// fired between <c>new WasmRuntime()</c> and module
        /// instantiation so any host functions the module imports
        /// resolve at instantiation time.</param>
        /// <param name="registerName">Name under which the instantiated
        /// module is registered in the runtime's module map. Default
        /// "harness" — embedders that need a stable cross-call name
        /// for nested-component scenarios can override.</param>
        public static LoadedComponent Load(
            byte[] componentBytes,
            Action<WasmRuntime>? bindImports = null,
            string registerName = "harness")
        {
            if (componentBytes == null)
                throw new ArgumentNullException(nameof(componentBytes));

            ComponentModule component;
            using (var ms = new MemoryStream(componentBytes))
                component = ComponentBinaryParser.Parse(ms);

            var coreModuleBytes = component.CoreModuleBinaries.FirstOrDefault();
            if (coreModuleBytes == null)
                throw new InvalidDataException(
                    "Component contains no core wasm modules.");

            var runtime = new WasmRuntime();

            Wacs.Core.Module coreModule;
            using (var coreMs = new MemoryStream(coreModuleBytes))
                coreModule = BinaryModuleParser.ParseWasm(coreMs);

            // Canonical-ABI resource handle tables — bind new/drop/rep
            // adapters BEFORE bindImports + InstantiateModule so the
            // inner module's import resolution finds them. Without
            // this, components that declare exported resources fail
            // at instantiation: "[export]<iface>.[resource-new]<name>
            // not provided by the environment". Late-resolve dtor
            // trampolines from the freshly-built core instance.
            var (_, dtorBindings) =
                Wacs.Core.Runtime.CanonResourceBinder
                    .BindImports(runtime, coreModule);

            bindImports?.Invoke(runtime);

            var moduleInst = runtime.InstantiateModule(coreModule);
            runtime.RegisterModule(registerName, moduleInst);

            Wacs.Core.Runtime.CanonResourceBinder
                .ResolveDtorTrampolines(runtime, moduleInst, dtorBindings);

            return new LoadedComponent(runtime, moduleInst);
        }

        /// <summary>
        /// Resolve an exported memory by name, returning the live
        /// <see cref="MemoryInstance"/> from the runtime's store.
        /// Throws <see cref="InvalidDataException"/> if the module
        /// doesn't export a memory with that name — used by emitted
        /// harness IL to surface contract-shape mismatches as typed
        /// errors instead of NullReferenceException-style faults.
        /// </summary>
        public static MemoryInstance RequireMemoryExport(
            WasmRuntime runtime, ModuleInstance module, string name)
        {
            foreach (var export in module.Exports)
            {
                if (export.Name == name && export.Value is ExternalValue.Memory m)
                    return runtime.RuntimeStore[m.Address];
            }
            throw new InvalidDataException(
                $"Required memory export '{name}' not found on component's core module.");
        }

        /// <summary>
        /// Resolve an exported function by name, returning its
        /// <see cref="FuncAddr"/>. Same diagnostic shape as
        /// <see cref="RequireMemoryExport"/>: typed failure on
        /// mismatch.
        /// </summary>
        public static FuncAddr RequireFunctionExport(
            ModuleInstance module, string name)
        {
            foreach (var export in module.Exports)
            {
                if (export.Name == name && export.Value is ExternalValue.Function f)
                    return f.Address;
            }
            throw new InvalidDataException(
                $"Required function export '{name}' not found on component's core module.");
        }
    }

    /// <summary>
    /// The runtime + module-instance pair the emitted harness walks
    /// after <see cref="HarnessLoader.Load"/>. A struct-ish record
    /// rather than tuple so consumers get named accessors and a stable
    /// surface; sealed class because Module / Runtime are reference
    /// types and there's no value in struct semantics here.
    /// </summary>
    public sealed class LoadedComponent
    {
        public WasmRuntime Runtime { get; }
        public ModuleInstance Module { get; }

        public LoadedComponent(WasmRuntime runtime, ModuleInstance module)
        {
            Runtime = runtime;
            Module = module;
        }
    }
}
