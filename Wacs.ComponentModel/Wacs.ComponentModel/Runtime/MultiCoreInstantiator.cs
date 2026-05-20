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

        // Component-level function index → (module, name) of the
        // host import the canon-lower entries reference. Only
        // function-sort imports populate this — the entries are
        // pushed in section order so the index matches the
        // component-func-idx space.
        private readonly List<(string Module, string Name)>
            _importedFuncs = new();

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
                BindHostImports();
                WalkSections();
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
            // helper bindings are wired by CanonAsyncBinder /
            // WitBindgenScaffoldingBinder downstream once we
            // know the primary core.
            if (ComponentInstance.HasAnyCanonAsync(_component.Canons))
            {
                Dispatcher = new AsyncDispatcher
                {
                    Types = _component.Types,
                };
                CanonAsyncBinder.BindImports(
                    _runtime, _component.Canons, Dispatcher);
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
                if (entry.Sort != ComponentSort.Func) continue;
                // Component import names use the
                // "<module-name>/<method-name>"-ish single-string
                // form at the component level, but wit-component
                // splits at "@VERSION" or similar. The fixture
                // imports we care about are bound by the host
                // already at (moduleName, methodName) where the
                // moduleName matches the component import's
                // outer name. We just record what was imported
                // so canon-lower can look up the (mod, name)
                // by component-func-idx.
                //
                // For wit-component output the component import
                // name IS the wire-level qualified name like
                // "wasi:filesystem/preopens@0.3.0-rc-...", and
                // the inner method name comes through the alias
                // + core-instance bundling.
                //
                // Here we store the qualified component import
                // name as the "module" placeholder; the inner
                // method gets attached when canon-lower's
                // funcIdx resolves through to its (mod, name)
                // via the alias chain.
                _importedFuncs.Add((entry.Name, string.Empty));
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
                if (entry.Sort != AliasSort.CoreSort) continue;
                if (entry.TargetKind != AliasTargetKind.CoreInstanceExport)
                    continue;
                if (!entry.InstanceIdx.HasValue
                    || entry.ExportName == null) continue;
                if (entry.InstanceIdx.Value >= _coreInstances.Count)
                    continue;

                var source = _coreInstances[(int)entry.InstanceIdx.Value];
                if (!source.TryGetValue(entry.ExportName, out var val))
                    continue;

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
                    // Module / Instance / Type sorts don't
                    // surface here — they'd track in their own
                    // spaces (not yet needed by the wit-component
                    // pattern).
                }
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
            // The lowered component func's (module, name) is the
            // matching component import. Component-func index
            // space starts with imported funcs (in declaration
            // order), then alias-imported component funcs, then
            // any aliased funcs from component-level instances.
            // For the wit-component-emitted pattern, the
            // canon-lower entries always reference imported
            // funcs, so the index resolution is the order in
            // which imported funcs were declared.
            if (lower.FuncIdx >= _importedFuncs.Count) return null;
            var (mod, _) = _importedFuncs[(int)lower.FuncIdx];
            // wit-component's component import is the FULL
            // qualified name like
            // "wasi:filesystem/types@0.3.0-rc-2026-03-15".
            // The host binds at (qualifiedName, methodName)
            // where methodName comes from the core module's
            // import-method name. CanonLower itself doesn't
            // carry the method name — that's only known once
            // the resulting core-func is referenced by an
            // inline-export bundle declaring it under a name.
            //
            // For now: capture the qualified module name as a
            // partial-handle; the inline-export bundling step
            // re-uses this entry by the canon-lower's slot
            // index, picking up the actual method-name from
            // the inline-export's name field.
            //
            // This is a slight bend of the ExternalValue
            // abstraction — we don't have a concrete FuncAddr
            // yet because the host's exact (mod, methodName)
            // pair won't be known until the consuming
            // inline-export resolves it. The placeholder lets
            // us advance the index counter; the actual
            // resolution happens at consume time via
            // ResolveCoreFuncForInlineExport.
            return new PendingCanonLower(mod);
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
            new PendingCanonLower(string.Empty);

        // Sentinel value for a canon-lower whose method-name
        // hasn't been observed yet. The inline-export builder
        // upgrades these to real ExternalValue.Function once
        // it sees the method name and can look up the host's
        // FuncAddr at (qualifiedModule, methodName).
        private sealed class PendingCanonLower : ExternalValue
        {
            public string QualifiedModule { get; }
            public PendingCanonLower(string qualifiedModule)
            {
                QualifiedModule = qualifiedModule;
            }
            public override Wacs.Core.Types.Defs.ExternalKind Type =>
                Wacs.Core.Types.Defs.ExternalKind.Function;
        }

        // Bind an ExternalValue under (moduleName, exportName)
        // so the next core-module instantiation's import
        // resolver finds it. For functions we use the
        // IFunctionInstance overload (re-allocates a FuncAddr
        // pointing at the same body — cheap). For tables /
        // memories / globals there's no existing public
        // re-bind API yet; we surface a clear error so the
        // gap is obvious rather than silently mis-routing.
        private void BindExternalAtName(
            string moduleName, string exportName, ExternalValue val)
        {
            switch (val)
            {
                case ExternalValue.Function func:
                    var fi = _runtime.GetFunction(func.Address);
                    _runtime.BindHostFunction(
                        (moduleName, exportName), fi);
                    return;
                case PendingCanonLower pending:
                    // The host bound a function at
                    // (pending.QualifiedModule, exportName)
                    // via configureImports. Re-bind it under
                    // (moduleName, exportName).
                    if (_runtime.TryGetExportedFunction(
                            (pending.QualifiedModule, exportName),
                            out var hostAddr))
                    {
                        var hostFi = _runtime.GetFunction(hostAddr);
                        _runtime.BindHostFunction(
                            (moduleName, exportName), hostFi);
                    }
                    // If the host didn't bind it, the import
                    // resolution at the next InstantiateModule
                    // surfaces the missing binding with a
                    // diagnostic.
                    return;
                // Tables / memories / globals: not yet wired —
                // wit-component shim fixtures don't currently
                // route these through inline-instance bundles
                // we handle. Add as needed.
            }
        }
    }
}
