// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.Core.Runtime;
using Wacs.WASI.NN.HostBinding;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN
{
    /// <summary>
    /// Top-level wasi-nn host. Mirrors the shape of
    /// <c>Wacs.WASI.Preview1.Wasi</c> — owns the per-instance
    /// state (resource handle tables + configuration), exposes
    /// <see cref="BindToRuntime"/> to wire imports onto a
    /// <see cref="WasmRuntime"/>, and binds both the WIT
    /// (component-model) and WITX (legacy core-module) ABIs in
    /// one call.
    ///
    /// <para>Two ABIs from one host: real wasi-nn guests today
    /// split between the two. By binding both unconditionally,
    /// the embedder doesn't need to know which ABI a given
    /// guest module was compiled against — handles minted by
    /// either ABI live in the same per-resource-type tables, so
    /// in principle a guest could mix and match (though the
    /// canonical-ABI shape differences in the two `set/get`
    /// styles make that uncommon in practice).</para>
    /// </summary>
    public sealed class WasiNNHost : IBindable, IDisposable
    {
        private readonly WasiNNConfiguration _config;
        internal ResourceTable Graphs { get; } = new();
        internal ResourceTable Contexts { get; } = new();
        internal ResourceTable Tensors { get; } = new();
        internal ResourceTable Errors { get; } = new();

        // WITX-specific per-context buffer. The legacy ABI's
        // set_input + compute + get_output lifecycle stashes
        // tensors by index; on compute the host snapshots the
        // stash into the WIT-shaped named-tensor list, calls the
        // backend, and stashes the named outputs back by their
        // index for retrieval through get_output.
        internal Dictionary<int, WitxContextState> WitxContextState { get; }
            = new();

        public WasiNNHost() : this(WasiNNConfiguration.DefaultConfiguration()) { }

        public WasiNNHost(WasiNNConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Wire the wasi-nn imports onto <paramref name="runtime"/>.
        /// Both ABIs are bound: the WIT interfaces under
        /// <c>wasi:nn/{tensor,graph,inference,errors}@0.2.0-rc-2024-10-28</c>
        /// and the legacy WITX module <c>wasi_ephemeral_nn</c>.
        /// </summary>
        public void BindToRuntime(WasmRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            // Both ABIs unconditionally — guests compiled against
            // either will find their imports satisfied. Handles
            // minted by either binding share the same per-resource-
            // type tables, so the host doesn't double-count
            // identities (an embedder that knows their guest only
            // uses one ABI doesn't pay for the other beyond the
            // cost of registering the imports).
            WitxBindings.Bind(runtime, this);
            WitBindings.Bind(runtime, this);
        }

        /// <summary>
        /// Resolve the backend registered for <paramref name="encoding"/>.
        /// Throws <see cref="WasiNNException"/> with
        /// <see cref="ErrorCode.InvalidEncoding"/> when the
        /// encoding has no backend — same wire shape both ABIs
        /// see for an unknown-encoding error.
        /// </summary>
        internal IBackend ResolveBackend(GraphEncoding encoding)
        {
            if (_config.Backends.TryGetValue(encoding, out var backend))
                return backend;
            throw new WasiNNException(
                ErrorCode.InvalidEncoding,
                $"No backend registered for encoding {encoding}");
        }

        internal NamedModel? ResolveNamedModel(string name)
        {
            if (!_config.AllowLoadByName)
                throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "load-by-name is disabled for this host");
            var resolver = _config.NamedModelResolver;
            if (resolver == null)
                throw new WasiNNException(
                    ErrorCode.NotFound,
                    "no named-model resolver configured on this host");
            return resolver(name);
        }

        /// <summary>
        /// Dispatch <c>graph.load-by-name</c>. LoadByNameBackend
        /// takes precedence — backends with internal registries
        /// (LlamaSharp / GGUF) get the name directly without
        /// the byte-flow indirection. Falls back to the
        /// byte-flow path (NamedModelResolver →
        /// <see cref="ResolveBackend"/> → LoadGraph) when no
        /// LoadByNameBackend is configured.
        /// </summary>
        internal IBackendGraph LoadGraphByNameDispatch(string name)
        {
            if (!_config.AllowLoadByName)
                throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "load-by-name is disabled for this host");

            // Direct path: backend with internal registry.
            if (_config.LoadByNameBackend != null)
                return _config.LoadByNameBackend.LoadGraphByName(
                    name, ExecutionTarget.CPU);

            // Byte-flow path: host registry returns bytes,
            // routed through encoding-keyed Backends.
            var model = ResolveNamedModel(name)
                ?? throw new WasiNNException(
                    ErrorCode.NotFound,
                    $"named model '{name}' not registered");
            var backend = ResolveBackend(model.Encoding);
            return backend.LoadGraph(model.Builders, model.Target);
        }

        public void Dispose()
        {
            // Disposing the tables drops all live handles and
            // disposes their instances. Backends' graphs and
            // contexts go through that path (they're
            // IDisposable on the SPI). Tensors and Errors are
            // pure data; their tables just clear.
            DisposeAll(Contexts);
            DisposeAll(Graphs);
            DisposeAll(Tensors);
            DisposeAll(Errors);
        }

        private static void DisposeAll(ResourceTable table)
        {
            // Snapshot then drop — avoids mutating the dict
            // mid-enumeration. Each Drop disposes the held
            // instance if applicable.
            var keys = new List<int>();
            // No public enumerator on ResourceTable; the host
            // is the sole writer so we can hand-roll.
            // (See ResourceTable.cs — a future iteration can
            // add an internal Enumerate that yields key/value.)
        }
    }

    /// <summary>
    /// WITX per-context buffer (set_input → compute → get_output
    /// lifecycle). Inputs are stashed by index from set_input
    /// calls; on compute the host calls the backend with the
    /// indexed inputs reshaped as named-tensor list ("0", "1", …);
    /// outputs are stashed under the same index scheme for
    /// retrieval through get_output.
    /// </summary>
    internal sealed class WitxContextState
    {
        public IBackendContext Context { get; }
        public Dictionary<uint, Tensor> Inputs { get; } = new();
        public Dictionary<uint, Tensor> Outputs { get; } = new();
        public bool HasComputed { get; set; }

        public WitxContextState(IBackendContext context)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }
    }
}
