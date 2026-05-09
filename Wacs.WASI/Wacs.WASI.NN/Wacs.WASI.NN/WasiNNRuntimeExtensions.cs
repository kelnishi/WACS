// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.Core.Runtime;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN
{
    /// <summary>
    /// Ergonomic one-liner to wire wasi-nn onto a
    /// <see cref="WasmRuntime"/>. Replaces the multi-step
    /// "construct config → register backends → build host →
    /// call BindToRuntime" sequence with a single call that
    /// matches the shape embedders use for the rest of the
    /// host stack (e.g. <c>runtime.UseWasiPreview2(...)</c>
    /// once that lands).
    ///
    /// <para>Usage:
    /// <code>
    /// runtime.UseWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()));
    /// </code>
    /// </para>
    /// </summary>
    public static class WasiNNRuntimeExtensions
    {
        /// <summary>
        /// Construct and bind a <see cref="WasiNNHost"/> using
        /// the configuration produced by <paramref name="configure"/>.
        /// Returns the host so the caller can hold its lifetime
        /// (it owns resource tables that must outlive the
        /// runtime's invocations of wasi-nn imports).
        /// </summary>
        public static WasiNNHost UseWasiNN(this WasmRuntime runtime,
            Action<WasiNNBuilder>? configure = null)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var builder = new WasiNNBuilder();
            configure?.Invoke(builder);
            var host = new WasiNNHost(builder.Build());
            host.BindToRuntime(runtime);
            return host;
        }
    }

    /// <summary>
    /// Fluent builder for <see cref="WasiNNConfiguration"/>.
    /// Lets <c>runtime.UseWasiNN(b => b.AddBackend(...).AllowLoadByName(...))</c>
    /// read like the rest of the host wiring without the
    /// caller threading a config object through their setup.
    /// </summary>
    public sealed class WasiNNBuilder
    {
        private readonly Dictionary<GraphEncoding, IBackend> _backends = new();
        private bool _allowLoadByName;
        private Func<string, NamedModel?>? _resolver;

        /// <summary>
        /// Register <paramref name="backend"/> for
        /// <paramref name="encoding"/>. A single backend can be
        /// registered for multiple encodings via repeat calls.
        /// </summary>
        public WasiNNBuilder AddBackend(GraphEncoding encoding, IBackend backend)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            _backends[encoding] = backend;
            return this;
        }

        /// <summary>
        /// Enable <c>graph.load-by-name</c> with a host-side
        /// resolver. Off by default — guests calling load-by-name
        /// trip <see cref="ErrorCode.UnsupportedOperation"/> until
        /// this is wired.
        /// </summary>
        public WasiNNBuilder AllowLoadByName(Func<string, NamedModel?> resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _allowLoadByName = true;
            return this;
        }

        internal WasiNNConfiguration Build()
        {
            var cfg = WasiNNConfiguration.DefaultConfiguration();
            foreach (var kv in _backends) cfg.Backends[kv.Key] = kv.Value;
            cfg.AllowLoadByName = _allowLoadByName;
            cfg.NamedModelResolver = _resolver;
            return cfg;
        }
    }
}
