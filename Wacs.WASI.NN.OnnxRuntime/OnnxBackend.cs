// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.OnnxRuntime
{
    /// <summary>
    /// <see cref="IBackend"/> implementation for
    /// <see cref="GraphEncoding.ONNX"/> backed by the official
    /// Microsoft.ML.OnnxRuntime NuGet. Ships as a sibling
    /// package so consumers wiring only one backend don't pull
    /// the ORT native binaries (~50 MB across the supported
    /// platform RIDs).
    ///
    /// <para>Lifetime: each <see cref="LoadGraph"/> call creates
    /// one <see cref="InferenceSession"/>; multiple contexts
    /// minted from the same graph share that session (ORT
    /// sessions are thread-safe for concurrent <c>Run</c> calls
    /// and ONNX models are inherently stateless across runs).
    /// The session is disposed when the
    /// <see cref="OnnxGraph"/> is dropped, which the WIT
    /// <c>[resource-drop]graph</c> binding triggers when the
    /// guest releases the handle.</para>
    ///
    /// <para>GPU execution providers (CUDA, DirectML, CoreML)
    /// aren't auto-wired in v0 — embedders that want them pass
    /// a <see cref="SessionOptions"/> factory through the
    /// constructor that calls
    /// <see cref="SessionOptions.AppendExecutionProvider_CUDA(int)"/>
    /// (or the EP they need). Default execution is CPU only;
    /// guest <see cref="ExecutionTarget"/> requests for GPU
    /// fall through to CPU unless the factory's options say
    /// otherwise. Guest <see cref="ExecutionTarget.TPU"/>
    /// throws <see cref="ErrorCode.UnsupportedOperation"/> —
    /// ORT has no public TPU EP.</para>
    /// </summary>
    public sealed class OnnxBackend : IBackend
    {
        private readonly Func<SessionOptions>? _optionsFactory;

        /// <summary>
        /// Create the backend with default <see cref="SessionOptions"/>
        /// (CPU execution, no logging, default thread pool).
        /// </summary>
        public OnnxBackend() : this(null) { }

        /// <summary>
        /// Create the backend with embedder-controlled
        /// <see cref="SessionOptions"/>. The factory is invoked
        /// once per <see cref="LoadGraph"/> so each graph can
        /// own its own options instance (ORT requires options
        /// outlive the session). Returning <c>null</c> from
        /// the factory falls back to default options.
        /// </summary>
        public OnnxBackend(Func<SessionOptions>? sessionOptionsFactory)
        {
            _optionsFactory = sessionOptionsFactory;
        }

        public IReadOnlyCollection<GraphEncoding> SupportedEncodings { get; }
            = new[] { GraphEncoding.ONNX };

        public IBackendGraph LoadGraph(
            IReadOnlyList<ReadOnlyMemory<byte>> builders,
            ExecutionTarget target)
        {
            if (target == ExecutionTarget.TPU)
                throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "OnnxBackend does not support ExecutionTarget.TPU; "
                    + "use a SessionOptions factory to configure a GPU EP "
                    + "if hardware acceleration is required.");
            if (builders.Count == 0)
                throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    "graph.load received an empty builder list");

            // ONNX is a single self-contained protobuf; the
            // canonical case is one builder. Multi-builder
            // input gets concatenated defensively (no real
            // ONNX guests we know of split a model across
            // builders, but the spec allows it for backends
            // like OpenVINO that need IR + weights split).
            byte[] modelBytes = ConcatBuilders(builders);

            var options = _optionsFactory?.Invoke() ?? new SessionOptions();
            try
            {
                var session = new InferenceSession(modelBytes, options);
                return new OnnxGraph(session, options);
            }
            catch (Exception ex)
            {
                options.Dispose();
                throw new WasiNNException(
                    ErrorCode.RuntimeError,
                    $"InferenceSession construction failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
        }

        public IBackendGraph LoadGraphByName(string name, ExecutionTarget target)
        {
            // The named-model resolver lives on
            // WasiNNConfiguration; the host calls
            // LoadGraph(model.Builders, model.Target) after
            // looking the name up. This per-backend
            // load-by-name path is a fallback for backends
            // that have their own internal registry — ORT
            // does not, so route to NotFound.
            throw new WasiNNException(
                ErrorCode.NotFound,
                "OnnxBackend has no internal named-model registry; "
                + "configure WasiNNConfiguration.NamedModelResolver "
                + "to map names to (encoding, builders) pairs.");
        }

        private static byte[] ConcatBuilders(IReadOnlyList<ReadOnlyMemory<byte>> builders)
        {
            if (builders.Count == 1)
            {
                // Single buffer: ToArray() forces a copy. The
                // canonical-ABI lift already materialized into
                // a host array, so this is converting
                // ReadOnlyMemory<byte> back to byte[] for the
                // ORT API which doesn't accept ReadOnlyMemory.
                // Phase 4 zero-copy lift hands an
                // already-pinned byte[] through, eliminating
                // the second copy.
                return builders[0].ToArray();
            }
            int total = 0;
            for (int i = 0; i < builders.Count; i++)
                total += builders[i].Length;
            var concat = new byte[total];
            int offset = 0;
            for (int i = 0; i < builders.Count; i++)
            {
                builders[i].Span.CopyTo(concat.AsSpan(offset));
                offset += builders[i].Length;
            }
            return concat;
        }
    }
}
