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

            // Build per-backend configure callbacks for whatever
            // wasi-nn backends are loadable. Each callback runs
            // INSIDE AddWasiNN's configure step (before
            // TryAddSingleton(opts.Configuration)), so the registered
            // Configuration instance carries every wired backend in
            // one go.
            //
            // Failures surface as stderr warnings (vs. silent
            // returns) — the SLM's round-13 "InvalidEncoding"
            // symptom and round-19's "no named-model resolver
            // configured" both root-cause faster when the load /
            // ctor failure shows up at startup time, not at first
            // guest call.
            var onnxCallback = BuildOnnxConfigureCallback(nnAsm!);
            var llamaCallback = BuildLlamaSharpConfigureCallback(nnAsm!);
            var torchCallback = BuildTorchSharpConfigureCallback(nnAsm!);
            var genaiCallback = BuildOnnxGenAIConfigureCallback(nnAsm!);
            var openVinoCallback = BuildOpenVinoConfigureCallback(nnAsm!);
            var configureDelegate = CombineCallbacks(
                onnxCallback, llamaCallback, torchCallback,
                genaiCallback, openVinoCallback);

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

        // Build an Action<WasiNNDependencyInjectionOptions>
        // delegate (typed against the dynamically-loaded type)
        // that calls opts.AddBackend(GraphEncoding.ONNX, new
        // OnnxBackend()). Returns null when:
        //   - Wacs.WASI.NN.OnnxRuntime isn't loadable
        //   - the OnnxBackend type or its parameterless ctor
        //     can't be resolved
        //   - Activator.CreateInstance throws
        // In each error case a stderr warning is emitted so the
        // failure is diagnosable (vs the previous silent return
        // that turned every error into "InvalidEncoding" at
        // guest-call time).
        private static Delegate? BuildOnnxConfigureCallback(Assembly nnAsm)
        {
            var optsType = nnAsm.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiNNDependencyInjectionOptions");
            if (optsType == null) return null;

            // AddBackend(GraphEncoding, IBackend) lives in
            // Wacs.WASI.NN.DependencyInjection but its first
            // parameter is Wacs.WASI.NN.Types.GraphEncoding from
            // the Wacs.WASI.NN assembly. Pull the type from the
            // method signature so we don't have to load the
            // sibling assembly explicitly — and so we don't fail
            // silently if the WASI.NN type namespace ever changes.
            var addBackend = optsType.GetMethod("AddBackend");
            if (addBackend == null) return null;
            var addBackendParams = addBackend.GetParameters();
            if (addBackendParams.Length != 2) return null;
            var encodingType = addBackendParams[0].ParameterType;
            var backendIfaceType = addBackendParams[1].ParameterType;

            var ortAsm = TryLoadAssembly("Wacs.WASI.NN.OnnxRuntime");
            if (ortAsm == null)
            {
                // Not necessarily an error — `--wasip2` without
                // `--wasi-nn` doesn't load OnnxRuntime, and that's
                // fine. Stay quiet here; only complain when the
                // assembly IS loadable but its Activator step
                // fails.
                return null;
            }

            var onnxBackendType = ortAsm.GetType(
                "Wacs.WASI.NN.OnnxRuntime.OnnxBackend");
            if (onnxBackendType == null)
            {
                System.Console.Error.WriteLine(
                    "warn: Wacs.WASI.NN.OnnxRuntime is loadable but "
                    + "OnnxBackend type wasn't found. wasi-nn ONNX "
                    + "guests will see InvalidEncoding errors.");
                return null;
            }

            object? onnxBackend;
            try { onnxBackend = Activator.CreateInstance(onnxBackendType); }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "warn: OnnxBackend instantiation failed: "
                    + ex.GetType().Name + ": " + ex.Message
                    + ". wasi-nn ONNX guests will see InvalidEncoding "
                    + "errors. Check that the native ONNX Runtime "
                    + "library is on the load path.");
                return null;
            }
            if (onnxBackend == null) return null;

            object onnxEncoding = Enum.ToObject(encodingType, 1);

            // Use Linq.Expressions to build a strongly-typed
            // Action<WasiNNDependencyInjectionOptions> at runtime.
            // Captures onnxBackend + onnxEncoding via Constant
            // expressions so the AddBackend call site doesn't
            // need to redo any reflection at invocation time.
            var optsParam = System.Linq.Expressions.Expression
                .Parameter(optsType, "opts");
            var call = System.Linq.Expressions.Expression.Call(
                optsParam,
                addBackend,
                System.Linq.Expressions.Expression.Constant(
                    onnxEncoding, encodingType),
                System.Linq.Expressions.Expression.Constant(
                    onnxBackend, backendIfaceType));
            var actionType = typeof(Action<>).MakeGenericType(optsType);
            var lambda = System.Linq.Expressions.Expression.Lambda(
                actionType, call, optsParam);
            return lambda.Compile();
        }

        // Combine per-backend configure callbacks into one
        // multicast Action<options>. Multicast invocation fires
        // each subscriber in registration order against the same
        // options instance — exactly what we need when
        // AddWasiNN's `configure?.Invoke(opts)` runs. Returns
        // null if every input is null (in which case AddWasiNN
        // gets a null configure and runs with an empty
        // Configuration, matching the no-backend-loadable
        // behavior).
        private static Delegate? CombineCallbacks(
            params Delegate?[] callbacks)
        {
            Delegate? result = null;
            foreach (var cb in callbacks)
            {
                if (cb == null) continue;
                result = result == null
                    ? cb
                    : Delegate.Combine(result, cb);
            }
            return result;
        }

        // Build an Action<WasiNNDependencyInjectionOptions>
        // delegate that wires the LlamaSharp backend, scanning
        // <c>WACS_WASINN_GGUF_DIR</c> for *.gguf files (matches
        // WasiNNLlamaSharpBindable's env-driven registry shape).
        // Registers the backend under BOTH:
        //   - opts.AddBackend(GraphEncoding.GGML, backend) — for
        //     embedders that route through the encoding-keyed
        //     Backends dict (currently unused for LlamaSharp
        //     since its byte-loader path traps, but left wired
        //     for symmetry with ONNX);
        //   - opts.Configuration.LoadByNameBackend = backend —
        //     the load-by-name path GraphFuncsImpl checks before
        //     falling back to the byte-flow (gap 24).
        //
        // Returns null when:
        //   - Wacs.WASI.NN.LlamaSharp isn't loadable (the common
        //     case for `wacs run --wasip2 --wasi-nn` — only ONNX
        //     ships bundled);
        //   - the LlamaSharpBackend type / FromPaths factory /
        //     LoadByNameBackend property can't be resolved;
        //   - FromPaths throws.
        // Each non-quiet error path emits a stderr warning so the
        // failure mode is discoverable at startup time.
        private static Delegate? BuildLlamaSharpConfigureCallback(
            Assembly nnAsm)
        {
            var optsType = nnAsm.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiNNDependencyInjectionOptions");
            if (optsType == null) return null;

            var addBackend = optsType.GetMethod("AddBackend");
            if (addBackend == null) return null;
            var addBackendParams = addBackend.GetParameters();
            if (addBackendParams.Length != 2) return null;
            var encodingType = addBackendParams[0].ParameterType;
            var backendIfaceType = addBackendParams[1].ParameterType;

            var llamaAsm = TryLoadAssembly("Wacs.WASI.NN.LlamaSharp");
            if (llamaAsm == null) return null;  // not an error.

            var llamaBackendType = llamaAsm.GetType(
                "Wacs.WASI.NN.LlamaSharp.LlamaSharpBackend");
            if (llamaBackendType == null)
            {
                System.Console.Error.WriteLine(
                    "warn: Wacs.WASI.NN.LlamaSharp is loadable but "
                    + "LlamaSharpBackend type wasn't found. wasi-nn "
                    + "GGUF guests will see NotFound errors.");
                return null;
            }

            var fromPaths = llamaBackendType.GetMethod("FromPaths",
                BindingFlags.Public | BindingFlags.Static);
            if (fromPaths == null)
            {
                System.Console.Error.WriteLine(
                    "warn: LlamaSharpBackend.FromPaths(IDictionary"
                    + "<string,string>) static factory not found. "
                    + "Cannot auto-wire LlamaSharp; embedders should "
                    + "construct LlamaSharpBackend directly via "
                    + "AddWasiNN(b => b.AddBackend(...)).");
                return null;
            }

            var registry = BuildGgufRegistryFromEnv();

            object? llamaBackend;
            try { llamaBackend = fromPaths.Invoke(null, new object?[] { registry }); }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "warn: LlamaSharpBackend.FromPaths failed: "
                    + ex.GetType().Name + ": " + ex.Message
                    + ". wasi-nn GGUF guests will see NotFound "
                    + "errors. Check that the LLamaSharp native "
                    + "library is on the load path.");
                return null;
            }
            if (llamaBackend == null) return null;

            // Resolve the LoadByNameBackend property on
            // WasiNNConfiguration via opts.Configuration.
            var configProp = optsType.GetProperty("Configuration");
            if (configProp == null) return null;
            var configType = configProp.PropertyType;
            var loadByNameProp = configType.GetProperty("LoadByNameBackend");
            if (loadByNameProp == null
                || loadByNameProp.GetSetMethod() == null)
            {
                System.Console.Error.WriteLine(
                    "warn: WasiNNConfiguration.LoadByNameBackend "
                    + "property missing or read-only; cannot route "
                    + "GGUF graph.load-by-name through LlamaSharp.");
                return null;
            }

            // GGML = 5 in both Nn.GraphEncoding and
            // Types.GraphEncoding (cross-checked at file scope).
            object ggmlEncoding = Enum.ToObject(encodingType, 5);

            // Build:
            //   opts.AddBackend(GraphEncoding.GGML, backend);
            //   opts.Configuration.LoadByNameBackend = backend;
            var optsParam = Expression.Parameter(optsType, "opts");
            var addCall = Expression.Call(
                optsParam,
                addBackend,
                Expression.Constant(ggmlEncoding, encodingType),
                Expression.Constant(llamaBackend, backendIfaceType));
            var configAccess = Expression.Property(optsParam, configProp);
            var assign = Expression.Assign(
                Expression.Property(configAccess, loadByNameProp),
                Expression.Constant(llamaBackend,
                    loadByNameProp.PropertyType));
            var block = Expression.Block(addCall, assign);
            var actionType = typeof(Action<>).MakeGenericType(optsType);
            return Expression.Lambda(actionType, block, optsParam)
                .Compile();
        }

        // Sibling of BuildLlamaSharpConfigureCallback for the
        // TorchSharp / PyTorch backend. Detects
        // `Wacs.WASI.NN.TorchSharp.TorchSharpBackend`, builds an
        // env-driven registry from $WACS_WASINN_TORCH_DIR
        // (mirroring WasiNNTorchSharpBindable's scan), and wires
        // the backend into BOTH `Backends[PyTorch]` AND
        // `LoadByNameBackend` so guest `graph.load-by-name(...)`
        // direct-links cleanly.
        //
        // Returns null when:
        //   - Wacs.WASI.NN.TorchSharp isn't loadable (the common
        //     `wacs run --wasip2 --wasi-nn` ONNX-default case;
        //     stay quiet)
        //   - the TorchSharpBackend type / FromPaths factory /
        //     LoadByNameBackend property isn't resolvable
        //   - FromPaths throws (libtorch native lib missing /
        //     mismatch)
        // Each non-quiet error path emits a stderr warning so the
        // failure is discoverable at startup, not at first
        // `compute(...)`.
        private static Delegate? BuildTorchSharpConfigureCallback(
            Assembly nnAsm)
        {
            var optsType = nnAsm.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiNNDependencyInjectionOptions");
            if (optsType == null) return null;

            var addBackend = optsType.GetMethod("AddBackend");
            if (addBackend == null) return null;
            var addBackendParams = addBackend.GetParameters();
            if (addBackendParams.Length != 2) return null;
            var encodingType = addBackendParams[0].ParameterType;
            var backendIfaceType = addBackendParams[1].ParameterType;

            var torchAsm = TryLoadAssembly("Wacs.WASI.NN.TorchSharp");
            if (torchAsm == null) return null;  // not an error.

            var torchBackendType = torchAsm.GetType(
                "Wacs.WASI.NN.TorchSharp.TorchSharpBackend");
            if (torchBackendType == null)
            {
                System.Console.Error.WriteLine(
                    "warn: Wacs.WASI.NN.TorchSharp is loadable but "
                    + "TorchSharpBackend type wasn't found. wasi-nn "
                    + "PyTorch guests will see NotFound errors.");
                return null;
            }

            var fromPaths = torchBackendType.GetMethod("FromPaths",
                BindingFlags.Public | BindingFlags.Static);
            if (fromPaths == null)
            {
                System.Console.Error.WriteLine(
                    "warn: TorchSharpBackend.FromPaths(IDictionary"
                    + "<string,string>) static factory not found. "
                    + "Cannot auto-wire TorchSharp; embedders should "
                    + "construct TorchSharpBackend directly via "
                    + "AddWasiNN(b => b.AddBackend(...)).");
                return null;
            }

            var registry = BuildTorchScriptRegistryFromEnv();

            object? torchBackend;
            try { torchBackend = fromPaths.Invoke(null, new object?[] { registry }); }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "warn: TorchSharpBackend.FromPaths failed: "
                    + ex.GetType().Name + ": " + ex.Message
                    + ". wasi-nn PyTorch guests will see NotFound "
                    + "errors. Check that the libtorch native "
                    + "library is on the load path.");
                return null;
            }
            if (torchBackend == null) return null;

            var configProp = optsType.GetProperty("Configuration");
            if (configProp == null) return null;
            var configType = configProp.PropertyType;
            var loadByNameProp = configType.GetProperty("LoadByNameBackend");
            if (loadByNameProp == null
                || loadByNameProp.GetSetMethod() == null)
            {
                System.Console.Error.WriteLine(
                    "warn: WasiNNConfiguration.LoadByNameBackend "
                    + "property missing or read-only; cannot route "
                    + "PyTorch graph.load-by-name through TorchSharp.");
                return null;
            }

            // PyTorch = 3 in both Nn.GraphEncoding and
            // Types.GraphEncoding (cross-checked at file scope —
            // OpenVINO=0, ONNX=1, TensorFlow=2, PyTorch=3).
            object pytorchEncoding = Enum.ToObject(encodingType, 3);

            // Build:
            //   opts.AddBackend(GraphEncoding.PyTorch, backend);
            //   opts.Configuration.LoadByNameBackend = backend;
            var optsParam = Expression.Parameter(optsType, "opts");
            var addCall = Expression.Call(
                optsParam,
                addBackend,
                Expression.Constant(pytorchEncoding, encodingType),
                Expression.Constant(torchBackend, backendIfaceType));
            var configAccess = Expression.Property(optsParam, configProp);
            var assign = Expression.Assign(
                Expression.Property(configAccess, loadByNameProp),
                Expression.Constant(torchBackend,
                    loadByNameProp.PropertyType));
            var block = Expression.Block(addCall, assign);
            var actionType = typeof(Action<>).MakeGenericType(optsType);
            return Expression.Lambda(actionType, block, optsParam)
                .Compile();
        }

        // Sibling of BuildLlamaSharpConfigureCallback /
        // BuildTorchSharpConfigureCallback for the
        // OnnxRuntime-GenAI backend (generative LLMs in GenAI
        // directory format: genai_config.json + tokenizer.json +
        // model.onnx + weights). Detects
        // `Wacs.WASI.NN.OnnxRuntimeGenAI.OnnxGenAIBackend`, builds
        // an env-driven registry from $WACS_WASINN_GENAI_DIR
        // (mirroring WasiNNOnnxGenAIBindable's subdirectory scan
        // for genai_config.json), and wires the backend into
        // `LoadByNameBackend` ONLY — leaves `Backends[ONNX]` to
        // the regular OnnxBackend so `graph.load(bytes)` and
        // `graph.load-by-name(directory)` route to the right
        // place when both packages are present.
        //
        // Returns null when:
        //   - Wacs.WASI.NN.OnnxRuntimeGenAI isn't loadable (the
        //     common `wacs run --wasip2 --wasi-nn` case);
        //   - OnnxGenAIBackend / FromDirectories factory /
        //     LoadByNameBackend property can't be resolved;
        //   - FromDirectories throws.
        // Closes gap 31 (wasi-nn/WACS-GAPS.md).
        private static Delegate? BuildOnnxGenAIConfigureCallback(
            Assembly nnAsm)
        {
            var optsType = nnAsm.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiNNDependencyInjectionOptions");
            if (optsType == null) return null;

            var addBackend = optsType.GetMethod("AddBackend");
            if (addBackend == null) return null;
            var addBackendParams = addBackend.GetParameters();
            if (addBackendParams.Length != 2) return null;
            var backendIfaceType = addBackendParams[1].ParameterType;

            var genaiAsm = TryLoadAssembly(
                "Wacs.WASI.NN.OnnxRuntimeGenAI");
            if (genaiAsm == null) return null;  // not an error.

            var genaiBackendType = genaiAsm.GetType(
                "Wacs.WASI.NN.OnnxRuntimeGenAI.OnnxGenAIBackend");
            if (genaiBackendType == null)
            {
                System.Console.Error.WriteLine(
                    "warn: Wacs.WASI.NN.OnnxRuntimeGenAI is loadable "
                    + "but OnnxGenAIBackend type wasn't found. "
                    + "wasi-nn GenAI guests will see NotFound errors.");
                return null;
            }

            var fromDirs = genaiBackendType.GetMethod(
                "FromDirectories",
                BindingFlags.Public | BindingFlags.Static);
            if (fromDirs == null)
            {
                System.Console.Error.WriteLine(
                    "warn: OnnxGenAIBackend.FromDirectories(IDictionary"
                    + "<string,string>) static factory not found. "
                    + "Cannot auto-wire OnnxRuntimeGenAI; embedders "
                    + "should construct OnnxGenAIBackend directly "
                    + "via AddWasiNN(b => b.AddBackend(...)).");
                return null;
            }

            var registry = BuildGenAIRegistryFromEnv();

            object? genaiBackend;
            try { genaiBackend = fromDirs.Invoke(null, new object?[] { registry }); }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "warn: OnnxGenAIBackend.FromDirectories failed: "
                    + ex.GetType().Name + ": " + ex.Message
                    + ". wasi-nn GenAI guests will see NotFound "
                    + "errors. Check that the Microsoft.ML.OnnxRuntimeGenAI "
                    + "native library is on the load path.");
                return null;
            }
            if (genaiBackend == null) return null;

            var configProp = optsType.GetProperty("Configuration");
            if (configProp == null) return null;
            var configType = configProp.PropertyType;
            var loadByNameProp = configType.GetProperty("LoadByNameBackend");
            if (loadByNameProp == null
                || loadByNameProp.GetSetMethod() == null)
            {
                System.Console.Error.WriteLine(
                    "warn: WasiNNConfiguration.LoadByNameBackend "
                    + "property missing or read-only; cannot route "
                    + "GenAI graph.load-by-name through OnnxGenAIBackend.");
                return null;
            }

            // GenAI backend takes the LoadByNameBackend slot only —
            // leaves Backends[ONNX] for the regular OnnxBackend so
            // `graph.load(bytes)` keeps its byte-tensor I/O path
            // while `graph.load-by-name(<dir>)` routes to GenAI's
            // KV-cached decode loop.
            var optsParam = Expression.Parameter(optsType, "opts");
            var configAccess = Expression.Property(optsParam, configProp);
            var assign = Expression.Assign(
                Expression.Property(configAccess, loadByNameProp),
                Expression.Constant(genaiBackend,
                    loadByNameProp.PropertyType));
            var actionType = typeof(Action<>).MakeGenericType(optsType);
            return Expression.Lambda(actionType, assign, optsParam)
                .Compile();
        }

        // Sibling of BuildOnnxConfigureCallback for the OpenVINO
        // backend. Closest analog of the four (both register via
        // the encoding-keyed Backends dict; neither uses
        // LoadByNameBackend or a directory-registry factory).
        // Closes the gap where the IBindable's BindHostFunction
        // registrations get silently shadowed under --wasip2 by
        // the direct-link path: the WitBindings handlers drop,
        // GraphFuncsImpl reads the DI-bundle's WasiNNConfiguration,
        // and without an auto-wire callback Backends[OpenVINO]
        // stays empty → InvalidEncoding at guest call time.
        //
        // Returns null when:
        //   - Wacs.WASI.NN.OpenVino isn't loadable (the common
        //     `wacs run --wasip2` case without the OpenVINO
        //     backend installed);
        //   - OpenVinoBackend type / parameterless ctor can't be
        //     resolved;
        //   - Activator.CreateInstance throws (typically because
        //     the OpenVINO native runtime isn't on the load
        //     path — `OpenVINO.runtime.<rid>` not installed).
        private static Delegate? BuildOpenVinoConfigureCallback(
            Assembly nnAsm)
        {
            var optsType = nnAsm.GetType(
                "Wacs.WASI.NN.DependencyInjection.WasiNNDependencyInjectionOptions");
            if (optsType == null) return null;

            var addBackend = optsType.GetMethod("AddBackend");
            if (addBackend == null) return null;
            var addBackendParams = addBackend.GetParameters();
            if (addBackendParams.Length != 2) return null;
            var encodingType = addBackendParams[0].ParameterType;
            var backendIfaceType = addBackendParams[1].ParameterType;

            var ovAsm = TryLoadAssembly("Wacs.WASI.NN.OpenVino");
            if (ovAsm == null) return null;  // not an error.

            var openVinoBackendType = ovAsm.GetType(
                "Wacs.WASI.NN.OpenVino.OpenVinoBackend");
            if (openVinoBackendType == null)
            {
                System.Console.Error.WriteLine(
                    "warn: Wacs.WASI.NN.OpenVino is loadable but "
                    + "OpenVinoBackend type wasn't found. wasi-nn "
                    + "OpenVINO guests will see InvalidEncoding errors.");
                return null;
            }

            object? openVinoBackend;
            try { openVinoBackend = Activator.CreateInstance(openVinoBackendType); }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine(
                    "warn: OpenVinoBackend instantiation failed: "
                    + ex.GetType().Name + ": " + ex.Message
                    + ". wasi-nn OpenVINO guests will see InvalidEncoding "
                    + "errors. Check that the OpenVINO native runtime "
                    + "(`OpenVINO.runtime.<rid>` NuGet) is on the load "
                    + "path.");
                return null;
            }
            if (openVinoBackend == null) return null;

            // GraphEncoding.OpenVINO = 0 in the WIT enum.
            object openVinoEncoding = Enum.ToObject(encodingType, 0);

            var optsParam = Expression.Parameter(optsType, "opts");
            var call = Expression.Call(optsParam, addBackend,
                Expression.Constant(openVinoEncoding, encodingType),
                Expression.Constant(openVinoBackend, backendIfaceType));
            var actionType = typeof(Action<>).MakeGenericType(optsType);
            return Expression.Lambda(actionType, call, optsParam).Compile();
        }

        // Mirrors WasiNNOnnxGenAIBindable's env-driven scan:
        // read $WACS_WASINN_GENAI_DIR, enumerate first-level
        // subdirectories containing `genai_config.json`, register
        // each under its directory name. Fail-soft on IO error.
        private static IDictionary<string, string>
            BuildGenAIRegistryFromEnv()
        {
            var dir = Environment.GetEnvironmentVariable(
                "WACS_WASINN_GENAI_DIR");
            var registry = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(dir)) return registry;
            try
            {
                if (!Directory.Exists(dir)) return registry;
                foreach (var modelDir in Directory.EnumerateDirectories(dir))
                {
                    var configPath = Path.Combine(
                        modelDir, "genai_config.json");
                    if (!File.Exists(configPath)) continue;
                    var name = Path.GetFileName(modelDir);
                    if (!string.IsNullOrEmpty(name))
                        registry[name!] = modelDir;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return registry;
        }

        // Mirrors WasiNNTorchSharpBindable's env-driven scan:
        // read $WACS_WASINN_TORCH_DIR, enumerate *.pt + *.ts in
        // the top-level dir, register each under filename-sans-
        // extension. Fail-soft on IO error.
        private static IDictionary<string, string>
            BuildTorchScriptRegistryFromEnv()
        {
            var dir = Environment.GetEnvironmentVariable(
                "WACS_WASINN_TORCH_DIR");
            var registry = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(dir)) return registry;
            try
            {
                if (!Directory.Exists(dir)) return registry;
                foreach (var pattern in new[] { "*.pt", "*.ts" })
                {
                    foreach (var path in Directory.EnumerateFiles(dir,
                        pattern, SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileNameWithoutExtension(path);
                        if (!string.IsNullOrEmpty(name))
                            registry[name!] = path;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return registry;
        }

        // Mirrors WasiNNLlamaSharpBindable's env-driven scan:
        // read $WACS_WASINN_GGUF_DIR, enumerate *.gguf files in
        // the top-level dir, register each under its filename-
        // sans-extension. Empty registry on miss / unset / IO
        // error — matches the IBindable's fail-soft posture so
        // the auto-wire never crashes scope construction.
        private static IDictionary<string, string>
            BuildGgufRegistryFromEnv()
        {
            var dir = Environment.GetEnvironmentVariable(
                "WACS_WASINN_GGUF_DIR");
            var registry = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(dir)) return registry;
            try
            {
                if (!Directory.Exists(dir)) return registry;
                foreach (var path in Directory.EnumerateFiles(dir,
                    "*.gguf", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (!string.IsNullOrEmpty(name))
                        registry[name!] = path;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return registry;
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
