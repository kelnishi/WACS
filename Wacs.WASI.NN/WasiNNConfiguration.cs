// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN
{
    /// <summary>
    /// Embedder-supplied configuration for the wasi-nn host
    /// surface. Mirrors the shape of <c>WasiConfiguration</c>
    /// in <c>Wacs.WASI.Preview1</c> — POCO with a static
    /// <see cref="DefaultConfiguration"/> factory for the
    /// "stand it up with sensible defaults" path. Configurations
    /// are passed by value into the
    /// <see cref="WasiNNHost"/> ctor; mutating the configuration
    /// after construction has no effect.
    ///
    /// <para>The configuration is required for any operation that
    /// touches a graph. With no backends registered the host
    /// rejects every <c>graph.load</c>/<c>load-by-name</c> with
    /// <see cref="ErrorCode.InvalidEncoding"/> — there is no
    /// implicit "use a default model" path, since real models
    /// are GB-scale and bundling them is the embedder's call.</para>
    /// </summary>
    public sealed class WasiNNConfiguration
    {
        /// <summary>
        /// Backend dispatch table keyed by graph encoding. The
        /// <see cref="GraphEncoding.Autodetect"/> route is
        /// registered explicitly when supported; the host does
        /// not otherwise iterate backends to pick one.
        /// </summary>
        public Dictionary<GraphEncoding, IBackend> Backends { get; set; }
            = new Dictionary<GraphEncoding, IBackend>();

        /// <summary>
        /// Resolver for <c>graph.load-by-name</c>. The function
        /// receives the guest-supplied name and must return a
        /// (encoding, builders) pair; the host then dispatches
        /// to the backend registered for that encoding. Returning
        /// null signals "name not found" — the binding maps that
        /// to <see cref="ErrorCode.NotFound"/>.
        ///
        /// <para>Default null — no named-model registry. Embedders
        /// running an LLM workflow (LlamaSharp / GGUF) MUST set
        /// this to surface their model catalog.</para>
        /// </summary>
        public Func<string, NamedModel?>? NamedModelResolver { get; set; }

        /// <summary>
        /// When true, <c>graph.load-by-name</c> resolves through
        /// <see cref="NamedModelResolver"/> and routes via
        /// <see cref="Backends"/>; when false, the operation is
        /// rejected with <see cref="ErrorCode.UnsupportedOperation"/>
        /// regardless of whether a resolver is set. Default true.
        /// </summary>
        public bool AllowLoadByName { get; set; } = true;

        /// <summary>
        /// Standard-defaults factory. Returns a
        /// <see cref="WasiNNConfiguration"/> with no backends
        /// registered and no named-model resolver wired —
        /// embedders fill in the backends they want before
        /// constructing <see cref="WasiNNHost"/>.
        ///
        /// <para>The defaults factory exists to mirror
        /// <c>Wasi.DefaultConfiguration</c>'s shape — every
        /// embedder gets the same starting point, then layers
        /// on the backends and registry they need.</para>
        /// </summary>
        public static WasiNNConfiguration DefaultConfiguration()
        {
            return new WasiNNConfiguration();
        }
    }

    /// <summary>
    /// Result row for <see cref="WasiNNConfiguration.NamedModelResolver"/>.
    /// The resolver returns the encoding + the builder buffers —
    /// the host then routes to the registered backend for that
    /// encoding via <see cref="WasiNNConfiguration.Backends"/>,
    /// the same path <c>graph.load</c> takes.
    /// </summary>
    public readonly struct NamedModel
    {
        public GraphEncoding Encoding { get; }
        public IReadOnlyList<ReadOnlyMemory<byte>> Builders { get; }
        public ExecutionTarget Target { get; }

        public NamedModel(
            GraphEncoding encoding,
            IReadOnlyList<ReadOnlyMemory<byte>> builders,
            ExecutionTarget target = ExecutionTarget.CPU)
        {
            Encoding = encoding;
            Builders = builders ?? throw new ArgumentNullException(nameof(builders));
            Target = target;
        }
    }
}
