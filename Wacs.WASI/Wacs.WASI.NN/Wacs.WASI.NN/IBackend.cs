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
    /// Provider plug-point for a wasi-nn backend (ML.NET, ONNX
    /// Runtime, LlamaSharp, …). The SPI is the same shape both
    /// ABIs (WIT and WITX) depend on; backends never see wire
    /// types or guest memory directly. The two ABIs converge on
    /// this surface, then each binding handles its own
    /// canonical-ABI / linear-memory plumbing on top.
    ///
    /// <para>Backends are registered with
    /// <see cref="WasiNNConfiguration.Backends"/> keyed by the
    /// <see cref="GraphEncoding"/> they handle. A single
    /// implementation can claim multiple encodings (e.g. ML.NET
    /// covers <c>tensorflow</c> + <c>tensorflowlite</c> +
    /// <c>autodetect</c>), but each encoding maps to exactly one
    /// backend per configuration — no fallthrough.</para>
    /// </summary>
    public interface IBackend
    {
        /// <summary>
        /// Encodings this backend will accept. The orchestrator
        /// uses this for registration validation; per-call
        /// encoding routing reads from
        /// <see cref="WasiNNConfiguration.Backends"/> directly.
        /// </summary>
        IReadOnlyCollection<GraphEncoding> SupportedEncodings { get; }

        /// <summary>
        /// Load a graph from raw byte buffers. Multiple buffers
        /// support backends like OpenVINO that split IR + weights
        /// across files; most backends will use exactly one
        /// element.
        ///
        /// <para><b>Ownership contract:</b> the
        /// <see cref="ReadOnlyMemory{T}"/> instances passed in
        /// MAY be views over live guest linear memory and MUST
        /// be consumed within this call. Backends that need the
        /// bytes past <c>LoadGraph</c> return must copy them
        /// (typically the load API of the underlying ML
        /// framework already does — ORT, LlamaSharp, etc.
        /// pin/copy the model bytes into native memory at
        /// session/weights construction time, so the wrapper
        /// can drop safely afterward). Retaining a reference
        /// past return is undefined behavior — a guest
        /// <c>memory.grow</c> after this call may relocate the
        /// linear-memory page and invalidate the view.</para>
        /// </summary>
        IBackendGraph LoadGraph(
            IReadOnlyList<ReadOnlyMemory<byte>> builders,
            ExecutionTarget target);

        /// <summary>
        /// Resolve a named graph from the host's pre-configured
        /// model registry. Wired from
        /// <see cref="WasiNNConfiguration.NamedModelResolver"/>;
        /// the backend interprets the result however its loader
        /// needs (file path, in-memory weights, etc.). Throw
        /// <see cref="WasiNNException"/> with
        /// <see cref="ErrorCode.NotFound"/> if the name is unknown.
        ///
        /// <para>Convention for LLM/GGUF guests (matching
        /// WasmEdge): the name is the model identifier the guest
        /// chose; the host registry maps it to the GGUF file path
        /// or pre-loaded weights.</para>
        /// </summary>
        IBackendGraph LoadGraphByName(string name, ExecutionTarget target);
    }

    /// <summary>
    /// A loaded graph (the model IR + weights). Owns native
    /// resources; the WIT and WITX bindings dispose it when the
    /// guest drops the corresponding handle.
    /// </summary>
    public interface IBackendGraph : IDisposable
    {
        /// <summary>
        /// Mint a fresh inference context. Multiple contexts
        /// over a single graph let the host fan out concurrent
        /// inferences without re-loading weights.
        /// </summary>
        IBackendContext CreateContext();
    }

    /// <summary>
    /// One inference session against a graph. Computes a single
    /// forward pass per <see cref="Compute"/> call; backends
    /// internally hold whatever per-session state they need
    /// (KV cache for LLMs, ORT-session, ML.NET prediction
    /// engine, …).
    ///
    /// <para>The signature matches the WIT shape — inputs and
    /// outputs are named-tensor lists, returned synchronously as
    /// a single value. The WITX binding synthesizes input names
    /// by index ("0", "1", …) and reads outputs out of the
    /// returned list by the same scheme.</para>
    /// </summary>
    public interface IBackendContext : IDisposable
    {
        /// <summary>
        /// Run inference on the supplied inputs and return the
        /// outputs. Backends MUST throw <see cref="WasiNNException"/>
        /// (not generic exceptions) for runtime failures so the
        /// binding layer can lower the error to the right wire
        /// representation per ABI.
        /// </summary>
        IReadOnlyList<NamedTensor> Compute(IReadOnlyList<NamedTensor> inputs);
    }
}
