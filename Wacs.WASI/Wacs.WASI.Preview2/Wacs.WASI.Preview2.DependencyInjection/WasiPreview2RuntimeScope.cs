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
using Microsoft.Extensions.DependencyInjection;
using Wacs.ComponentModel.Runtime;
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

            // Walk every loaded assembly for [WasiScopeBootstrap]
            // attributes and invoke each pointed-at registration.
            // Each subsystem DI package (wasi-nn, wasi-gfx, future
            // siblings) ships its own IWasiScopeBootstrap impl +
            // assembly attribute — this scope holds no hardcoded
            // knowledge of which subsystems exist.
            ApplyScopeBootstraps(services);

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
            // Attribute-driven composite-bundle discovery. Scan the
            // AppDomain for types tagged with [WacsCompositeBundle],
            // sort by Priority desc / Family asc, and resolve the
            // highest one whose DI registration is present in the
            // current scope. Falls through to the Preview2 base
            // bundle when no composite is registered — components
            // without sibling-family imports stay on the slim path.
            //
            // 1c: pre-load any DI sibling assembly declared by
            // already-loaded contract assemblies via
            // [WacsDependencyInjectionSibling]. The base
            // Wacs.WASI.Preview2 assembly is always loaded here
            // (we're in its DI sibling), and it declares its own
            // sibling — so this also picks up the case where no
            // GFX / NN contract was explicitly --bind'd but the
            // declaring assembly is reachable via ProjectReference.
            LoadDeclaredSiblings();

            foreach (var t in DiscoverCompositeBundleTypes())
            {
                var resolved = sp.GetService(t);
                if (resolved != null) return resolved;
            }
            return sp.GetRequiredService<WasiPreview2Bundle>();
        }

        // Walks every already-loaded assembly for
        // [WacsDependencyInjectionSibling] attributes and
        // Assembly.Load()s each declared sibling. Idempotent;
        // quiet on failure. See HostPackageResolver.LoadDeclaredSiblings
        // for the transpiler-side companion.
        private static void LoadDeclaredSiblings()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                var attrs = asm.GetCustomAttributes(
                    typeof(WacsDependencyInjectionSiblingAttribute),
                    inherit: false);
                foreach (var raw in attrs)
                {
                    var attr = (WacsDependencyInjectionSiblingAttribute)raw;
                    if (!seen.Add(attr.AssemblyName)) continue;
                    try { Assembly.Load(attr.AssemblyName); }
                    catch { /* not on disk — non-fatal */ }
                }
            }
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "Walks loaded assemblies for the " +
                "WacsCompositeBundleAttribute. Tagged composite bundle " +
                "types are statically referenced from their hosting DI " +
                "sibling — same root path as ApplyScopeBootstraps above.")]
        private static IEnumerable<Type> DiscoverCompositeBundleTypes()
        {
            var seen = new HashSet<Type>();
            var candidates = new List<(Type Type, int Priority, string Family)>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                Type[] types;
                try { types = asm.GetExportedTypes(); }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types
                        .Where(t => t != null)
                        .Select(t => t!)
                        .ToArray();
                }
                foreach (var t in types)
                {
                    if (!seen.Add(t)) continue;
                    var attr = t.GetCustomAttribute<
                        WacsCompositeBundleAttribute>(inherit: false);
                    if (attr == null) continue;
                    candidates.Add((t, attr.Priority, attr.Family));
                }
            }
            candidates.Sort((a, b) =>
            {
                int p = b.Priority.CompareTo(a.Priority);
                return p != 0 ? p
                    : string.Compare(a.Family, b.Family,
                        StringComparison.Ordinal);
            });
            return candidates.Select(c => c.Type);
        }

        // Walk loaded assemblies for [WasiScopeBootstrap]
        // attributes and apply each. Each WASI subsystem DI
        // package self-registers via this attribute; nothing
        // here is wired by subsystem name.
        //
        // The Activator.CreateInstance + Apply call per bootstrap
        // is the only reflection: the type token comes from the
        // attribute's typeof() (statically referenced from the
        // sibling package's source, no string lookup), so the
        // type is rooted by static reference — its parameterless
        // constructor isn't trimmed even though the attribute's
        // Type property is unannotated.
        [UnconditionalSuppressMessage("Trimming", "IL2072",
            Justification = "attr.Type comes from typeof(...) in the " +
                "sibling assembly's [WasiScopeBootstrap(...)] argument " +
                "— that's a static reference to the bootstrap class, so " +
                "trimming preserves both the type and its public " +
                "parameterless ctor.")]
        private static void ApplyScopeBootstraps(IServiceCollection services)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                WasiScopeBootstrapAttribute[] attrs;
                try
                {
                    attrs = (WasiScopeBootstrapAttribute[])asm
                        .GetCustomAttributes(typeof(WasiScopeBootstrapAttribute),
                            inherit: false);
                }
                catch
                {
                    // Malformed metadata on a collectable / dynamic
                    // assembly. Skip; don't blank out the search.
                    continue;
                }
                foreach (var attr in attrs)
                {
                    try
                    {
                        var inst = (IWasiScopeBootstrap)Activator
                            .CreateInstance(attr.Type)!;
                        inst.Apply(services);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            "warn: scope bootstrap "
                            + attr.Type.FullName + " threw "
                            + ex.GetType().Name + ": " + ex.Message
                            + ". subsystem registration skipped.");
                    }
                }
            }
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
