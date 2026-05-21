// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;

// The component-model interpreter's canonical-ABI lift/lower
// machinery uses Type.MakeGenericType / MethodInfo.MakeGenericMethod
// / Array.CreateInstance(Type, …) / Activator.CreateInstance(Type)
// per call boundary to materialize Option<T> / Result<T,E> / List<T>
// / variant arms from runtime WIT type info. This path is
// fundamentally incompatible with AOT (NativeAOT / Unity IL2CPP)
// because the generic instantiations aren't statically rooted.
//
// AOT-targeting consumers do NOT call into this file at runtime —
// they go through the build-time-transpiled typed harness shipping
// in their consumer assembly (see docs/wit-harness-plan.md). For
// non-AOT consumers (desktop dev, JIT-only) the reflective path
// works fine. The public entry points carry
// [RequiresDynamicCode] + [RequiresUnreferencedCode] so AOT
// builds emit a clear top-level error rather than runtime crashes
// inside the interior.
//
// File-level pragma over the IL codes the lift/lower paths emit.
// Anything outside lift/lower (parsing, instantiation of
// modules) does not trip these codes — they originate solely in
// the typed-generic dispatch helpers below.
#pragma warning disable IL2026, IL2067, IL2072, IL2075, IL2087, IL3050
using Wacs.ComponentModel.CanonicalABI;
using Wacs.ComponentModel.Runtime.Parser;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using WacsCoreModule = Wacs.Core.Module;

namespace Wacs.ComponentModel.Runtime
{
    /// <summary>
    /// Interpreter-side companion to the AOT transpiler — wraps a
    /// parsed <see cref="ComponentModule"/> + an instantiated
    /// <see cref="WasmRuntime"/> so callers can invoke component-
    /// level exports through the canonical ABI without ever
    /// emitting IL.
    ///
    /// <para>Parallel to <see cref="ModuleInstance"/> on the core
    /// side: holds the per-component state (the runtime backing
    /// the embedded core module(s), the canonical-ABI lift/lower
    /// machinery, eventually a resource-handle table). Phase 1c
    /// v0 scope: single-core-module components, primitive +
    /// string returns through <see cref="Invoke"/>. Aggregate
    /// returns + the typed binding-layer surfaces
    /// (<c>BindComponentInterface&lt;T&gt;</c> / etc.) layer on
    /// top in follow-ups.</para>
    ///
    /// <para><b>Why an interpreter path at all when the
    /// transpiler exists?</b> Two reasons. First, fast iteration
    /// during component development — no .dll roundtrip per
    /// fixture change. Second, dual-engine equivalence: running
    /// the same component through the interpreter AND the
    /// transpiler is the cheapest cross-check on the canonical-
    /// ABI implementation. Bug in either side surfaces as a
    /// disagreement.</para>
    /// </summary>
    public sealed class ComponentInstance
    {
        private readonly ComponentModule _component;
        private readonly WasmRuntime _runtime;
        /// <summary>Per-Invoke string encoding (set from the
        /// matching canon-lift's options). Threaded as field
        /// rather than method parameter because string lifts fan
        /// out across many helpers (return, list-of-string,
        /// option-string, result-string, variant-string-payload).
        /// Reset on each Invoke entry.</summary>
        private CanonOption.Kind _stringEncoding =
            CanonOption.Kind.StringUtf8;
        private readonly ModuleInstance _coreInstance;
        private MemoryInstance? _memory;
        private Wacs.Core.Runtime.Delegates.GenericFuncs? _cabiRealloc;
        /// <summary>Per-resource canonical-ABI handle tables (one
        /// per exported resource type). Populated during Instantiate
        /// by <see cref="Wacs.Core.Runtime.CanonResourceBinder"/>
        /// walking the core module's
        /// <c>[export]&lt;iface&gt;.[resource-X]&lt;name&gt;</c>
        /// imports. Keyed by resource name (per-instance, which is the
        /// v1 scope — multi-instance composition is a v2 follow-up).</summary>
        internal Dictionary<string, Wacs.Core.Runtime.ResourceHandleTable> _resourceTables
            = new();

        /// <summary>Component-scope async dispatcher — Phase 3
        /// surface. Holds the per-component handle tables for
        /// tasks, subtasks, waitable-sets, streams, futures, and
        /// error-contexts; the canon-async builtins route through
        /// here. Null when the component declared no async-ABI
        /// canon entries.</summary>
        public Wacs.ComponentModel.Async.AsyncDispatcher? AsyncDispatcher { get; internal set; }

        /// <summary>
        /// The underlying core <see cref="WasmRuntime"/>. Exposed
        /// so embedders can reach into the core module for
        /// exports that aren't surfaced through the component-
        /// level Invoke API (e.g., wit-component-emitted
        /// <c>[async-lift]&lt;iface&gt;#&lt;func&gt;</c> entry
        /// points, before the canon-async lift-adapter
        /// integration completes).
        /// </summary>
        public WasmRuntime CoreRuntime => _runtime;

        /// <summary>
        /// Invoke a wit-component-emitted
        /// <c>[async-lift]&lt;iface&gt;#&lt;func&gt;</c> core
        /// export through the canon-async lift adapter.
        /// Synchronously blocks on the resulting
        /// <see cref="Task{TResult}"/> and returns the
        /// dispatcher's <c>task.return</c> value (or
        /// <c>null</c> if the body returned implicitly).
        ///
        /// <para>Component-level async-lifted exports are
        /// wrapped in <c>Sort=Instance</c> by wit-component,
        /// so the public <see cref="Invoke(string, object[])"/>
        /// API doesn't reach them — that path expects
        /// <c>Sort=Func</c>. This entry point fills the gap
        /// until canon-async invoke integration into the
        /// component-level surface lands.</para>
        ///
        /// <para>Args are passed to the core function as the
        /// untyped invoker takes them. The wasm-side
        /// async-lift body sets up canon-async machinery
        /// (registers a task, runs the body, calls
        /// <c>task.return</c> on completion); the lift
        /// adapter unwraps that and surfaces the value.</para>
        /// </summary>
        public object? InvokeCoreAsyncLift(
            string coreExportName, params object?[] args)
        {
            if (string.IsNullOrEmpty(coreExportName))
                throw new ArgumentException(
                    "Core export name must not be empty.",
                    nameof(coreExportName));
            if (AsyncDispatcher == null)
                throw new InvalidOperationException(
                    "Component has no AsyncDispatcher — the " +
                    "canon section had no async entries. " +
                    "InvokeCoreAsyncLift only applies to " +
                    "components with async-lifted exports.");
            if (!_runtime.TryGetExportedFunction(
                    coreExportName, out var addr))
                throw new ArgumentException(
                    $"Core module has no export '{coreExportName}'.",
                    nameof(coreExportName));

            // Allocate a placeholder ContInstance. The lift
            // adapter uses it to register a ComponentTask; the
            // continuation itself isn't executed (the body
            // runs synchronously on the CLR stack).
            var contStore = new Wacs.Core.Runtime.Concurrency.ContinuationStore();
            var continuation = contStore.Allocate(
                (Wacs.Core.Types.TypeIdx)0,
                (Delegate)(Func<int>)(() => 0));

            var hostTask = Wacs.ComponentModel.Async.AsyncLiftAdapter
                .InvokeAsync(AsyncDispatcher, continuation, () =>
                {
                    var invoker = _runtime.CreateInvoker(addr,
                        new InvokerOptions());
                    var rawArgs = args ?? Array.Empty<object?>();
                    var boxedArgs = new object[rawArgs.Length];
                    for (int i = 0; i < rawArgs.Length; i++)
                        boxedArgs[i] = rawArgs[i] ?? 0;
                    invoker(boxedArgs);
                });
            return hostTask.GetAwaiter().GetResult();
        }

        private ComponentInstance(
            ComponentModule component,
            WasmRuntime runtime,
            ModuleInstance coreInstance)
        {
            _component = component;
            _runtime = runtime;
            _coreInstance = coreInstance;
            _runtime.TryGetExportedMemory("memory", out _memory!);
        }

        /// <summary>
        /// Parse + instantiate a component binary in one pass.
        ///
        /// <para>NOT AOT-compatible: lift/lower at component-import/
        /// export boundaries uses runtime generic specialization
        /// (Type.MakeGenericType / MethodInfo.MakeGenericMethod /
        /// Activator.CreateInstance). NativeAOT / IL2CPP consumers
        /// should use the build-time-transpiled typed harness
        /// surface instead — see docs/wit-harness-plan.md.</para>
        /// </summary>
        [RequiresDynamicCode("Component-model lift/lower instantiates " +
            "Option<T> / Result<T,E> / list<T> / variant arms via " +
            "Type.MakeGenericType at runtime from the component's WIT " +
            "shape. AOT consumers must use the build-time typed " +
            "harness surface instead.")]
        [RequiresUnreferencedCode("Reflective lift/lower walks types " +
            "decoded from the component binary at runtime; trimming " +
            "may remove the needed generic specializations.")]
        public static ComponentInstance Instantiate(byte[] componentBytes,
            Action<WasmRuntime>? configureImports = null)
        {
            using var ms = new MemoryStream(componentBytes);
            return Instantiate(ms, configureImports);
        }

        [RequiresDynamicCode("Component-model lift/lower instantiates " +
            "Option<T> / Result<T,E> / list<T> / variant arms via " +
            "Type.MakeGenericType at runtime from the component's WIT " +
            "shape. AOT consumers must use the build-time typed " +
            "harness surface instead.")]
        [RequiresUnreferencedCode("Reflective lift/lower walks types " +
            "decoded from the component binary at runtime; trimming " +
            "may remove the needed generic specializations.")]
        public static ComponentInstance Instantiate(Stream componentStream,
            Action<WasmRuntime>? configureImports = null)
        {
            var component = ComponentBinaryParser.Parse(componentStream);
            return Instantiate(component, configureImports);
        }

        /// <summary>Instantiate a pre-parsed component. Used both
        /// for the top-level entry and recursively for nested
        /// components when the outer is a "composer" — a wrapper
        /// that bundles + re-exports nested components without
        /// embedding its own core module.
        ///
        /// <para><paramref name="configureImports"/> is invoked
        /// after the runtime is created but before the inner core
        /// module is instantiated. Callers use it to satisfy
        /// component imports — currently via the same
        /// <c>BindHostFunction</c> API the WASIp1 host bindings
        /// use. The (module, name) pair the runtime resolves
        /// against is the <i>core</i> import that the component's
        /// canon-lower + core-instance machinery routes the
        /// component-level import through. For the common case
        /// (instantiate-with passes the host-provided instance
        /// straight through to the inner module's import) the
        /// pair matches the component-level interface name +
        /// kebab-case function name verbatim — see Phase 3 v0
        /// fixtures for the convention.</para></summary>
        [RequiresDynamicCode("Component-model lift/lower instantiates " +
            "Option<T> / Result<T,E> / list<T> / variant arms via " +
            "Type.MakeGenericType at runtime from the component's WIT " +
            "shape. AOT consumers must use the build-time typed " +
            "harness surface instead.")]
        [RequiresUnreferencedCode("Reflective lift/lower walks types " +
            "decoded from the component binary at runtime; trimming " +
            "may remove the needed generic specializations.")]
        public static ComponentInstance Instantiate(ComponentModule component,
            Action<WasmRuntime>? configureImports = null)
        {
            var coreBinaries = component.CoreModuleBinaries.ToList();
            if (coreBinaries.Count == 1)
            {
                var runtime = new WasmRuntime();
                using var coreMs = new MemoryStream(coreBinaries[0]);
                var coreModule = BinaryModuleParser.ParseWasm(coreMs);

                // Canonical-ABI resource handle tables — bind
                // new/drop/rep adapters BEFORE configureImports +
                // InstantiateModule so the inner module's import
                // resolution finds them. Late-resolve dtor
                // trampolines from the freshly-built core instance.
                // See CanonResourceBinder for the algorithm.
                var (tables, dtorBindings) =
                    Wacs.Core.Runtime.CanonResourceBinder
                        .BindImports(runtime, coreModule);

                // Canon-async builtins — bind host functions for
                // the canon entries in this component (task/subtask/
                // stream/future/error-context/waitable-set/etc.).
                // No-op when the component has no canon-async
                // entries. See CanonAsyncBinder for the placeholder
                // name convention; embedders can override via the
                // NameResolver hook when wit-component's convention
                // settles.
                Wacs.ComponentModel.Async.AsyncDispatcher? asyncDispatcher = null;
                if (HasAnyCanonAsync(component.Canons))
                {
                    asyncDispatcher = new Wacs.ComponentModel.Async.AsyncDispatcher
                    {
                        Types = component.Types,
                    };
                    Wacs.ComponentModel.Async.CanonAsyncBinder.BindImports(
                        runtime, component.Canons, asyncDispatcher);
                    // wit-bindgen-rt emits per-imported-method
                    // canon-async helper imports under
                    // <iface>.[<canon-op>-N]<funcname>. Bind
                    // them too — these aren't reached by the
                    // canon-section walk because they're core-
                    // module-local declarations.
                    Wacs.ComponentModel.Async.WitBindgenScaffoldingBinder
                        .BindImports(runtime, coreModule, asyncDispatcher);
                }

                configureImports?.Invoke(runtime);
                var coreInstance = runtime.InstantiateModule(coreModule,
                    new RuntimeOptions { MemoryStorage = AmbientRuntime.MemoryStorage });

                Wacs.Core.Runtime.CanonResourceBinder
                    .ResolveDtorTrampolines(runtime, coreInstance, dtorBindings);

                // Late-bind the dispatcher's memory + string
                // encoding from the now-instantiated core
                // instance + canon-lift options. The lift adapter
                // (for string task.return, etc.) consults these
                // at delegate-call time.
                if (asyncDispatcher != null)
                {
                    runtime.TryGetExportedMemory("memory", out var asyncMem);
                    asyncDispatcher.Memory = asyncMem;
                    asyncDispatcher.StringEncoding =
                        FindCanonLiftStringEncoding(component);
                }

                return new ComponentInstance(component, runtime, coreInstance)
                    { _resourceTables = tables, AsyncDispatcher = asyncDispatcher };
            }

            // Composer mode: no core modules of our own — every
            // export funnels through alias chains into nested
            // components. v1 supports the simplest shape:
            // (instantiate component-idx) + alias re-exports,
            // no args. Multi-arg + nested-of-nested instantiation
            // are follow-ups.
            if (coreBinaries.Count == 0
                && component.NestedComponentCount > 0)
            {
                return InstantiateComposer(component);
            }

            // Multi-core-module mode: wit-component's typical
            // output for an aggregate-typed host import — 3+
            // core modules wired via core-instance + with-clause
            // directives. v1 takes the pragmatic shortcut: trace
            // canon-lift entries back to identify the "user"
            // core module (the one whose exports are component-
            // visible), instantiate just that one, and bind the
            // host implementation directly to its imports under
            // the (module, name) pair its core import declares.
            // Skips wit-component's call_indirect adapter +
            // post-return shim — they're scaffolding around the
            // canon-lower wrapper, which we do directly in the
            // binder. Matches what most components need without
            // implementing the full multi-instance composition
            // engine.
            if (coreBinaries.Count > 1)
            {
                var primaryIdx = FindPrimaryCoreModuleIdx(component);
                if (primaryIdx.HasValue)
                    return InstantiateMultiCore(component,
                        coreBinaries, primaryIdx.Value,
                        configureImports);
            }

            throw new InvalidOperationException(
                "ComponentInstance.Instantiate requires exactly one "
                + "embedded core module OR (zero core modules + at "
                + "least one nested component) OR a multi-module "
                + "wit-component output where canon-lift traces to "
                + "a primary user module; got "
                + coreBinaries.Count + " core modules and "
                + component.NestedComponentCount + " nested components.");
        }

        /// <summary>Trace canon-lift entries through the alias +
        /// core-instance chain to find which core-module hosts
        /// the component's "user" exports. wit-component's
        /// adapter + post-return shims sit elsewhere in the
        /// module list; the canon-lifts always reference the
        /// user module's exported funcs. Returns the
        /// 0-based core-module index, or null if the trace
        /// fails (caller surfaces an InvalidOperationException
        /// since no other heuristic fits).</summary>
        /// <summary>
        /// Locate the "primary" core module in a multi-core-module
        /// component — the one whose exports are component-visible
        /// via the first canon-lift. wit-component emits adapter +
        /// post-return modules around the user module; tracing the
        /// first canon-lift back through alias→core-instance gives
        /// the user module's index. Returns null when the trace
        /// fails (unrecognized layout). Public so the transpiler
        /// can reuse the same heuristic.
        /// </summary>
        /// <summary>True iff <paramref name="canons"/> contains any
        /// Component Model async-ABI canon entry (task / subtask /
        /// stream / future / error-context / waitable-set /
        /// waitable.join / backpressure / context / thread.yield).
        /// Used to decide whether to allocate an
        /// <see cref="Wacs.ComponentModel.Async.AsyncDispatcher"/>.</summary>
        internal static bool HasAnyCanonAsync(IReadOnlyList<CanonEntry> canons)
        {
            foreach (var c in canons)
            {
                switch (c)
                {
                    case CanonTaskReturn _:
                    case CanonTaskCancel _:
                    case CanonSubtaskCancel _:
                    case CanonSubtaskDrop _:
                    case CanonBackpressureOp _:
                    case CanonContextOp _:
                    case CanonThreadYield _:
                    case CanonStreamOp _:
                    case CanonFutureOp _:
                    case CanonErrorContextOp _:
                    case CanonWaitableSetOp _:
                    case CanonWaitableJoin _:
                        return true;
                }
            }
            return false;
        }

        public static int? FindPrimaryCoreModuleIdx(ComponentModule component)
        {
            // Build core-func-idx → core-instance-idx map from
            // alias entries (the core-export form). Walk
            // RawSections in file order to track index growth
            // across canon-lower entries (which also bump the
            // core-func space).
            var coreFuncToInstance = new Dictionary<uint, uint>();
            uint coreFuncIdx = 0;
            foreach (var s in component.RawSections)
            {
                switch (s.Id)
                {
                    case ComponentSectionId.Alias:
                    {
                        var entries = AliasSectionReader.Decode(s.Payload);
                        foreach (var a in entries)
                        {
                            if (a.Sort == AliasSort.CoreSort
                                && a.CoreKind == CoreAliasKind.Func)
                            {
                                if (a.TargetKind ==
                                        AliasTargetKind.CoreInstanceExport
                                    && a.InstanceIdx.HasValue)
                                    coreFuncToInstance[coreFuncIdx] =
                                        a.InstanceIdx.Value;
                                coreFuncIdx++;
                            }
                        }
                        break;
                    }
                    case ComponentSectionId.Canon:
                    {
                        var entries = CanonSectionReader.Decode(s.Payload);
                        foreach (var e in entries)
                        {
                            // Every canon-form except `canon lift`
                            // produces a (core func) — see the
                            // canon grammar in component-model
                            // Binary.md. Counting only some
                            // mis-aligns subsequent core-alias
                            // indices when wit-component emits
                            // mixed resource + async builtins.
                            switch (e)
                            {
                                case CanonLift _:
                                    // Lift produces a *component*-
                                    // level func; doesn't bump the
                                    // core-func counter.
                                    break;
                                case CanonLower _:
                                case CanonResourceOp _:
                                case CanonTaskReturn _:
                                case CanonTaskCancel _:
                                case CanonSubtaskCancel _:
                                case CanonSubtaskDrop _:
                                case CanonBackpressureOp _:
                                case CanonContextOp _:
                                case CanonThreadYield _:
                                case CanonStreamOp _:
                                case CanonFutureOp _:
                                case CanonErrorContextOp _:
                                case CanonWaitableSetOp _:
                                case CanonWaitableJoin _:
                                    coreFuncIdx++;
                                    break;
                            }
                        }
                        break;
                    }
                }
            }

            // First canon-lift: that's the export-side anchor.
            // Its CoreFuncIdx resolves through the map above to
            // a core-instance, and that instance's
            // InstantiateCoreModule entry tells us the module.
            CanonLift? firstLift = null;
            foreach (var c in component.Canons)
                if (c is CanonLift cl) { firstLift = cl; break; }
            if (firstLift == null) return null;

            if (!coreFuncToInstance.TryGetValue(
                    firstLift.CoreFuncIdx, out var instIdx))
                return null;
            var coreInsts = component.CoreInstances;
            if (instIdx >= coreInsts.Count) return null;
            if (coreInsts[(int)instIdx] is InstantiateCoreModule ic)
                return (int)ic.ModuleIdx;
            return null;
        }

        /// <summary>Instantiate a multi-core-module component
        /// by picking the primary module, satisfying its
        /// imports via host bindings, and skipping the
        /// adapter / post-return scaffolding wit-component
        /// emits around aggregate-typed canon-lowers. The
        /// configureImports callback is the single point where
        /// callers register the host delegates that satisfy
        /// the primary module's imports — the binder writes
        /// canon-lower wrappers under those (module, name)
        /// pairs.</summary>
        private static ComponentInstance InstantiateMultiCore(
            ComponentModule component,
            List<byte[]> coreBinaries,
            int primaryIdx,
            Action<WasmRuntime>? configureImports)
        {
            var runtime = new WasmRuntime();

            // wit-component-emitted multi-core components include a
            // shim module that proxies canon-async builtin imports.
            // Recognize + bind it BEFORE the primary module
            // instantiates so its imports of the shim's exports
            // resolve through the binder.
            //
            // ParseCustomNames is opt-in process-wide; flip it on
            // for the shim-scan window then restore. The primary
            // module is parsed below with this also enabled, which
            // costs a few µs but harms nothing.
            Wacs.ComponentModel.Async.AsyncDispatcher? asyncDispatcher = null;
            var prevParseNames = BinaryModuleParser.ParseCustomNames;
            BinaryModuleParser.ParseCustomNames = true;
            try
            {
                // Scan every binary for a shim module — wit-component
                // can put it anywhere in the module list.
                for (int i = 0; i < coreBinaries.Count; i++)
                {
                    if (i == primaryIdx) continue;
                    using var ms = new MemoryStream(coreBinaries[i]);
                    var candidate = BinaryModuleParser.ParseWasm(ms);
                    if (!Wacs.ComponentModel.Async.ShimModuleRecognizer
                            .IsShimModule(candidate))
                        continue;

                    asyncDispatcher ??=
                        new Wacs.ComponentModel.Async.AsyncDispatcher
                        {
                            Types = component.Types,
                        };
                    Wacs.ComponentModel.Async.ShimModuleRecognizer
                        .BindShimImports(
                            runtime, candidate, component.Canons,
                            asyncDispatcher);
                    // Multiple shims (wit-component:shim and a
                    // matching fixups module) can appear — keep
                    // scanning so each one gets a pass.
                }

                // Parse the primary module after the shim binder
                // has registered its delegates.
                using var coreMs = new MemoryStream(coreBinaries[primaryIdx]);
                var coreModule = BinaryModuleParser.ParseWasm(coreMs);

                // Bind wit-bindgen-rt-emitted per-method canon-
                // async helper imports. These live on the
                // primary core module (not in the shim) and
                // are named (<iface>, "[<op>-N]<funcname>").
                if (asyncDispatcher != null)
                {
                    Wacs.ComponentModel.Async.WitBindgenScaffoldingBinder
                        .BindImports(runtime, coreModule, asyncDispatcher);
                }

                configureImports?.Invoke(runtime);
                var coreInstance = runtime.InstantiateModule(coreModule,
                    new RuntimeOptions
                    {
                        MemoryStorage = AmbientRuntime.MemoryStorage,
                    });

                // Late-bind dispatcher memory + encoding (same as
                // the single-core path).
                if (asyncDispatcher != null)
                {
                    runtime.TryGetExportedMemory("memory", out var asyncMem);
                    asyncDispatcher.Memory = asyncMem;
                    asyncDispatcher.StringEncoding =
                        FindCanonLiftStringEncoding(component);
                }

                return new ComponentInstance(component, runtime, coreInstance)
                {
                    AsyncDispatcher = asyncDispatcher,
                };
            }
            finally
            {
                BinaryModuleParser.ParseCustomNames = prevParseNames;
            }
        }

        /// <summary>
        /// Spec-following multi-core instantiation. Walks the
        /// component's CoreInstance / Alias / Canon sections in
        /// file order, building the parallel core spaces and
        /// instantiating each declared core-module with the
        /// imports its CoreInstance entry's args specify. Lets
        /// the wit-component shim + fixup dance run end-to-end:
        /// shim defines the funcref table, fixup's element
        /// segment fills it, primary calls through.
        ///
        /// <para>Routed via the <c>WACS_PROPER_MULTI_CORE=1</c>
        /// env var while being validated against the fixture
        /// suite. Will become the default once cli-hello +
        /// filesystem-mkdir-rmdir are both green through this
        /// path.</para>
        /// </summary>
        private static ComponentInstance InstantiateMultiCoreProper(
            ComponentModule component,
            List<byte[]> coreBinaries,
            Action<WasmRuntime>? configureImports)
        {
            var runtime = new WasmRuntime();
            var inst = new MultiCoreInstantiator(
                component, coreBinaries, runtime, configureImports);
            inst.Run();

            if (inst.PrimaryInstance == null)
                throw new InvalidOperationException(
                    "MultiCoreInstantiator produced no primary core " +
                    "instance — the component's CoreInstance section " +
                    "didn't instantiate the module identified by " +
                    "FindPrimaryCoreModuleIdx.");

            if (inst.Dispatcher != null)
            {
                runtime.TryGetExportedMemory("memory", out var mem);
                inst.Dispatcher.Memory = mem;
                inst.Dispatcher.StringEncoding =
                    FindCanonLiftStringEncoding(component);
            }

            return new ComponentInstance(component, runtime, inst.PrimaryInstance)
            {
                AsyncDispatcher = inst.Dispatcher,
            };
        }

        /// <summary>Build a composer-mode instance: recursively
        /// instantiate each nested component, then resolve the
        /// outer's instance + alias sections to map outer
        /// component-func indices through to inner functions.
        /// Invoke routes through the alias chain.</summary>
        private static ComponentInstance InstantiateComposer(
            ComponentModule component)
        {
            // Instantiate each nested component into a child
            // ComponentInstance.
            var nested = new List<ComponentInstance>();
            foreach (var sub in component.NestedComponents)
                nested.Add(Instantiate(sub));

            // Walk Instances + Aliases in file order to populate
            // the component-instance and component-func index
            // spaces. v1 only handles the shape this fixture
            // exercises: InstantiateComponent (no args) +
            // alias-export-of-instance for funcs.
            var instances = new List<ComponentInstance>();
            var componentFuncResolver =
                new Dictionary<uint, (ComponentInstance Inner, string ExportName)>();
            uint funcIdx = 0;
            foreach (var i in component.Instances)
            {
                if (i is InstantiateComponent ic)
                {
                    if (ic.Args.Count != 0)
                        throw new NotSupportedException(
                            "InstantiateComponent with args is a "
                            + "follow-up — current composer support "
                            + "only handles arg-less instantiation.");
                    if (ic.ComponentIdx >= nested.Count)
                        throw new InvalidOperationException(
                            "Instantiate references nested component "
                            + ic.ComponentIdx + " but only "
                            + nested.Count + " parsed.");
                    instances.Add(nested[(int)ic.ComponentIdx]);
                }
                else
                {
                    throw new NotSupportedException(
                        "InstantiateInline is a follow-up.");
                }
            }
            foreach (var a in component.Aliases)
            {
                if (a.IsComponentFunc)
                {
                    if (a.TargetKind !=
                            AliasTargetKind.ComponentInstanceExport)
                        throw new NotSupportedException(
                            "Non-instance-export alias targets are a "
                            + "follow-up — only ComponentInstanceExport "
                            + "is supported in composer mode v1.");
                    if (!a.InstanceIdx.HasValue
                        || a.InstanceIdx.Value >= instances.Count)
                        throw new InvalidOperationException(
                            "Alias references instance "
                            + a.InstanceIdx + " but only "
                            + instances.Count + " in scope.");
                    componentFuncResolver[funcIdx] = (
                        instances[(int)a.InstanceIdx.Value],
                        a.ExportName!);
                    funcIdx++;
                }
            }
            return new ComponentInstance(component, componentFuncResolver);
        }

        // Composer-mode constructor: no own runtime + core
        // instance; routes Invoke through nested instances via
        // _composerFuncResolver.
        private ComponentInstance(
            ComponentModule component,
            Dictionary<uint, (ComponentInstance Inner, string ExportName)>
                composerFuncResolver)
        {
            _component = component;
            _runtime = null!;        // composer mode has no runtime
            _coreInstance = null!;   // nor a core instance
            _composerFuncResolver = composerFuncResolver;
        }

        private readonly Dictionary<uint, (ComponentInstance Inner, string ExportName)>?
            _composerFuncResolver;

        /// <summary>The parsed component this instance backs —
        /// useful for callers that want to inspect declared
        /// exports / types before invoking.</summary>
        public ComponentModule Component => _component;

        /// <summary>
        /// Invoke a component-level export by name with the
        /// supplied user args. Resolves the export to its
        /// underlying canon lift, looks up the matching core
        /// function (the convention wit-component uses: core
        /// export name == component export name), lowers any
        /// aggregate args, calls through, and lifts the result
        /// back into a managed value.
        ///
        /// <para>v0 lift coverage: primitives (i32/i64/f32/f64
        /// pass-through, narrow ints + bool), string returns
        /// (StringMarshal.LiftUtf8 from the return-area). Other
        /// aggregate returns route through <see cref="WasmRuntime"/>
        /// directly — the caller gets the raw retArea pointer
        /// and can lift further via the same canonical-ABI
        /// helpers the transpiler emits IL against.</para>
        /// </summary>
        public object? Invoke(string exportName, params object?[] args)
        {
            var componentExport = _component.Exports
                .FirstOrDefault(e => e.Name == exportName
                    && e.Sort == ComponentSort.Func)
                ?? throw new ArgumentException(
                    $"No component-level Func export named '{exportName}'.",
                    nameof(exportName));

            // Composer mode: outer has no canon lifts of its own;
            // every component-func resolves through an alias into
            // a nested instance. Resolve the chain and delegate.
            if (_composerFuncResolver != null)
            {
                if (!_composerFuncResolver.TryGetValue(
                        componentExport.Index, out var target))
                    throw new InvalidOperationException(
                        "Composer-mode component-func "
                        + componentExport.Index + " (export '"
                        + exportName + "') has no resolved alias "
                        + "target; only ComponentInstanceExport "
                        + "aliases are wired in v1.");
                return target.Inner.Invoke(target.ExportName, args);
            }

            if (!_component.ComponentFuncToCanon.TryGetValue(
                    componentExport.Index, out var lift))
                throw new InvalidOperationException(
                    "Component export '" + exportName + "' has no "
                    + "matching canon lift; component may be malformed.");

            if (lift.TypeIdx >= _component.Types.Count
                || !(_component.Types[(int)lift.TypeIdx]
                        is ComponentFuncType fn))
                throw new InvalidOperationException(
                    "Canon lift for '" + exportName + "' references a "
                    + "non-function type; component may be malformed.");

            // Map the component-level export name to the core
            // export. wit-component preserves the name verbatim
            // for world-level exports, so name-based lookup works
            // for all our fixtures.
            if (!_runtime.TryGetExportedFunction(exportName, out var coreAddr))
                throw new InvalidOperationException(
                    "Core module has no export matching component-level "
                    + "export '" + exportName + "'.");

            var coreFunc = _runtime.GetFunction(coreAddr);
            var coreFuncType = coreFunc.Type;

            // The runtime's untyped invoker takes `object[]` and
            // returns `Value[]`. Boxing is fine here — Phase 1c is
            // about correctness, not throughput. (The transpiler
            // path stays the fast lane; the interpreter is the
            // checking lane.)
            // Set encoding BEFORE lowering so string-param
            // encoding tracks the canon-lift's option (UTF-8
            // default; UTF-16 picks align=2 + u16-code-unit
            // length).
            _stringEncoding = ResolveStringEncoding(lift.Options);
            var invoker = _runtime.CreateInvoker(coreAddr, new InvokerOptions());
            var coreArgs = LowerArgs(fn, args);
            var coreResults = invoker(coreArgs);

            return LiftResult(fn, coreFuncType, coreResults);
        }

        /// <summary>Pick the export's string encoding from its
        /// canon-lift options. Mirror of the transpiler's
        /// Component-wide string-encoding probe: scan canon entries
        /// for the first <see cref="CanonLift"/> and return its
        /// resolved string-encoding option. Used to bake the
        /// encoding into the
        /// <see cref="Wacs.ComponentModel.Async.AsyncDispatcher"/>'s
        /// state at instantiation time, so the canon-async lift
        /// adapter can pick the right
        /// <see cref="Wacs.ComponentModel.CanonicalABI.StringMarshal"/>
        /// variant without re-walking the canon section per call.
        ///
        /// <para>A component with multiple lifts that disagree on
        /// string-encoding is rare in practice (wit-component
        /// emits one encoding per component); when it happens,
        /// the first lift wins. Per-call refinement would require
        /// threading the option through the binder per-canon-entry,
        /// a follow-up if real fixtures surface the divergence.</para>
        /// </summary>
        internal static CanonOption.Kind FindCanonLiftStringEncoding(
            ComponentModule component)
        {
            foreach (var c in component.Canons)
            {
                if (c is CanonLift cl)
                    return ResolveStringEncoding(cl.Options);
            }
            return CanonOption.Kind.StringUtf8;
        }

        /// <summary>
        /// ResolveStringEncoding — defaults to UTF-8 when no
        /// option present.</summary>
        private static CanonOption.Kind ResolveStringEncoding(
            IReadOnlyList<CanonOption> options)
        {
            foreach (var opt in options)
            {
                switch (opt.OptionKind)
                {
                    case CanonOption.Kind.StringUtf8:
                    case CanonOption.Kind.StringUtf16:
                    case CanonOption.Kind.StringLatin1OrUtf16:
                        return opt.OptionKind;
                }
            }
            return CanonOption.Kind.StringUtf8;
        }

        // ---- Lower (managed → core wire) -----------------------------

        private object[] LowerArgs(ComponentFuncType fn, object?[] userArgs)
        {
            if (fn.Params.Count != userArgs.Length)
                throw new ArgumentException(
                    $"Expected {fn.Params.Count} args, got "
                    + $"{userArgs.Length}.");

            // Primitive params pass through; string + list<prim>
            // params route through cabi_realloc + a memcpy into
            // exported memory, then push (ptr, count) — same
            // shape the transpiler's EmitPrecomputeBufferParam
            // helpers emit IL for.
            var lowered = new List<object>(fn.Params.Count);
            for (int i = 0; i < fn.Params.Count; i++)
            {
                var pt = fn.Params[i].Type;
                if (pt.IsPrimitive)
                {
                    if (pt.Prim == ComponentPrim.String)
                    {
                        var (ptr, len) = LowerStringParam((string)userArgs[i]!);
                        lowered.Add(ptr);
                        lowered.Add(len);
                    }
                    else
                    {
                        lowered.Add(LowerPrimitive(pt.Prim, userArgs[i])!);
                    }
                }
                else if (!pt.IsPrimitive
                    && pt.TypeIdx < _component.Types.Count
                    && _component.Types[(int)pt.TypeIdx]
                        is ComponentListType list
                    && list.Element.IsPrimitive
                    && list.Element.Prim != ComponentPrim.String)
                {
                    var (ptr, count) = LowerListOfPrimParam(
                        userArgs[i], list.Element.Prim);
                    lowered.Add(ptr);
                    lowered.Add(count);
                }
                else if (!pt.IsPrimitive
                    && pt.TypeIdx < _component.Types.Count
                    && (_component.Types[(int)pt.TypeIdx]
                            is ComponentOwnType
                        || _component.Types[(int)pt.TypeIdx]
                            is ComponentBorrowType))
                {
                    // own<R> / borrow<R> param — accept the raw
                    // i32 handle from the user. Without dynamic-
                    // type emission for the resource class, this
                    // is the simplest binding shape.
                    lowered.Add(Convert.ToInt32(userArgs[i]));
                }
                else
                {
                    throw new NotSupportedException(
                        "Lower-side marshaling for this aggregate "
                        + "param shape is a follow-up. v0 covers "
                        + "primitive + string + list<prim> + "
                        + "own/borrow<R> (as int handles) params.");
                }
            }
            return lowered.ToArray();
        }

        private (int ptr, int len) LowerStringParam(string value)
        {
            if (_memory == null)
                throw new InvalidOperationException(
                    "String param lowering requires Module.Memory.");
            // Encoding picked from the per-Invoke
            // _stringEncoding field, set on Invoke entry from the
            // canon-lift options. UTF-16 uses align=2 + length-
            // in-code-units. latin1+utf16 emits as UTF-16 with
            // the high-bit tag set (canonical-ABI permits the
            // implementation to pick; Latin-1-when-fits is a
            // follow-up optimization).
            var enc = _stringEncoding;
            var isUtf16 = enc == CanonOption.Kind.StringUtf16
                || enc == CanonOption.Kind.StringLatin1OrUtf16;
            byte[] bytes;
            if (enc == CanonOption.Kind.StringUtf16)
                bytes = StringMarshal.EncodeUtf16(value);
            else if (enc == CanonOption.Kind.StringLatin1OrUtf16)
                bytes = StringMarshal.EncodeLatin1OrUtf16(value);
            else
                bytes = StringMarshal.EncodeUtf8(value);
            var align = isUtf16 ? 2 : 1;
            var ptr = CabiRealloc(0, 0, align, bytes.Length);
            StringMarshal.CopyToGuest(bytes, _memory, ptr);
            int len = isUtf16 ? bytes.Length / 2 : bytes.Length;
            if (enc == CanonOption.Kind.StringLatin1OrUtf16)
                len = (int)((uint)len | StringMarshal.Latin1OrUtf16Tag);
            return (ptr, len);
        }

        private (int ptr, int count) LowerListOfPrimParam(
            object? userArg, ComponentPrim elemPrim)
        {
            if (_memory == null)
                throw new InvalidOperationException(
                    "List param lowering requires Module.Memory.");
            if (userArg is not Array arr)
                throw new ArgumentException(
                    "list<" + elemPrim + "> param requires a "
                    + "compatible array argument.");
            var elemSize = PrimByteSize(elemPrim);
            var count = arr.Length;
            var byteLen = count * elemSize;
            var ptr = CabiRealloc(0, 0, elemSize, byteLen);

            // Closed-generic dispatch over ListMarshal.CopyArrayToGuest
            // — same MakeGenericMethod pattern the transpiler IL
            // uses, but at runtime via reflection.
            var elemCs = PrimToCs(elemPrim);
            var copyOpen = typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.CopyArrayToGuest))!;
            var copyClosed = copyOpen.MakeGenericMethod(elemCs);
            // Coerce the raw array to the closed element type if
            // the user passed e.g. uint[] for a u32 list — the
            // helper's `T : unmanaged` constraint requires a
            // matching CLR array. For mismatches the user gets
            // a clear conversion error here rather than a
            // confusing reflection failure later.
            if (arr.GetType().GetElementType() != elemCs)
            {
                var typed = Array.CreateInstance(elemCs, count);
                Array.Copy(arr, typed, count);
                arr = typed;
            }
            copyClosed.Invoke(null, new object[] { arr, _memory, ptr });
            return (ptr, count);
        }

        /// <summary>Call the guest's <c>cabi_realloc</c> export
        /// to allocate (or grow / move) a buffer in the
        /// component's exported memory. Cached on first call —
        /// every aggregate-param lowering routes through here.</summary>
        private int CabiRealloc(int oldPtr, int oldLen, int align, int newLen)
        {
            if (_cabiRealloc == null)
            {
                if (!_runtime.TryGetExportedFunction(
                        "cabi_realloc", out var addr))
                    throw new InvalidOperationException(
                        "Component does not export cabi_realloc; "
                        + "aggregate params require it.");
                _cabiRealloc = _runtime.CreateInvoker(
                    addr, new InvokerOptions());
            }
            var results = _cabiRealloc(oldPtr, oldLen, align, newLen);
            return results[0].Data.Int32;
        }

        private static int PrimByteSize(ComponentPrim p) => p switch
        {
            ComponentPrim.Bool => 1,
            ComponentPrim.S8   => 1,
            ComponentPrim.U8   => 1,
            ComponentPrim.S16  => 2,
            ComponentPrim.U16  => 2,
            ComponentPrim.S32  => 4,
            ComponentPrim.U32  => 4,
            ComponentPrim.F32  => 4,
            ComponentPrim.Char => 4,
            ComponentPrim.S64  => 8,
            ComponentPrim.U64  => 8,
            ComponentPrim.F64  => 8,
            _ => throw new NotSupportedException(
                "PrimByteSize for " + p + " is a follow-up."),
        };

        private static object LowerPrimitive(ComponentPrim prim, object? value) =>
            prim switch
            {
                ComponentPrim.Bool => (value is bool b && b) ? (object)1 : 0,
                ComponentPrim.S8   => Convert.ToInt32(value),
                ComponentPrim.U8   => Convert.ToInt32(value),
                ComponentPrim.S16  => Convert.ToInt32(value),
                ComponentPrim.U16  => Convert.ToInt32(value),
                ComponentPrim.S32  => Convert.ToInt32(value),
                ComponentPrim.U32  => (object)unchecked((int)Convert.ToUInt32(value)),
                ComponentPrim.S64  => Convert.ToInt64(value),
                ComponentPrim.U64  => (object)unchecked((long)Convert.ToUInt64(value)),
                ComponentPrim.F32  => Convert.ToSingle(value),
                ComponentPrim.F64  => Convert.ToDouble(value),
                ComponentPrim.Char => Convert.ToInt32(value),
                _ => throw new NotSupportedException(
                    "Lower-side primitive marshaling for " + prim
                    + " is a follow-up."),
            };

        // ---- Lift (core wire → managed) ------------------------------

        private object? LiftResult(
            ComponentFuncType fn, FunctionType coreFuncType,
            Wacs.Core.Runtime.Value[] coreResults)
        {
            if (fn.Results.Count == 0) return null;
            if (fn.Results.Count != 1)
                throw new NotSupportedException(
                    "Multi-result lifts are a follow-up.");
            if (coreResults.Length == 0)
                throw new InvalidOperationException(
                    "Core returned no values for an export with a "
                    + "result type. Component / core signatures are "
                    + "out of sync.");

            var r = fn.Results[0];
            var coreResult = coreResults[0];

            // String is encoded as a primitive at the WIT type
            // level but always lifted via the return-area
            // pointer the core leaves behind — special-case it
            // before the primitive switch.
            if (IsStringRef(r))
                return LiftStringRetArea(coreResult.Data.Int32);

            if (r.IsPrimitive)
                return LiftPrimitiveFromValue(r.Prim, coreResult);

            // Type-ref aggregates. These each share a pattern:
            // core returns the retArea pointer P; the lift reads
            // bytes from memory at P and routes them through the
            // same *Marshal helpers the transpiler emits IL
            // against — so the two engines share a single source
            // of truth for ABI semantics.
            if (TryLiftListOfPrim(r, coreResult.Data.Int32,
                    out var listResult))
                return listResult;
            if (TryLiftListOfString(r, coreResult.Data.Int32,
                    out var listStringResult))
                return listStringResult;
            if (TryLiftListOfStringPair(r, coreResult.Data.Int32,
                    out var listPairResult))
                return listPairResult;
            if (TryLiftOptionOfPrim(r, coreResult.Data.Int32,
                    out var optionResult))
                return optionResult;
            if (TryLiftOptionOfString(r, coreResult.Data.Int32,
                    out var optionStringResult))
                return optionStringResult;
            if (TryLiftTupleOfPrims(r, coreResult.Data.Int32,
                    out var tupleResult))
                return tupleResult;
            if (TryLiftResult(r, coreResult.Data.Int32,
                    out var resultValue))
                return resultValue;
            if (TryLiftEnum(r, coreResult.Data.Int32,
                    out var enumResult))
                return enumResult;
            if (TryLiftFlags(r, coreResult.Data.Int32,
                    out var flagsResult))
                return flagsResult;
            if (TryLiftRecord(r, coreResult.Data.Int32,
                    out var recordResult))
                return recordResult;
            if (TryLiftVariant(r, coreResult.Data.Int32,
                    out var variantResult))
                return variantResult;
            if (TryLiftOwn(r, coreResult.Data.Int32,
                    out var ownResult))
                return ownResult;

            throw new NotSupportedException(
                "Lift of this aggregate return shape via the "
                + "interpreter is a follow-up.");
        }

        /// <summary>Attempt <c>list&lt;primitive&gt;</c> lift —
        /// returns false for any other type-ref shape so the
        /// caller can fall through to the next candidate.</summary>
        private bool TryLiftListOfPrim(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentListType list))
                return false;
            if (!list.Element.IsPrimitive) return false;
            if (list.Element.Prim == ComponentPrim.String) return false;
            if (_memory == null) return false;

            var header = ReadMemoryBytes(retAreaPtr, 8);
            var dataPtr = BitConverter.ToInt32(header, 0);
            var count = BitConverter.ToInt32(header, 4);

            // Closed-generic dispatch: the transpiler's IL does
            // the same MakeGenericMethod step, so this is pure
            // runtime reflection — the transpiler's AOT path is
            // the fast lane; the interpreter pays the reflection
            // cost per call.
            var elemCs = PrimToCs(list.Element.Prim);
            var liftOpen = typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.LiftPrim),
                1,
                new[] { typeof(MemoryInstance), typeof(int), typeof(int) })!;
            var liftClosed = liftOpen.MakeGenericMethod(elemCs);
            // Pass the live MemoryInstance so the helper's AsSpan
            // dispatches on storage mode (ManagedArray / NativePointer).
            result = liftClosed.Invoke(null,
                new object[] { _memory, dataPtr, count });
            return true;
        }

        /// <summary>Attempt <c>option&lt;primitive&gt;</c> lift.</summary>
        private bool TryLiftOptionOfPrim(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentOptionType opt))
                return false;
            if (!opt.Inner.IsPrimitive) return false;
            if (opt.Inner.Prim == ComponentPrim.String) return false;
            if (_memory == null) return false;

            var disc = PrimitiveStore.ReadU8(_memory, retAreaPtr);
            var innerCs = PrimToCs(opt.Inner.Prim);
            var nullableT = typeof(Nullable<>).MakeGenericType(innerCs);
            if (disc == 0)
            {
                // Default Nullable<T> (HasValue = false).
                result = Activator.CreateInstance(nullableT);
                return true;
            }
            if (disc != 1)
                throw new FormatException(
                    $"Invalid option discriminant 0x{disc:X2}; expected 0 or 1.");

            // Some: payload at offset 4 for small prims (≤4 bytes).
            var payloadBytes = ReadMemoryBytes(retAreaPtr + 4, 4);
            object payload = opt.Inner.Prim switch
            {
                ComponentPrim.Bool => (object)(payloadBytes[0] != 0),
                ComponentPrim.S8   => (object)(sbyte)payloadBytes[0],
                ComponentPrim.U8   => (object)payloadBytes[0],
                ComponentPrim.S16  => (object)BitConverter.ToInt16(payloadBytes, 0),
                ComponentPrim.U16  => (object)BitConverter.ToUInt16(payloadBytes, 0),
                ComponentPrim.S32  => (object)BitConverter.ToInt32(payloadBytes, 0),
                ComponentPrim.U32  => (object)BitConverter.ToUInt32(payloadBytes, 0),
                ComponentPrim.F32  => (object)BitConverter.ToSingle(payloadBytes, 0),
                ComponentPrim.Char => (object)BitConverter.ToUInt32(payloadBytes, 0),
                _ => throw new NotSupportedException(
                    "option<" + opt.Inner.Prim + "> lift is a follow-up "
                    + "(wide primitives + strings need extra offset math)."),
            };
            result = Activator.CreateInstance(nullableT, payload);
            return true;
        }

        /// <summary>Lift <c>list&lt;string&gt;</c> via the
        /// dedicated <see cref="ListMarshal.LiftStringList"/>
        /// helper — keeps the UTF-8 chokepoint discipline.</summary>
        private bool TryLiftListOfString(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentListType list))
                return false;
            if (!list.Element.IsPrimitive) return false;
            if (list.Element.Prim != ComponentPrim.String) return false;
            if (_memory == null) return false;

            var header = ReadMemoryBytes(retAreaPtr, 8);
            var listPtr = BitConverter.ToInt32(header, 0);
            var count = BitConverter.ToInt32(header, 4);
            result = _stringEncoding == CanonOption.Kind.StringUtf16
                ? ListMarshal.LiftStringListUtf16(_memory, listPtr, count)
                : ListMarshal.LiftStringList(_memory, listPtr, count);
            return true;
        }

        /// <summary>Lift <c>list&lt;tuple&lt;string, string&gt;&gt;</c>
        /// — the wire shape WASI uses for environment-var pairs.
        /// Header is (arrayPtr, count); each element is 16 bytes
        /// (k_ptr, k_len, v_ptr, v_len). Decoded as a
        /// <c>(string, string)[]</c> on the C# side.</summary>
        private bool TryLiftListOfStringPair(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentListType list))
                return false;
            // The element must itself be a tuple<string, string>
            // — a type-ref to a structural ComponentTupleType
            // with exactly two string elements.
            if (list.Element.IsPrimitive) return false;
            if (list.Element.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)list.Element.TypeIdx]
                    is ComponentTupleType tup)) return false;
            if (tup.Elements.Count != 2) return false;
            foreach (var e in tup.Elements)
                if (!e.IsPrimitive || e.Prim != ComponentPrim.String)
                    return false;
            if (_memory == null) return false;

            var header = ReadMemoryBytes(retAreaPtr, 8);
            var arrayPtr = BitConverter.ToInt32(header, 0);
            var count = BitConverter.ToInt32(header, 4);
            var pairs = new (string, string)[count];
            for (int i = 0; i < count; i++)
            {
                int basePtr = arrayPtr + i * 16;
                var slot = ReadMemoryBytes(basePtr, 16);
                int kPtr = BitConverter.ToInt32(slot, 0);
                int kLen = BitConverter.ToInt32(slot, 4);
                int vPtr = BitConverter.ToInt32(slot, 8);
                int vLen = BitConverter.ToInt32(slot, 12);
                pairs[i] = (
                    LiftStringAt(kPtr, kLen),
                    LiftStringAt(vPtr, vLen));
            }
            result = pairs;
            return true;
        }

        /// <summary>Lift <c>option&lt;string&gt;</c>: disc byte
        /// at offset 0, on Some the (strPtr, strLen) at offset
        /// 4/8 → <see cref="StringMarshal.LiftUtf8"/>; on None
        /// the C# surface is <c>null</c>.</summary>
        private bool TryLiftOptionOfString(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentOptionType opt))
                return false;
            if (!opt.Inner.IsPrimitive
                || opt.Inner.Prim != ComponentPrim.String) return false;
            if (_memory == null) return false;

            var disc = PrimitiveStore.ReadU8(_memory, retAreaPtr);
            if (disc == 0) { result = null; return true; }
            if (disc != 1)
                throw new FormatException(
                    $"Invalid option discriminant 0x{disc:X2}; expected 0 or 1.");
            var strPtr = PrimitiveStore.ReadI32LE(_memory, retAreaPtr + 4);
            var strLen = PrimitiveStore.ReadI32LE(_memory, retAreaPtr + 8);
            result = LiftStringAt(strPtr, strLen);
            return true;
        }

        /// <summary>Lift a <c>tuple&lt;prim, prim, …&gt;</c> as
        /// a <c>ValueTuple&lt;…&gt;</c>. v0 supports 1–7 element
        /// tuples; 8+ element variants need the nested TRest
        /// shape the transpiler also defers.</summary>
        private bool TryLiftTupleOfPrims(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentTupleType tup))
                return false;
            var prims = new ComponentPrim[tup.Elements.Count];
            for (int i = 0; i < prims.Length; i++)
            {
                if (!tup.Elements[i].IsPrimitive) return false;
                prims[i] = tup.Elements[i].Prim;
            }
            if (_memory == null) return false;

            // Per-element offset follows canonical-ABI alignment
            // — running offset rounded up to each element's
            // alignment, then incremented by its byte size.
            // Mixed-width tuples (e.g. tuple<u64, u32> at 0/8)
            // are common in WASI shapes like wall-clock's
            // datetime tuple.
            var values = new object[prims.Length];
            int off = 0;
            for (int i = 0; i < prims.Length; i++)
            {
                var size = PrimByteSize(prims[i]);
                off = AlignUp(off, size);
                values[i] = ReadPrimAtOffset(retAreaPtr + off, prims[i]);
                off += size;
            }

            var csTypes = new Type[prims.Length];
            for (int i = 0; i < prims.Length; i++)
                csTypes[i] = PrimToCs(prims[i]);
            var tupleType = MakeValueTuple(csTypes);
            result = Activator.CreateInstance(tupleType, values);
            return true;
        }

        /// <summary>Lift <c>result&lt;Ok, Err&gt;</c> as
        /// <c>ValueTuple&lt;bool, Ok, Err&gt;</c> — same shape
        /// the transpiler's `ResultSide` projection produces.
        /// Disc=0 → (true, ok, default(Err)); Disc=1 → (false,
        /// default(Ok), err). Each side may be Absent (object),
        /// Primitive, or String.</summary>
        private bool TryLiftResult(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentResultType res))
                return false;
            if (!TryClassifySide(res.Ok, out var okKind, out var okPrim))
                return false;
            if (!TryClassifySide(res.Err, out var errKind, out var errPrim))
                return false;
            if (_memory == null) return false;

            var disc = PrimitiveStore.ReadU8(_memory, retAreaPtr);
            if (disc > 1)
                throw new FormatException(
                    $"Invalid result discriminant 0x{disc:X2}.");

            var okType = SideToCsType(okKind, okPrim);
            var errType = SideToCsType(errKind, errPrim);
            var tupleType = typeof(ValueTuple<,,>)
                .MakeGenericType(typeof(bool), okType, errType);
            object okValue = SideDefault(okType);
            object errValue = SideDefault(errType);
            if (disc == 0 && okKind != SideKind.Absent)
                okValue = LiftSidePayload(okKind, okPrim, retAreaPtr + 4);
            else if (disc == 1 && errKind != SideKind.Absent)
                errValue = LiftSidePayload(errKind, errPrim, retAreaPtr + 4);
            result = Activator.CreateInstance(tupleType,
                new object[] { disc == 0, okValue, errValue });
            return true;
        }

        private enum SideKind { Absent, Primitive, String }

        private static bool TryClassifySide(
            ComponentValType? raw, out SideKind kind,
            out ComponentPrim prim)
        {
            kind = SideKind.Absent;
            prim = default;
            if (raw == null) return true;
            if (!raw.Value.IsPrimitive) return false;
            if (raw.Value.Prim == ComponentPrim.String)
            { kind = SideKind.String; return true; }
            kind = SideKind.Primitive;
            prim = raw.Value.Prim;
            return true;
        }

        private static Type SideToCsType(SideKind kind, ComponentPrim prim) =>
            kind switch
            {
                SideKind.Absent => typeof(object),
                SideKind.String => typeof(string),
                SideKind.Primitive => PrimToCs(prim),
                _ => throw new InvalidOperationException(),
            };

        private static object SideDefault(Type t)
        {
            if (t.IsValueType) return Activator.CreateInstance(t)!;
            return null!;
        }

        private object LiftSidePayload(SideKind kind,
            ComponentPrim prim, int payloadOffset)
        {
            switch (kind)
            {
                case SideKind.Primitive:
                    return ReadPrimAtOffset(payloadOffset, prim);
                case SideKind.String:
                    var strPtr = BitConverter.ToInt32(
                        ReadMemoryBytes(payloadOffset, 4), 0);
                    var strLen = BitConverter.ToInt32(
                        ReadMemoryBytes(payloadOffset + 4, 4), 0);
                    return LiftStringAt(strPtr, strLen);
                default:
                    throw new InvalidOperationException(
                        "Absent side has no payload to lift.");
            }
        }

        /// <summary>Read a primitive value at a given memory
        /// offset — handles 32-bit prims (and bool / narrow
        /// ints which fit in i32 wire form). Wide prims
        /// (s64/u64/f64) read 8 bytes; the wider read width is
        /// the only delta from the small-prim path.</summary>
        private object ReadPrimAtOffset(int offset, ComponentPrim prim)
        {
            switch (prim)
            {
                case ComponentPrim.S64:
                    return BitConverter.ToInt64(
                        ReadMemoryBytes(offset, 8), 0);
                case ComponentPrim.U64:
                    return BitConverter.ToUInt64(
                        ReadMemoryBytes(offset, 8), 0);
                case ComponentPrim.F64:
                    return BitConverter.ToDouble(
                        ReadMemoryBytes(offset, 8), 0);
            }
            var bytes = ReadMemoryBytes(offset, 4);
            return prim switch
            {
                ComponentPrim.Bool => bytes[0] != 0,
                ComponentPrim.S8   => (object)(sbyte)bytes[0],
                ComponentPrim.U8   => (object)bytes[0],
                ComponentPrim.S16  => (object)BitConverter.ToInt16(bytes, 0),
                ComponentPrim.U16  => (object)BitConverter.ToUInt16(bytes, 0),
                ComponentPrim.S32  => (object)BitConverter.ToInt32(bytes, 0),
                ComponentPrim.U32  => (object)BitConverter.ToUInt32(bytes, 0),
                ComponentPrim.F32  => (object)BitConverter.ToSingle(bytes, 0),
                ComponentPrim.Char => (object)BitConverter.ToUInt32(bytes, 0),
                _ => throw new NotSupportedException(
                    "ReadPrimAtOffset for " + prim
                    + " is unsupported."),
            };
        }

        /// <summary>Lift a structural enum. Unlike the
        /// retArea-pointer aggregates, enum results return the
        /// discriminant directly on the wasm stack — narrow the
        /// i32 to the case-count-derived width per the
        /// canonical-ABI rule.</summary>
        private bool TryLiftEnum(
            ComponentValType t, int rawValue, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentEnumType en))
                return false;

            // Without a decoded WIT name to bind to a generated
            // enum type, return the raw discriminant integer —
            // the transpiler does the same fallback in
            // `ResolveEnumReturnType` when no WIT name is bound.
            var caseCount = en.Cases.Count;
            if (caseCount <= 256) result = (byte)rawValue;
            else if (caseCount <= 65536) result = (ushort)rawValue;
            else result = (uint)rawValue;
            return true;
        }

        /// <summary>Lift structural flags — same wire shape as
        /// enum (the bitmask comes back directly on the wasm
        /// stack) with a flag-count-derived width: ≤8 → byte,
        /// ≤16 → ushort, ≤32 → uint.</summary>
        private bool TryLiftFlags(
            ComponentValType t, int rawValue, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentFlagsType fl))
                return false;

            var n = fl.Flags.Count;
            if (n <= 8) result = (byte)rawValue;
            else if (n <= 16) result = (ushort)rawValue;
            else result = (uint)rawValue;
            return true;
        }

        /// <summary>Lift a record return. Without per-instance
        /// dynamic-type emission (the transpiler's
        /// EmitRecordType path) the interpreter surfaces records
        /// as <c>IReadOnlyDictionary&lt;string, object&gt;</c>
        /// keyed by WIT field name. Callers that need a typed
        /// shape should go through the transpiler.</summary>
        private bool TryLiftRecord(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentRecordType rec))
                return false;
            // v0 supports primitive fields only — same restriction
            // the transpiler enforces. Aggregate fields each add
            // their own marshaling shape.
            foreach (var f in rec.Fields)
                if (!f.Type.IsPrimitive) return false;
            if (_memory == null) return false;

            var dict = new Dictionary<string, object>(rec.Fields.Count);
            int offset = 0;
            foreach (var f in rec.Fields)
            {
                var align = PrimByteSize(f.Type.Prim);
                offset = AlignUp(offset, align);
                dict[f.Name] = ReadPrimAtOffset(retAreaPtr + offset,
                                                 f.Type.Prim);
                offset += align;
            }
            result = dict;
            return true;
        }

        /// <summary>Lift a variant return. Surfaces as a
        /// <c>(byte Tag, object? Payload)</c> tuple — Tag is the
        /// 0-indexed case ordinal, Payload is the lifted value:
        /// primitive, string, T[] for list-of-prim, or
        /// <c>IReadOnlyDictionary&lt;string, object&gt;</c> for
        /// record-of-prim. <c>null</c> for cases with no payload.
        /// Without dynamic-type emission this is the closest the
        /// interpreter gets to the transpiler's generated
        /// tagged-union class.</summary>
        private bool TryLiftVariant(
            ComponentValType t, int retAreaPtr, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentVariantType vr))
                return false;
            // Phase 2: every payload-bearing case must be
            // primitive, list<prim>, or record-of-prim. Other
            // aggregates (nested variant, list<aggregate>) bail.
            foreach (var c in vr.Cases)
            {
                if (c.Payload == null) continue;
                if (c.Payload.Value.IsPrimitive) continue;
                if (c.Payload.Value.TypeIdx >= _component.Types.Count)
                    return false;
                var inner = _component.Types[(int)c.Payload.Value.TypeIdx];
                if (inner is ComponentListType lt
                    && lt.Element.IsPrimitive
                    && lt.Element.Prim != ComponentPrim.String) continue;
                if (inner is ComponentRecordType rt
                    && AllPrimRecord(rt)) continue;
                return false;
            }
            if (_memory == null) return false;

            // Discriminant width tracks case count.
            var caseCount = vr.Cases.Count;
            var discWidth = caseCount <= 256 ? 1
                : caseCount <= 65536 ? 2 : 4;
            // Payload alignment: max alignment over all payload-
            // bearing cases. Mirrors the transpiler's
            // EmitVariantReturnBody computation.
            int payloadAlign = 1;
            foreach (var c in vr.Cases)
            {
                if (c.Payload == null) continue;
                int a;
                if (c.Payload.Value.IsPrimitive)
                {
                    a = c.Payload.Value.Prim == ComponentPrim.String
                        ? 4 : PrimByteSize(c.Payload.Value.Prim);
                }
                else
                {
                    var inner = _component.Types[(int)c.Payload.Value.TypeIdx];
                    a = inner is ComponentRecordType rec
                        ? RecordMaxFieldAlign(rec)
                        : 4;   // list<prim>: pointer alignment
                }
                if (a > payloadAlign) payloadAlign = a;
            }
            var payloadOffset = AlignUp(discWidth, payloadAlign);

            uint disc;
            if (discWidth == 1) disc = PrimitiveStore.ReadU8(_memory, retAreaPtr);
            else if (discWidth == 2)
                disc = PrimitiveStore.ReadU16LE(_memory, retAreaPtr);
            else disc = PrimitiveStore.ReadU32LE(_memory, retAreaPtr);
            if (disc >= caseCount)
                throw new FormatException(
                    $"Variant discriminant {disc} out of range "
                    + $"(case count = {caseCount}).");

            var c0 = vr.Cases[(int)disc];
            object? payload = null;
            if (c0.Payload.HasValue)
            {
                var p = c0.Payload.Value;
                if (p.IsPrimitive)
                {
                    if (p.Prim == ComponentPrim.String)
                        payload = LiftStringRetArea(retAreaPtr + payloadOffset);
                    else
                        payload = ReadPrimAtOffset(
                            retAreaPtr + payloadOffset, p.Prim);
                }
                else
                {
                    var inner = _component.Types[(int)p.TypeIdx];
                    if (inner is ComponentListType lt)
                        payload = LiftListPayload(
                            retAreaPtr + payloadOffset, lt.Element.Prim);
                    else if (inner is ComponentRecordType rec)
                        payload = LiftRecordPayload(
                            retAreaPtr + payloadOffset, rec);
                }
            }
            result = ((byte)disc, payload);
            return true;
        }

        /// <summary>True iff every record field is primitive.
        /// Companion to <see cref="TryLiftVariant"/>'s record-
        /// payload classifier.</summary>
        private static bool AllPrimRecord(ComponentRecordType rec)
        {
            foreach (var f in rec.Fields)
                if (!f.Type.IsPrimitive) return false;
            return true;
        }

        /// <summary>Max field alignment in a record — drives the
        /// canonical-ABI record alignment. Mirrors the
        /// transpiler's <c>RecordAlign</c>.</summary>
        private static int RecordMaxFieldAlign(ComponentRecordType rec)
        {
            int a = 1;
            foreach (var f in rec.Fields)
            {
                if (!f.Type.IsPrimitive) continue;
                var fa = PrimByteSize(f.Type.Prim);
                if (fa > a) a = fa;
            }
            return a;
        }

        /// <summary>Lift a list-of-primitive payload at a given
        /// offset. Reads (dataPtr, count) and dispatches
        /// <see cref="ListMarshal.LiftPrim{T}"/> via reflection
        /// over the element CLR type.</summary>
        private object LiftListPayload(int offset, ComponentPrim elemPrim)
        {
            var dataPtr = PrimitiveStore.ReadI32LE(_memory!, offset);
            var count = PrimitiveStore.ReadI32LE(_memory!, offset + 4);
            var elemCs = PrimToCs(elemPrim);
            var liftOpen = typeof(ListMarshal).GetMethod(
                nameof(ListMarshal.LiftPrim),
                1,
                new[] { typeof(MemoryInstance), typeof(int), typeof(int) })!;
            var liftClosed = liftOpen.MakeGenericMethod(elemCs);
            return liftClosed.Invoke(null,
                new object[] { _memory!, dataPtr, count })!;
        }

        /// <summary>Lift a record-of-primitive payload at a given
        /// offset. Mirrors <see cref="TryLiftRecord"/>'s field-by-
        /// field reader; surfaces as
        /// <c>IReadOnlyDictionary&lt;string, object&gt;</c>.</summary>
        private object LiftRecordPayload(int baseOffset, ComponentRecordType rec)
        {
            var dict = new Dictionary<string, object>(rec.Fields.Count);
            int rel = 0;
            foreach (var f in rec.Fields)
            {
                var align = PrimByteSize(f.Type.Prim);
                rel = AlignUp(rel, align);
                dict[f.Name] = ReadPrimAtOffset(
                    baseOffset + rel, f.Type.Prim);
                rel += align;
            }
            return dict;
        }

        /// <summary>Lift an <c>own&lt;R&gt;</c> return as the raw
        /// i32 handle. Users wrap it in their own resource type
        /// — the interpreter doesn't dynamically emit a sealed
        /// class the way the transpiler's EmitResourceType
        /// does. Callers that need the typed wrapper should
        /// transpile the component instead.</summary>
        private bool TryLiftOwn(
            ComponentValType t, int rawValue, out object? result)
        {
            result = null;
            if (t.IsPrimitive) return false;
            if (t.TypeIdx >= _component.Types.Count) return false;
            if (!(_component.Types[(int)t.TypeIdx] is ComponentOwnType _))
                return false;
            // own<R> returns the i32 handle directly on the wasm
            // stack — no retArea indirection.
            result = rawValue;
            return true;
        }

        private static int AlignUp(int offset, int alignment)
        {
            var rem = offset % alignment;
            return rem == 0 ? offset : offset + (alignment - rem);
        }

        /// <summary>Build a closed <c>ValueTuple&lt;…&gt;</c>
        /// type — copy of the transpiler's <c>MakeValueTuple</c>.</summary>
        private static Type MakeValueTuple(Type[] elements)
        {
            if (elements.Length == 0)
                throw new ArgumentException("Empty tuple not supported.");
            var openGeneric = elements.Length switch
            {
                1 => typeof(ValueTuple<>),
                2 => typeof(ValueTuple<,>),
                3 => typeof(ValueTuple<,,>),
                4 => typeof(ValueTuple<,,,>),
                5 => typeof(ValueTuple<,,,,>),
                6 => typeof(ValueTuple<,,,,,>),
                7 => typeof(ValueTuple<,,,,,,>),
                _ => throw new NotImplementedException(
                    "8+-element tuples (nested TRest) are a follow-up."),
            };
            return openGeneric.MakeGenericType(elements);
        }

        /// <summary>C# type for a component primitive — parallel
        /// to the transpiler's <c>PrimToCs</c>. Duplicated here
        /// rather than shared to keep the interpreter path from
        /// reaching into transpiler-only code.</summary>
        private static Type PrimToCs(ComponentPrim p) => p switch
        {
            ComponentPrim.Bool   => typeof(bool),
            ComponentPrim.S8     => typeof(sbyte),
            ComponentPrim.U8     => typeof(byte),
            ComponentPrim.S16    => typeof(short),
            ComponentPrim.U16    => typeof(ushort),
            ComponentPrim.S32    => typeof(int),
            ComponentPrim.U32    => typeof(uint),
            ComponentPrim.S64    => typeof(long),
            ComponentPrim.U64    => typeof(ulong),
            ComponentPrim.F32    => typeof(float),
            ComponentPrim.F64    => typeof(double),
            ComponentPrim.Char   => typeof(uint),
            ComponentPrim.String => typeof(string),
            _ => throw new NotSupportedException(
                "PrimToCs for " + p + " is a follow-up."),
        };

        private static object? LiftPrimitiveFromValue(
            ComponentPrim prim, Wacs.Core.Runtime.Value v) =>
            prim switch
            {
                ComponentPrim.Bool => (object)(v.Data.Int32 != 0),
                ComponentPrim.S8   => (object)(sbyte)v.Data.Int32,
                ComponentPrim.U8   => (object)(byte)v.Data.Int32,
                ComponentPrim.S16  => (object)(short)v.Data.Int32,
                ComponentPrim.U16  => (object)(ushort)v.Data.Int32,
                ComponentPrim.S32  => (object)v.Data.Int32,
                ComponentPrim.U32  => (object)v.Data.UInt32,
                ComponentPrim.S64  => (object)v.Data.Int64,
                ComponentPrim.U64  => (object)v.Data.UInt64,
                ComponentPrim.F32  => (object)v.Data.Float32,
                ComponentPrim.F64  => (object)v.Data.Float64,
                ComponentPrim.Char => (object)v.Data.UInt32,
                _ => throw new NotSupportedException(
                    "Primitive lift for " + prim + " is a follow-up."),
            };

        private static bool IsStringRef(ComponentValType t) =>
            t.IsPrimitive && t.Prim == ComponentPrim.String;

        private string LiftStringRetArea(int retAreaPtr)
        {
            if (_memory == null)
                throw new InvalidOperationException(
                    "String-returning component requires the core "
                    + "module to export a memory.");
            var memBytes = ReadMemoryBytes(retAreaPtr, 8);
            var strPtr = BitConverter.ToInt32(memBytes, 0);
            var strLen = BitConverter.ToInt32(memBytes, 4);
            return LiftStringAt(strPtr, strLen);
        }

        /// <summary>Decode a string at <paramref name="strPtr"/>
        /// per the per-invoke <see cref="_stringEncoding"/>.
        /// <paramref name="strLen"/> is bytes for UTF-8, u16 code
        /// units for UTF-16 (canonical-ABI rule). For
        /// latin1+utf16 the length's high bit distinguishes
        /// per-string — set → UTF-16 code units; clear → Latin-1
        /// byte count.</summary>
        private string LiftStringAt(int strPtr, int strLen)
        {
            if (_stringEncoding == CanonOption.Kind.StringUtf16)
                return StringMarshal.LiftUtf16(
                    _memory!, strPtr, strLen);
            if (_stringEncoding == CanonOption.Kind.StringLatin1OrUtf16)
                return StringMarshal.LiftLatin1OrUtf16(
                    _memory!, strPtr, strLen);
            return StringMarshal.LiftUtf8(
                _memory!, strPtr, strLen);
        }

        /// <summary>Snapshot a slice of the core module's
        /// linear memory into a fresh byte array. Component-side
        /// callers shouldn't peek at the underlying memory
        /// representation directly — this routes the lookup
        /// through the standard MemoryInstance accessors so
        /// growth / shared-memory invariants stay
        /// MemoryInstance's concern.</summary>
        private byte[] ReadMemoryBytes(int offset, int length)
        {
            // mem.AsSpan is the mode-aware accessor; ToArray() copies
            // out of the live span (ManagedArray byte[] or
            // NativePointer span over native memory) into a snapshot.
            return _memory!.AsSpan(offset, length).ToArray();
        }

    }
}
