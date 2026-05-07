// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using Microsoft.ML;
using Microsoft.ML.OnnxRuntime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.Types;

namespace Wacs.WASI.NN.MLNet
{
    /// <summary>
    /// ML.NET-flavored backend for <see cref="GraphEncoding.ONNX"/>.
    /// Owns a <see cref="MLContext"/> for lifecycle integration —
    /// embedders that want to chain wasi-nn inference with
    /// other Microsoft.ML transformers (normalizers, custom
    /// predictors, etc.) can pass in a pre-configured context
    /// and reach it through their own pipeline composition.
    ///
    /// <para>For raw tensor I/O the actual inference flows
    /// through <see cref="InferenceSession"/> directly — ML.NET's
    /// <c>IDataView</c> / <c>PredictionEngine&lt;TIn,TOut&gt;</c>
    /// pipeline is row-oriented and requires strongly-typed
    /// schema classes, which doesn't fit a backend that has to
    /// load arbitrary ONNX models with unknown shapes at
    /// runtime. Microsoft.ML.OnnxTransformer wraps the same
    /// ORT API under the hood, so the inference itself is
    /// equivalent to <c>Wacs.WASI.NN.OnnxRuntime.OnnxBackend</c>;
    /// the value-add of this package is the MLContext lifecycle
    /// + the surface for embedders to extend with pipeline
    /// transformers.</para>
    ///
    /// <para>Encodings: ONNX + Autodetect. TensorFlow /
    /// TensorFlowLite throw <see cref="ErrorCode.UnsupportedOperation"/>
    /// in v0 — wiring them needs the heavyweight
    /// Microsoft.ML.TensorFlow package and additional native
    /// binaries; embedders that need TF inference should pass
    /// a custom <see cref="IBackend"/> for those encodings.</para>
    /// </summary>
    public sealed class MLNetBackend : IBackend
    {
        private readonly MLContext _mlContext;
        private readonly Func<SessionOptions>? _sessionOptionsFactory;

        /// <summary>
        /// Default ctor — fresh <see cref="MLContext"/>, default
        /// session options.
        /// </summary>
        public MLNetBackend() : this(null, null) { }

        /// <summary>
        /// Construct with embedder-supplied <see cref="MLContext"/>.
        /// Useful when the embedder wants to share one context
        /// across wasi-nn inference and other ML.NET work
        /// (logging, threading, deterministic-seed config, etc.).
        /// </summary>
        public MLNetBackend(MLContext context)
            : this(() => context, null) { }

        /// <summary>
        /// Construct with both factories. The
        /// <paramref name="sessionOptionsFactory"/> is invoked
        /// once per <see cref="LoadGraph"/> so each graph owns
        /// its own <see cref="SessionOptions"/> (ORT requires
        /// options to outlive the session). Returning <c>null</c>
        /// from the session factory falls back to default
        /// options.
        /// </summary>
        public MLNetBackend(
            Func<MLContext>? mlContextFactory,
            Func<SessionOptions>? sessionOptionsFactory)
        {
            _mlContext = mlContextFactory?.Invoke() ?? new MLContext();
            _sessionOptionsFactory = sessionOptionsFactory;
        }

        public IReadOnlyCollection<GraphEncoding> SupportedEncodings { get; }
            = new[] { GraphEncoding.ONNX, GraphEncoding.Autodetect };

        /// <summary>
        /// The <see cref="MLContext"/> this backend owns.
        /// Exposed so embedders can attach further transformers
        /// or share it across other ML.NET work. Disposing the
        /// context outside this backend is the embedder's
        /// responsibility — backends don't enforce ownership of
        /// the context.
        /// </summary>
        public MLContext Context => _mlContext;

        public IBackendGraph LoadGraph(
            IReadOnlyList<ReadOnlyMemory<byte>> builders,
            ExecutionTarget target)
        {
            if (target == ExecutionTarget.TPU)
                throw new WasiNNException(
                    ErrorCode.UnsupportedOperation,
                    "MLNetBackend does not support ExecutionTarget.TPU");
            if (builders.Count == 0)
                throw new WasiNNException(
                    ErrorCode.InvalidArgument,
                    "graph.load received an empty builder list");

            byte[] modelBytes = ConcatBuilders(builders);

            // Quick sniff: ONNX protobuf typically starts with
            // 0x08 (field 1, varint — ir_version). When the
            // guest passed Autodetect we accept anything that
            // ORT can parse; the format check is best-effort.
            // Real validation happens in the InferenceSession
            // ctor below, which throws on malformed bytes.

            var options = _sessionOptionsFactory?.Invoke() ?? new SessionOptions();
            try
            {
                var session = new InferenceSession(modelBytes, options);
                return new MLNetGraph(_mlContext, session, options);
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
            // Same as OnnxBackend: no internal registry. The
            // host-level NamedModelResolver on
            // WasiNNConfiguration handles this; this method
            // only fires if an embedder calls the backend
            // directly.
            throw new WasiNNException(
                ErrorCode.NotFound,
                "MLNetBackend has no internal named-model registry; "
                + "configure WasiNNConfiguration.NamedModelResolver "
                + "to map names to (encoding, builders) pairs.");
        }

        private static byte[] ConcatBuilders(IReadOnlyList<ReadOnlyMemory<byte>> builders)
        {
            if (builders.Count == 1) return builders[0].ToArray();
            int total = 0;
            for (int i = 0; i < builders.Count; i++) total += builders[i].Length;
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
