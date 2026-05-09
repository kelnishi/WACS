// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Linq;
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
        }

        private static object ResolveBundle(IServiceProvider sp)
        {
            // When WASI.NN.DI is loaded and the composite bundle
            // type is registered (via AddWasiPreview2NNBundle),
            // prefer it — its forwarding properties cover every
            // [WitSource] interface from both packages so direct-
            // link emit picks up wasi-nn alongside Preview2. The
            // composite type is reflection-discovered to keep
            // this assembly free of a compile-time WASI.NN dep.
            var nnAsm = TryLoadAssembly("Wacs.WASI.NN.DependencyInjection");
            var compositeType = nnAsm?.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiPreview2NNBundle");
            if (compositeType != null)
            {
                var resolved = sp.GetService(compositeType);
                if (resolved != null) return resolved;
            }
            return sp.GetRequiredService<WasiPreview2Bundle>();
        }

        // Reflection-load the WASI.NN.DI assembly + run its
        // service-collection extensions so the composite bundle
        // and IGraphFuncs land in the scope. Mirrors what
        // ComponentMainHost did inline pre-refactor.
        private static void ReflectivelyAddWasiNN(IServiceCollection services)
        {
            var nnAsm = TryLoadAssembly("Wacs.WASI.NN.DependencyInjection");
            var nnExtType = nnAsm?.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiNNServiceCollectionExtensions");
            if (nnExtType == null) return;

            // services.AddWasiNN(null) — null configure accepts
            // defaults (no backends until the embedder explicitly
            // calls AddBackend).
            nnExtType.GetMethod("AddWasiNN",
                    BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object?[] { services, null });

            // services.AddWasiPreview2NNBundle() — registers the
            // composite that exposes both the Preview2 and
            // WASI.NN [WitSource] interfaces through one CLR
            // object.
            nnExtType.GetMethod("AddWasiPreview2NNBundle",
                    BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object?[] { services });

            // Auto-register the ONNX backend if Wacs.WASI.NN.OnnxRuntime
            // is on the load path — saves the embedder the manual
            // `b.AddBackend(GraphEncoding.ONNX, new OnnxBackend())`
            // call for the common case.
            AutoRegisterOnnxBackend(services, nnAsm!);
        }

        private static void AutoRegisterOnnxBackend(
            IServiceCollection services, Assembly nnAsm)
        {
            // The configuration is a singleton registered by
            // AddWasiNN; we mutate its Backends dictionary post-
            // registration so the IGraphFuncs sees the entry.
            var configType = nnAsm.GetType(
                "Wacs.WASI.NN.WasiNNConfiguration");
            if (configType == null) return;

            // Find the WasiNNConfiguration descriptor and grab
            // its singleton instance from the post-Add factory.
            var descriptor = services.FirstOrDefault(d =>
                d.ServiceType == configType);
            if (descriptor == null) return;
            object? config = descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(null!);
            if (config == null) return;

            var backendsProp = configType.GetProperty("Backends");
            var backends = backendsProp?.GetValue(config);
            if (backends == null) return;

            var ortAsm = TryLoadAssembly("Wacs.WASI.NN.OnnxRuntime");
            var onnxBackendType = ortAsm?.GetType(
                "Wacs.WASI.NN.OnnxRuntime.OnnxBackend");
            if (onnxBackendType == null) return;
            object? onnxBackend;
            try { onnxBackend = Activator.CreateInstance(onnxBackendType); }
            catch { return; }
            if (onnxBackend == null) return;

            var encodingType = nnAsm.GetType(
                "Wacs.WASI.NN.Types.GraphEncoding");
            if (encodingType == null) return;
            object onnxEncoding = Enum.ToObject(encodingType, 1);

            backends.GetType().GetMethod("set_Item")?
                .Invoke(backends, new object?[] { onnxEncoding, onnxBackend });
        }

        private static Assembly? TryLoadAssembly(string name)
        {
            try { return Assembly.Load(name); }
            catch { return null; }
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
