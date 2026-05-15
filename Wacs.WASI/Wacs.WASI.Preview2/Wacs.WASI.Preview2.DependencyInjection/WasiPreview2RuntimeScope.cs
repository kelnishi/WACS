// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Wacs.ComponentModel.Validation;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.Filesystem;

namespace Wacs.WASI.Preview2.DependencyInjection
{
    /// <summary>
    /// One-shot owner of the DI scope that backs a wasip2 component
    /// run. Bridges the gap that exposed <c>wasi-nn/WACS-GAPS.md</c>
    /// gap 9: the bundle (passed as the module ctor's
    /// <c>hostBundle</c> arg) and the <see cref="Linker"/> (which
    /// fires every <c>*Bindings.BindToRuntime</c>) come from the
    /// same scope, so the scoped <see cref="HostBinding.ResourceContext"/> +
    /// <see cref="IPreopens"/> + per-impl singletons are consistent
    /// between the transpiler-direct-link path (reads the bundle's
    /// properties) and the BindHostFunction-fallback path (reads
    /// the runtime's import delegate table).
    ///
    /// <para>Lifetime contract: the scope is constructed inside
    /// <c>ComponentTranspiler.TranspileSingleModule</c>'s
    /// <c>configureImports</c> callback (where the runtime first
    /// becomes available); the ctor eagerly resolves
    /// <see cref="Bundle"/> and the Linker, so by the time the ctor
    /// returns the runtime has every BindHostFunction-based
    /// host binding registered. The caller disposes after the
    /// run completes.</para>
    /// </summary>
    public sealed class WasiPreview2RuntimeScope : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;
        private bool _disposed;

        /// <summary>
        /// The <see cref="WasmRuntime"/> the scope binds against.
        /// Same instance the caller passed in — DI's normal
        /// <c>TryAdd&lt;WasmRuntime&gt;</c> registration is
        /// pre-empted by an explicit <c>AddSingleton</c> so the
        /// Linker binds to this specific runtime.
        /// </summary>
        public WasmRuntime Runtime { get; }

        /// <summary>
        /// The resolved bundle — either <see cref="WasiPreview2Bundle"/>
        /// or the <c>WasiPreview2NNBundle</c> composite when WASI.NN
        /// was wired alongside. Pass through to the moduleClass's
        /// <c>(IImports, object hostBundle, object resources)</c>
        /// ctor as the second argument.
        /// </summary>
        public object Bundle { get; }

        /// <summary>
        /// The scoped resources object for the moduleClass's third
        /// ctor argument. Shares the same <c>ResourceContext</c>
        /// the bundle saw, so resource handles minted by either
        /// dispatch path resolve consistently.
        /// </summary>
        public WasiPreview2Resources Resources { get; }

        /// <summary>
        /// Build a scope for <paramref name="runtime"/>.
        ///
        /// <para>The ctor performs the full registration sequence
        /// up-front — registering the external runtime, optional
        /// preopens, calling <c>AddWasiPreview2</c> + (when
        /// requested) <c>AddWasiNN</c> + <c>AddWasiPreview2NNBundle</c>,
        /// resolving the Linker (which fires every
        /// <c>BindToRuntime</c> against <paramref name="runtime"/>),
        /// and resolving the bundle/resources. After return the
        /// runtime is fully wired; the moduleClass can be
        /// instantiated.</para>
        /// </summary>
        /// <param name="runtime">The transpiler's runtime, created
        /// by <c>ComponentTranspiler.TranspileSingleModule</c>.</param>
        /// <param name="preopens">Optional list of
        /// <c>(hostPath, guestPath)</c> mount pairs. When non-empty,
        /// a <see cref="Preopens"/> impl is registered as the
        /// scope's <see cref="IPreopens"/>, pre-empting the
        /// <c>DefaultPreopens</c> empty fallback that
        /// <c>AddWasiPreview2</c> would otherwise install.</param>
        /// <param name="configure">Optional service-collection
        /// hook fired AFTER the standard registrations but BEFORE
        /// scope construction. Lets callers register additional
        /// services or override defaults.</param>
        ///
        /// <para>WASI.NN composite is auto-detected: if
        /// <c>Wacs.WASI.NN.DependencyInjection</c> is loadable,
        /// the composite <c>WasiPreview2NNBundle</c> is registered
        /// alongside the base bundle. The transpiler emits its
        /// direct-link IL against whichever bundle type was
        /// resolved at transpile time, so this scope MUST match —
        /// when WASI.NN.DI is on the load path the IL casts to the
        /// composite type and a base-bundle here causes
        /// <c>InvalidCastException</c> at first call. Fail-soft if
        /// the assembly isn't loadable: components without
        /// wasi-nn imports never need it.</para>
        public WasiPreview2RuntimeScope(
            WasmRuntime runtime,
            IEnumerable<(string hostPath, string guestPath)>? preopens = null,
            Action<IServiceCollection>? configure = null)
        {
            Runtime = runtime
                ?? throw new ArgumentNullException(nameof(runtime));

            var services = new ServiceCollection();

            // Register the EXTERNAL runtime before AddWasiPreview2
            // so its TryAdd<WasmRuntime> falls through. The Linker
            // built later resolves THIS runtime, so its
            // BindToRuntime calls register against the transpiler-
            // owned runtime — not a fresh DI-built one.
            services.AddSingleton<WasmRuntime>(runtime);

            // Custom preopens, if any. AddWasiPreview2's
            // TryAddSingleton<IPreopens>(DefaultPreopens) below
            // skips when this is registered first.
            var preopenList = preopens?.ToArray();
            if (preopenList != null && preopenList.Length > 0)
            {
                services.AddSingleton<IPreopens>(
                    new Preopens(preopenList));
            }

            services.AddWasiPreview2(opts =>
            {
                // Singleton lifetime keeps the scope's
                // ResourceContext / WasmRuntime / bindings as one
                // instance set per scope. Validation defaults to
                // Strict; embedders override via `configure`.
                opts.InstanceLifetime = ServiceLifetime.Singleton;
                opts.ValidationLevel = ValidationLevel.Strict;
            });

            // Auto-detect WASI.NN.DependencyInjection: if it's on
            // the load path, register the composite bundle so the
            // transpiler's direct-link IL (emitted against the
            // composite type when both packages were available at
            // transpile time) finds the type it expects.
            ReflectivelyAddWasiNN(services);

            // Same shape for WASI.GFX.DependencyInjection — when
            // --wasi-gfx is on the load path, the composite
            // WasiPreview2GfxBundle is what the resolver auto-
            // discovers and what direct-link IL is emitted
            // against.
            ReflectivelyAddWasiGfx(services);

            configure?.Invoke(services);

            _serviceProvider = services.BuildServiceProvider();
            _scope = _serviceProvider.CreateScope();
            var sp = _scope.ServiceProvider;

            // Resolve the Linker FIRST. Its construction in
            // BuildLinker calls linker.Bind(*) for every
            // *Bindings, and each Bind triggers
            // bindings.BindToRuntime(linker.Runtime) — which IS
            // our external runtime. After this line the runtime
            // has every wasi:* host import registered.
            _ = sp.GetRequiredService<Linker>();

            Resources = sp.GetRequiredService<WasiPreview2Resources>();
            Bundle = ResolveBundle(sp);

            // Wire the cross-binding resource-drop hook so the
            // interpreter's [resource-drop]X handlers (registered
            // earlier when IBindable BindToRuntime calls fired)
            // also release handles allocated through the
            // direct-link path's WasiPreview2Resources table. The
            // SLM workflow allocates ~26-230 MiB logits tensors
            // per token through the direct-link compute call; the
            // interpreter binding's host.Tensors.Drop is a no-op
            // for those handles, so without this hook every token
            // leaks its logits tensor for the lifetime of the
            // component instance. Setter is idempotent — repeat
            // scope construction (typically one per component
            // instance) just rewires to the latest Resources.
            Runtime.ExternalResourceDrop =
                (t, h) => Resources.FreeResource(t, h);
        }

        private static object ResolveBundle(IServiceProvider sp)
        {
            // When a sibling-family composite bundle is registered,
            // prefer it — its forwarding properties cover every
            // [WitSource] interface from both packages so direct-
            // link emit picks up the sibling alongside Preview2. The
            // composite types are reflection-discovered to keep
            // this assembly free of compile-time deps on either
            // sibling. Order: gfx first, then nn — different
            // families don't coexist in one runtime today; first
            // match wins, mirroring the resolver's bundle
            // auto-discovery order.
            var gfxAsm = TryLoadAssembly("Wacs.WASI.GFX.DependencyInjection");
            var gfxCompositeType = gfxAsm?.GetType(
                "Wacs.WASI.GFX.DependencyInjection.WasiPreview2GfxBundle");
            if (gfxCompositeType != null)
            {
                var resolved = sp.GetService(gfxCompositeType);
                if (resolved != null) return resolved;
            }

            var nnAsm = TryLoadAssembly("Wacs.WASI.NN.DependencyInjection");
            var nnCompositeType = nnAsm?.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiPreview2NNBundle");
            if (nnCompositeType != null)
            {
                var resolved = sp.GetService(nnCompositeType);
                if (resolved != null) return resolved;
            }
            return sp.GetRequiredService<WasiPreview2Bundle>();
        }

        // Reflection-load the WASI.NN.DI assembly + run its
        // service-collection extensions so the composite bundle
        // and IGraphFuncs land in the scope. Mirrors what
        // ComponentMainHost did inline pre-refactor.
        //
        // The OnnxBackend registration runs INSIDE AddWasiNN's
        // configure callback (instead of post-hoc mutating the
        // singleton from `AutoRegisterOnnxBackend`) so the
        // Configuration instance the singleton resolves is the
        // SAME instance the backend was added to. Post-hoc
        // mutation worked when descriptor.ImplementationInstance
        // was reliably populated, but brittle to ordering — a
        // pre-existing factory registration would silently put
        // the mutated instance and the resolved instance out of
        // sync. Configure-callback ordering avoids that class of
        // bug entirely.
        private static void ReflectivelyAddWasiNN(IServiceCollection services)
        {
            var nnAsm = TryLoadAssembly("Wacs.WASI.NN.DependencyInjection");
            var nnExtType = nnAsm?.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiNNServiceCollectionExtensions");
            if (nnExtType == null) return;

            // Auto-discover every wasi-nn backend assembly loaded
            // into the AppDomain. Each backend's bindable type
            // implements `Wacs.WASI.NN.IWasiNNBackendRegistration`
            // and exposes a `ConfigureConfiguration(WasiNNConfiguration)`
            // method that mutates the shared config; we instantiate
            // each one via its parameterless ctor and chain the
            // mutations into the AddWasiNN configure callback.
            //
            // Failures (assembly not loadable / type not found /
            // Activator throws / ConfigureConfiguration throws)
            // surface as stderr warnings — the SLM's round-13
            // "InvalidEncoding" and round-19's "no named-model
            // resolver configured" both root-cause faster when the
            // load/ctor failure shows up at startup time, not at
            // first guest call.
            //
            // Adding a new wasi-nn backend now requires no edit to
            // this file: the new package's bindable implements
            // IWasiNNBackendRegistration and gets picked up the
            // next time the bundle scope is built.
            var configureDelegate = BuildAutoDiscoveredCallback(nnAsm!);

            // services.AddWasiNN(configure) — configure runs
            // BEFORE TryAddSingleton(opts.Configuration), so each
            // backend lands on the same Configuration instance
            // GraphFuncsImpl resolves later.
            nnExtType.GetMethod("AddWasiNN",
                    BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object?[] { services, configureDelegate });

            // services.AddWasiPreview2NNBundle() — registers the
            // composite that exposes both the Preview2 and
            // WASI.NN [WitSource] interfaces through one CLR
            // object.
            nnExtType.GetMethod("AddWasiPreview2NNBundle",
                    BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object?[] { services });
        }

        // Same pattern as ReflectivelyAddWasiNN above but for
        // WACS.WASI.GFX. The backend (SilkGfxBackend) is wired
        // into WasiGfxAmbient by WasiGfxSilkBindable.BindToRuntime,
        // not via a DI configure callback — so the gfx Configuration
        // can stay at its DefaultConfiguration() defaults here.
        // The DI registration's only job is to make the composite
        // WasiPreview2GfxBundle resolvable; the bundle's GfxBackend
        // forwarder picks up the ambient at first use.
        private static void ReflectivelyAddWasiGfx(IServiceCollection services)
        {
            var gfxAsm = TryLoadAssembly("Wacs.WASI.GFX.DependencyInjection");
            var gfxExtType = gfxAsm?.GetType(
                "Wacs.WASI.GFX.DependencyInjection.WasiGfxServiceCollectionExtensions");
            if (gfxExtType == null) return;

            // services.AddWasiGfx(null) — registers
            // WasiGfxConfiguration + WasiGfxBundle. configure=null
            // means "use defaults"; the ambient-backend hook fills
            // in the IBackend at runtime via the Silk bindable.
            gfxExtType.GetMethod("AddWasiGfx",
                    BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object?[] { services, null });

            // services.AddWasiPreview2GfxBundle() — registers the
            // composite forwarding Preview2 + GFX bundle properties
            // through one CLR object.
            gfxExtType.GetMethod("AddWasiPreview2GfxBundle",
                    BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object?[] { services });
        }

        // Auto-discover every wasi-nn backend assembly loaded into
        // the AppDomain and build a single combined
        // `Action<WasiNNDependencyInjectionOptions>` that registers
        // each backend's `IWasiNNBackendRegistration` against the
        // DI bundle's shared `WasiNNConfiguration`.
        //
        // Discovery rules:
        //   1. Enumerate all assemblies whose name starts with
        //      "Wacs.WASI.NN." (case-insensitive). Excludes the DI
        //      sibling (which doesn't ship a backend) and any
        //      `.Test` assemblies.
        //   2. For each, find every public type implementing
        //      `Wacs.WASI.NN.IWasiNNBackendRegistration` with a
        //      parameterless ctor.
        //   3. Activator.CreateInstance + call
        //      ConfigureConfiguration(opts.Configuration) per
        //      registrant. Failures stderr-warn and skip — one
        //      backend's ctor failure doesn't bring down scope
        //      construction for the others.
        //
        // Adding a new wasi-nn backend now requires no edit to this
        // file: the new package's bindable implements
        // IWasiNNBackendRegistration and gets picked up the next
        // time the bundle scope is built.
        private static Delegate? BuildAutoDiscoveredCallback(Assembly nnAsm)
        {
            var optsType = nnAsm.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiNNDependencyInjectionOptions");
            if (optsType == null) return null;

            var configProp = optsType.GetProperty("Configuration");
            if (configProp == null) return null;
            var configType = configProp.PropertyType;

            // IWasiNNBackendRegistration lives in Wacs.WASI.NN; the
            // DI assembly already references the core, so its
            // referenced-assembly list points at the right version.
            Assembly? coreAsm = TryLoadAssembly("Wacs.WASI.NN");
            var regIface = coreAsm?.GetType(
                "Wacs.WASI.NN.IWasiNNBackendRegistration");
            if (regIface == null) return null;
            var regMethod = regIface.GetMethod("ConfigureConfiguration");
            if (regMethod == null) return null;

            var registrants = DiscoverBackendRegistrants(regIface);
            if (registrants.Count == 0) return null;

            // Build an Action<opts> that walks each registrant and
            // invokes `reg.ConfigureConfiguration(opts.Configuration)`.
            // Pure reflection at invocation time (not Linq.Expressions)
            // because the registrants list is dynamic — building a
            // typed delegate against `IWasiNNBackendRegistration`
            // doesn't gain us anything since each call is already a
            // reflection invoke. The Action<opts> is typed against
            // the dynamically-resolved options type so it satisfies
            // AddWasiNN's configure parameter contract.
            var optsParam = Expression.Parameter(optsType, "opts");
            var configAccess = Expression.Property(optsParam, configProp);
            // Build a non-generic body: call ApplyAll(opts.Configuration, regMethod, registrants)
            var applyAll = typeof(WasiPreview2RuntimeScope).GetMethod(
                nameof(ApplyAllRegistrants),
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var call = Expression.Call(applyAll,
                Expression.Convert(configAccess, typeof(object)),
                Expression.Constant(regMethod, typeof(MethodInfo)),
                Expression.Constant(registrants, typeof(List<object>)));
            var actionType = typeof(Action<>).MakeGenericType(optsType);
            return Expression.Lambda(actionType, call, optsParam).Compile();
        }

        // Invoke ConfigureConfiguration on every discovered
        // registrant. Errors stderr-warn and skip so one bad
        // registrant doesn't sink the others.
        private static void ApplyAllRegistrants(
            object config, MethodInfo regMethod, List<object> registrants)
        {
            foreach (var r in registrants)
            {
                try { regMethod.Invoke(r, new[] { config }); }
                catch (TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    Console.Error.WriteLine(
                        "warn: " + r.GetType().FullName
                        + ".ConfigureConfiguration threw "
                        + inner.GetType().Name + ": " + inner.Message
                        + ". guests requesting this backend will see "
                        + "InvalidEncoding / NotFound errors.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        "warn: " + r.GetType().FullName
                        + ".ConfigureConfiguration failed: "
                        + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        // Walk every loaded assembly with a `Wacs.WASI.NN.*` name
        // (excluding the DI / Test subpackages), find every public
        // class implementing the registration interface, and
        // instantiate via parameterless ctor. Errors stderr-warn
        // and skip — one misbehaving backend doesn't kill the rest.
        private static List<object> DiscoverBackendRegistrants(
            Type regIface)
        {
            var result = new List<object>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                string name;
                try { name = asm.GetName().Name ?? ""; }
                catch { continue; }
                if (!name.StartsWith("Wacs.WASI.NN.",
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".DependencyInjection",
                        StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".Test",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                Type[] types;
                try { types = asm.GetExportedTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null)
                        .Select(t => t!).ToArray();
                }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.IsAbstract || t.IsInterface) continue;
                    if (!regIface.IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                    object? instance;
                    try { instance = Activator.CreateInstance(t); }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            "warn: wasi-nn backend " + t.FullName
                            + " ctor failed: " + ex.GetType().Name
                            + ": " + ex.Message
                            + ". guests requesting this backend will "
                            + "see InvalidEncoding / NotFound errors.");
                        continue;
                    }
                    if (instance != null) result.Add(instance);
                }
            }
            return result;
        }

        // Resolve a named assembly across both .NET load contexts.
        // First tries `Assembly.Load(name)` (the default context —
        // covers project-referenced and CLR-resolved-by-name
        // dependencies). On miss, walks `AppDomain.CurrentDomain
        // .GetAssemblies()` to catch assemblies already loaded into
        // the LoadFromContext via `Assembly.LoadFrom(path)` — the
        // path the CLI's `--bind <path>` walks. Without the fallback,
        // `--bind /path/to/Wacs.WASI.NN.LlamaSharp.dll` populates
        // AppDomain but the auto-wire's Assembly.Load(name) misses
        // it, the LlamaSharp backend never registers in the DI's
        // WasiNNConfiguration, and `compute(...)` trips NotFound at
        // first guest call (gap 25, round-20 verification).
        //
        // Mirror of the round-18 fix in HostPackageResolver
        // .TryFindResourceImpl — same AppDomain-vs-default-context
        // split, same fallback shape.
        private static Assembly? TryLoadAssembly(string name)
        {
            try
            {
                var asm = Assembly.Load(name);
                if (asm != null) return asm;
            }
            catch { /* fall through to AppDomain walk */ }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    if (string.Equals(asm.GetName().Name, name,
                        StringComparison.OrdinalIgnoreCase))
                        return asm;
                }
                catch
                {
                    // Malformed metadata on a collectable / dynamic-
                    // but-not-flagged assembly. Skip and keep walking;
                    // a single-asm hiccup must not blank out the
                    // search.
                }
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scope.Dispose();
            _serviceProvider.Dispose();
        }
    }
}
