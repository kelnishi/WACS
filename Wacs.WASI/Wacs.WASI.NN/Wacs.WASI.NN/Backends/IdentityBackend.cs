// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.Backends
{
    /// <summary>
    /// Trivial backend that passes inputs straight through as
    /// outputs (rename "0" → "0", "1" → "1", …) — exists so the
    /// host plumbing can be smoke-tested without any heavy ML
    /// NuGet dependency. The WIT/WITX bindings, the resource
    /// tables, the canonical-ABI lifts, and the
    /// <see cref="WasiNNException"/> error path can all be
    /// exercised end-to-end against this backend.
    ///
    /// <para>Claims <see cref="GraphEncoding.Autodetect"/> by
    /// default; embedders configuring it as the explicit
    /// fall-through register additional encodings as they need.
    /// Tests register it under the encoding(s) they're
    /// exercising.</para>
    /// </summary>
    public sealed class IdentityBackend : IBackend
    {
        private readonly GraphEncoding[] _encodings;

        public IdentityBackend() : this(GraphEncoding.Autodetect) { }

        public IdentityBackend(params GraphEncoding[] encodings)
        {
            _encodings = encodings ?? throw new ArgumentNullException(nameof(encodings));
        }

        public IReadOnlyCollection<GraphEncoding> SupportedEncodings => _encodings;

        public IBackendGraph LoadGraph(
            IReadOnlyList<ReadOnlyMemory<byte>> builders,
            ExecutionTarget target)
        {
            // Echo backend ignores the bytes; any builder shape
            // is accepted, including zero-builder lists. Captures
            // total length for diagnostics only.
            long total = 0;
            for (int i = 0; i < builders.Count; i++)
                total += builders[i].Length;
            return new Graph(total);
        }

        public IBackendGraph LoadGraphByName(string name, ExecutionTarget target)
        {
            // Identity has no model registry; route every name
            // to a fresh empty graph. Embedders wiring a real
            // backend should override the resolver instead.
            return new Graph(0);
        }

        private sealed class Graph : IBackendGraph
        {
            private readonly long _totalBytes;
            public Graph(long totalBytes) => _totalBytes = totalBytes;

            public IBackendContext CreateContext() => new Context();
            public void Dispose() { /* no native handles */ }
        }

        private sealed class Context : IBackendContext
        {
            public IReadOnlyList<NamedTensor> Compute(IReadOnlyList<NamedTensor> inputs)
            {
                // Pass-through: echo each input as the
                // corresponding output. Tests that want to
                // verify per-tensor byte fidelity can compare
                // input.Data against output.Data.
                var outputs = new List<NamedTensor>(inputs.Count);
                for (int i = 0; i < inputs.Count; i++)
                    outputs.Add(inputs[i]);
                return outputs;
            }
            public void Dispose() { }
        }
    }
}
