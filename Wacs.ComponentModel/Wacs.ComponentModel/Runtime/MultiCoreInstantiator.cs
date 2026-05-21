// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using Wacs.ComponentModel.Async;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Spec-following multi-core component instantiator.
    /// Replaces the legacy "primary-only" path by walking
    /// component sections in file order, maintaining parallel
    /// core index spaces (core-func / core-instance / core-
    /// table / core-memory / core-global), and instantiating
    /// each core module declared in the core-instance section
    /// with imports drawn from prior core instances.
    ///
    /// <para>This is what makes the wit-component shim + fixup
    /// dance work end-to-end: the shim instantiates first to
    /// define its funcref table, the host's slot bindings get
    /// wired through the synthetic core-instance bundles, and
    /// the fixup module's element segment fills the table at
    /// fixup-instantiation time — letting the primary module's
    /// `call_indirect` reach the host implementations.</para>
    ///
    /// <para>See <c>docs/wacs-multi-core-instantiation-plan.md</c>
    /// for the design rationale.</para>
    /// </summary>
    internal sealed class MultiCoreInstantiator
    {
        private readonly ComponentModule _component;
        private readonly IReadOnlyList<byte[]> _coreBinaries;
        private readonly WasmRuntime _runtime;
        private readonly Action<WasmRuntime>? _configureImports;

        // Parsed core modules, indexed by core-module-idx
        // (the order they appear in the component's core-module
        // sections).
        private readonly List<Module> _coreModules = new();

        // Component-level instance space — wit-component
        // imports each external interface as a single Instance,
        // with the qualified name as the import string.
        private readonly List<string> _componentInstances = new();

        // Component-level function space → (qualifiedModule,
        // methodName). Populated by:
        //  - Function-sort imports (rare in wit-component output)
        //  - Component aliases of sort=Func from instance exports
        //    (the typical wit-component pattern: each
        //    `(alias instance-export I "method")` adds an entry
        //    pointing at imported-instance I's qualified name +
        //    the method name).
        // Canon-lower(K) reads this list at index K to find
        // which (mod, method) host binding to route the
        // resulting core-func to.
        private readonly List<(string Module, string Method)>
            _componentFuncs = new();

        // Core spaces — each entry is an ExternalValue (i.e. a
        // typed *Addr pointing into the runtime's Store).
        private readonly List<ExternalValue> _coreFuncs = new();
        private readonly List<ExternalValue> _coreTables = new();
        private readonly List<ExternalValue> _coreMemories = new();
        private readonly List<ExternalValue> _coreGlobals = new();

        // Core-instance space. Each slot is a dictionary of
        // exportName → ExternalValue. Both real (ModuleInstance)
        // and virtual (InstantiateCoreInline) instances surface
        // through this uniform shape — the caller only needs
        // export lookups, not the underlying ModuleInstance.
        private readonly List<Dictionary<string, ExternalValue>>
            _coreInstances = new();

        // Primary core's ModuleInstance once we've walked far
        // enough to instantiate it.
        public ModuleInstance? PrimaryInstance { get; private set; }

        public AsyncDispatcher? Dispatcher { get; private set; }

        public MultiCoreInstantiator(
            ComponentModule component,
            IReadOnlyList<byte[]> coreBinaries,
            WasmRuntime runtime,
            Action<WasmRuntime>? configureImports)
        {
            _component = component;
            _coreBinaries = coreBinaries;
            _runtime = runtime;
            _configureImports = configureImports;
        }

        private static readonly bool TraceEnabled =
            System.Environment.GetEnvironmentVariable(
                "WACS_TRACE_MULTI_CORE") == "1";

        private static void Trace(string msg)
        {
            if (TraceEnabled)
                System.Console.Error.WriteLine($"[multi-core] {msg}");
        }

        public void Run()
        {
            // ParseCustomNames is opt-in process-wide; the
            // canon-async shim still uses module-name to
            // identify itself, and IsShimModule's structural
            // fallback covers stripped name sections.
            var prevParseNames = BinaryModuleParser.ParseCustomNames;
            BinaryModuleParser.ParseCustomNames = true;
            try
            {
                PreParseAllCoreModules();
                Trace($"PreParse: {_coreModules.Count} core modules");
                BindHostImports();
                Trace($"BindHostImports done, dispatcher={Dispatcher != null}");
                WalkSections();
                Trace($"WalkSections done. " +
                    $"coreFuncs={_coreFuncs.Count}, " +
                    $"coreInstances={_coreInstances.Count}, " +
                    $"primary={PrimaryInstance != null}");
            }
            finally
            {
                BinaryModuleParser.ParseCustomNames = prevParseNames;
            }
        }

        private void PreParseAllCoreModules()
        {
            foreach (var bytes in _coreBinaries)
            {
                using var ms = new MemoryStream(bytes);
                _coreModules.Add(BinaryModuleParser.ParseWasm(ms));
            }
        }

        // Host imports are configured first so canon-lower
        // entries (which produce core-funcs that *reference* the
        // host import binding by its (module, name) pair) can
        // resolve through the runtime's entity table during the
        // section walk.
        private void BindHostImports()
        {
            // Async dispatcher gets created if the component has
            // ANY canon-async entry. The dispatcher's typed
            // helper bindings are wired by CanonAsyncBinder
            // (which uses the placeholder "[canon]" namespace)
            // and WitBindgenScaffoldingBinder (which walks each
            // core module's imports and binds at the primary's
            // actual ($root, [waitable-set-poll])-style names).
            if (ComponentInstance.HasAnyCanonAsync(_component.Canons))
            {
                Dispatcher = new AsyncDispatcher
                {
                    Types = _component.Types,
                };
                CanonAsyncBinder.BindImports(
                    _runtime, _component.Canons, Dispatcher);

                // Walk every core module's imports for the
                // wit-bindgen-rt scaffolding shape — covers the
                // [waitable-join] / [context-get-N] /
                // [stream-write-N] etc names the primary AND
                // the synthetic instances reference. Skips
                // imports that are already bound, so it
                // doesn't clobber existing bindings.
                foreach (var module in _coreModules)
                {
                    WitBindgenScaffoldingBinder.BindImports(
                        _runtime, module, Dispatcher);
                }
            }

            _configureImports?.Invoke(_runtime);
        }

        private void WalkSections()
        {
            foreach (var section in _component.RawSections)
            {
                switch (section.Id)
                {
                    case ComponentSectionId.Import:
                        ProcessImportSection(section);
                        break;
                    case ComponentSectionId.CoreInstance:
                        ProcessCoreInstanceSection(section);
                        break;
                    case ComponentSectionId.Alias:
                        ProcessAliasSection(section);
                        break;
                    case ComponentSectionId.Canon:
                        ProcessCanonSection(section);
                        break;
                    // Other sections (Type, Instance, Export,
                    // Component, CoreModule, CoreType, …) don't
                    // affect core spaces directly for our use
                    // case. Component-level exports / types are
                    // consulted on demand by canon entries.
                }
            }
        }

        private void ProcessImportSection(RawComponentSection section)
        {
            var entries = ImportSectionReader.Decode(section.Payload);
            foreach (var entry in entries)
            {
                switch (entry.Sort)
                {
                    case ComponentSort.Instance:
                        // Track the imported instance's
                        // qualified name. Component aliases of
                        // sort=Func target this instance by
                        // index + export name to build the
                        // component-func space.
                        _componentInstances.Add(entry.Name);
                        break;
                    case ComponentSort.Func:
                        // Rare in wit-component output — direct
                        // function import. The component-func
                        // name is the import name; we have no
                        // separate module / method split, so
                        // store the full name in both fields
                        // and let downstream resolution treat
                        // it appropriately.
                        _componentFuncs.Add((entry.Name, entry.Name));
                        break;
                    // Type / Component / Value / Module imports
                    // don't populate the component-func space.
                }
            }
        }

        private void ProcessCoreInstanceSection(RawComponentSection section)
        {
            var entries = CoreInstanceSectionReader.Decode(section.Payload);
            foreach (var entry in entries)
            {
                switch (entry)
                {
                    case InstantiateCoreModule ic:
                        InstantiateCore(ic);
                        break;
                    case InstantiateCoreInline ii:
                        BuildInlineCoreInstance(ii);
                        break;
                }
            }
        }

        private void InstantiateCore(InstantiateCoreModule entry)
        {
            if (entry.ModuleIdx >= _coreModules.Count)
                throw new InvalidDataException(
                    $"core module index {entry.ModuleIdx} out of " +
                    $"range (have {_coreModules.Count} modules)");
            var module = _coreModules[(int)entry.ModuleIdx];

            // For each arg, rebind that source instance's
            // exports under the import-module name the target
            // expects. ExternalValue carries the address — we
            // alias the same address (no fresh allocation)
            // under the new (module, exportName) key.
            foreach (var arg in entry.Args)
            {
                if (arg.InstanceIdx >= _coreInstances.Count)
                    throw new InvalidDataException(
                        $"core instance index {arg.InstanceIdx} " +
                        "out of range");
                var source = _coreInstances[(int)arg.InstanceIdx];
                foreach (var kv in source)
                    BindExternalAtName(arg.Name, kv.Key, kv.Value);
            }

            Trace($"  InstantiateCore(moduleIdx={entry.ModuleIdx}, " +
                $"args={entry.Args.Count})");
            var instance = _runtime.InstantiateModule(module,
                new RuntimeOptions
                {
                    MemoryStorage = AmbientRuntime.MemoryStorage,
                });

            // Record this instance's exports under its
            // core-instance-idx so future args / aliases /
            // inline-instances can find them.
            var exports = new Dictionary<string, ExternalValue>(
                StringComparer.Ordinal);
            foreach (var export in instance.Exports)
                exports[export.Name] = export.Value;
            _coreInstances.Add(exports);

            // The first instantiate-core-module entry of the
            // primary module's index is the body the lift
            // adapter invokes. Track the latest one — if there
            // are multiple instantiations of the same primary
            // module (rare), the last wins.
            //
            // Primary identity is decided up-front via the
            // existing FindPrimaryCoreModuleIdx; that's a
            // module-idx, not an instance-idx, so we compare
            // here.
            var primaryIdx = ComponentInstance
                .FindPrimaryCoreModuleIdx(_component);
            if (primaryIdx.HasValue && entry.ModuleIdx == primaryIdx.Value)
                PrimaryInstance = instance;
        }

        private void BuildInlineCoreInstance(InstantiateCoreInline entry)
        {
            var exports = new Dictionary<string, ExternalValue>(
                StringComparer.Ordinal);
            foreach (var export in entry.Exports)
            {
                var value = ResolveCoreItem(export.Sort, export.Index);
                if (value != null)
                    exports[export.Name] = value;
            }
            _coreInstances.Add(exports);
        }

        // Resolve a (sort, idx) reference against the current
        // core spaces. Returns null if the entry isn't tracked
        // yet — happens for unhandled sorts; the caller skips
        // the inline export rather than aborting.
        private ExternalValue? ResolveCoreItem(CoreSort sort, uint idx)
        {
            switch (sort)
            {
                case CoreSort.Func:
                    if (idx < _coreFuncs.Count)
                        return _coreFuncs[(int)idx];
                    return null;
                case CoreSort.Table:
                    if (idx < _coreTables.Count)
                        return _coreTables[(int)idx];
                    return null;
                case CoreSort.Memory:
                    if (idx < _coreMemories.Count)
                        return _coreMemories[(int)idx];
                    return null;
                case CoreSort.Global:
                    if (idx < _coreGlobals.Count)
                        return _coreGlobals[(int)idx];
                    return null;
                default:
                    return null;
            }
        }

        private void ProcessAliasSection(RawComponentSection section)
        {
            var entries = AliasSectionReader.Decode(section.Payload);
            foreach (var entry in entries)
            {
                if (entry.Sort == AliasSort.CoreSort)
                {
                    ProcessCoreAlias(entry);
                }
                else if (entry.Sort == AliasSort.Func)
                {
                    // Component-level Func alias from an
                    // imported instance's export. Adds to the
                    // component-func space at the next slot,
                    // pointing at (imported-instance-name,
                    // method-name) — the host has bound there.
                    if (entry.TargetKind
                            == AliasTargetKind.ComponentInstanceExport
                        && entry.InstanceIdx.HasValue
                        && entry.ExportName != null
                        && entry.InstanceIdx.Value
                            < _componentInstances.Count)
                    {
                        var modName = _componentInstances[
                            (int)entry.InstanceIdx.Value];
                        _componentFuncs.Add((modName, entry.ExportName));
                    }
                    else
                    {
                        // Outer aliases or out-of-range
                        // instance references: pad with a
                        // placeholder so the index counter
                        // stays aligned.
                        _componentFuncs.Add(
                            (string.Empty, string.Empty));
                    }
                }
            }
        }

        private void ProcessCoreAlias(ComponentAliasEntry entry)
        {
            if (entry.TargetKind != AliasTargetKind.CoreInstanceExport)
                return;
            if (!entry.InstanceIdx.HasValue || entry.ExportName == null)
                return;
            if (entry.InstanceIdx.Value >= _coreInstances.Count) return;

            var source = _coreInstances[(int)entry.InstanceIdx.Value];
            if (!source.TryGetValue(entry.ExportName, out var val))
                return;

            switch (entry.CoreKind)
            {
                case CoreAliasKind.Func:
                    _coreFuncs.Add(val);
                    break;
                case CoreAliasKind.Table:
                    _coreTables.Add(val);
                    break;
                case CoreAliasKind.Memory:
                    _coreMemories.Add(val);
                    break;
                case CoreAliasKind.Global:
                    _coreGlobals.Add(val);
                    break;
                // Module / Instance / Type sorts don't surface
                // here — they'd track in their own spaces (not
                // yet needed by the wit-component pattern).
            }
        }

        private void ProcessCanonSection(RawComponentSection section)
        {
            var entries = CanonSectionReader.Decode(section.Payload);
            foreach (var entry in entries)
            {
                // Each canon entry (except canon-lift) produces
                // a core-func. Lift produces a component-level
                // func — counted separately.
                //
                // For canon-lower we record a reference to the
                // host's existing import binding (the host has
                // already bound at the qualified (module, name)
                // via configureImports). For canon-async builtins
                // the dispatcher's typed delegate is the source.
                switch (entry)
                {
                    case CanonLift _:
                        // Component-level — no core-func slot.
                        break;
                    case CanonLower lower:
                        _coreFuncs.Add(
                            ResolveCanonLower(lower)
                            ?? PlaceholderFunc());
                        break;
                    default:
                        // Canon-async builtins were bound by
                        // CanonAsyncBinder.BindImports at the
                        // dispatcher's typed (module, name)
                        // — locate the FuncAddr via the dispatcher
                        // op-name lookup once the binder
                        // surface lands. Until then, push a
                        // placeholder so the index counter
                        // matches.
                        _coreFuncs.Add(
                            ResolveCanonAsyncBuiltin(entry)
                            ?? PlaceholderFunc());
                        break;
                }
            }
        }

        private ExternalValue? ResolveCanonLower(CanonLower lower)
        {
            // CanonLower(componentFuncIdx=K) produces a core-func
            // whose body invokes the component-level func at K.
            // Look up component-func K in our tracked space:
            // the alias / import that put K there told us which
            // (module, methodName) host binding the resulting
            // core-func should route to.
            if (lower.FuncIdx >= _componentFuncs.Count) return null;
            var (mod, method) = _componentFuncs[(int)lower.FuncIdx];
            if (string.IsNullOrEmpty(mod) || string.IsNullOrEmpty(method))
                return null;
            if (_runtime.TryGetExportedFunction((mod, method), out var addr))
                return new ExternalValue.Function(addr);
            // Host hasn't bound this import — record a pending
            // marker so any consuming inline-export's
            // BindExternalAtName surfaces a useful diagnostic
            // (rather than silently mis-routing).
            return new PendingCanonLower(mod, method);
        }

        private ExternalValue? ResolveCanonAsyncBuiltin(CanonEntry entry)
        {
            // CanonAsyncBinder.BindImports has registered the
            // dispatcher delegates at (module, name) pairs
            // derived from each canon-async entry's op + type
            // index. We'd look them up here to populate the
            // core-func slot. Defer to a follow-up — the
            // wit-component fixtures we're targeting don't
            // alias these directly into inline-instance
            // exports; they import them by name from the
            // $root instance, which is built differently.
            return null;
        }

        // Placeholder for canon-func slots we don't yet know
        // how to resolve. ResolveCoreItem will return null for
        // these, which the inline-export builder treats as
        // "skip this export" — surfaces the gap as a later
        // import-resolution failure during instantiation.
        private static ExternalValue PlaceholderFunc() =>
            new PendingCanonLower(string.Empty, string.Empty);

        // Sentinel for canon-lower entries whose host binding
        // wasn't found at canon-section walk time. Carries the
        // resolved (module, method) so a later diagnostic can
        // name what's missing.
        private sealed class PendingCanonLower : ExternalValue
        {
            public string QualifiedModule { get; }
            public string MethodName { get; }
            public PendingCanonLower(string mod, string method)
            {
                QualifiedModule = mod;
                MethodName = method;
            }
            public override Wacs.Core.Types.Defs.ExternalKind Type =>
                Wacs.Core.Types.Defs.ExternalKind.Function;
        }

        // Bind an ExternalValue under (moduleName, exportName)
        // so the next core-module instantiation's import
        // resolver finds it. Uses the runtime's BindExternal
        // re-aliasing API for tables / memories / globals
        // (shares the original address verbatim — element-
        // segment writes stay observable through both names).
        private void BindExternalAtName(
            string moduleName, string exportName, ExternalValue val)
        {
            switch (val)
            {
                case PendingCanonLower pending:
                    // Try a final lookup at (pending.Module,
                    // pending.MethodName). The host may have
                    // late-bound it after our canon-section
                    // walk recorded the placeholder.
                    if (!string.IsNullOrEmpty(pending.QualifiedModule)
                        && _runtime.TryGetExportedFunction(
                            (pending.QualifiedModule,
                             pending.MethodName),
                            out var hostAddr))
                    {
                        var hostFi = _runtime.GetFunction(hostAddr);
                        _runtime.BindHostFunction(
                            (moduleName, exportName), hostFi);
                    }
                    else
                    {
                        Trace($"    UNRESOLVED canon-lower under " +
                            $"({moduleName}, {exportName}) → " +
                            $"({pending.QualifiedModule}, " +
                            $"{pending.MethodName})");
                    }
                    return;
                default:
                    _runtime.BindExternal(
                        (moduleName, exportName), val);
                    return;
            }
        }
    }
}
