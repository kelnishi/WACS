// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
    /// <para>Hardware acceleration is opt-in via either the
    /// <c>WACS_WASINN_ONNX_EP</c> environment variable
    /// (<c>auto|coreml|cuda|dml|rocm</c>) or by passing an
    /// <see cref="OnnxBackendOptions"/> with a non-CPU
    /// <see cref="OnnxBackendOptions.ExecutionProvider"/>. The
    /// CPU default matches the pre-v0.3.0 behavior and avoids the
    /// silent op-coverage issues seen with CoreML / DirectML
    /// against generative-LLM ops. <see cref="OnnxExecutionProvider.Auto"/>
    /// resolves at session-construction time to the platform-best
    /// EP (CoreML on macOS, DirectML on Windows, CUDA / ROCm on
    /// Linux) and silently falls back to CPU on EP-append failure.
    /// Library embedders that need fine-grained
    /// <see cref="SessionOptions"/> control pass a
    /// <see cref="Func{SessionOptions}"/> factory instead — full
    /// escape hatch that wins over the typed-options path.
    /// Guest <see cref="ExecutionTarget.TPU"/> throws
    /// <see cref="ErrorCode.UnsupportedOperation"/> — ORT has no
    /// public TPU EP.</para>
    /// </summary>
    public sealed class OnnxBackend : IBackend
    {
        private readonly Func<SessionOptions>? _optionsFactory;
        private readonly OnnxBackendOptions _options;

        /// <summary>
        /// Create the backend reading
        /// <see cref="OnnxBackendOptions.FromEnvironment"/> for
        /// EP selection. Default is CPU unless
        /// <c>WACS_WASINN_ONNX_EP</c> is set — hardware
        /// acceleration is opt-in. See the class docs above for
        /// the Auto-resolution platform pick order.
        /// </summary>
        public OnnxBackend() : this(OnnxBackendOptions.FromEnvironment()) { }

        /// <summary>
        /// Create the backend with typed
        /// <see cref="OnnxBackendOptions"/>. Useful for library
        /// embedders that pin a specific EP at startup (or opt out
        /// of acceleration) without touching env vars.
        /// </summary>
        public OnnxBackend(OnnxBackendOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Create the backend with embedder-controlled
        /// <see cref="SessionOptions"/>. The factory is invoked
        /// once per <see cref="LoadGraph"/> so each graph can
        /// own its own options instance (ORT requires options
        /// outlive the session). Returning <c>null</c> from
        /// the factory falls back to default CPU options. Wins
        /// over the typed <see cref="OnnxBackendOptions"/> path —
        /// embedders that need both should append EPs inside the
        /// factory themselves.
        /// </summary>
        public OnnxBackend(Func<SessionOptions>? sessionOptionsFactory)
        {
            _optionsFactory = sessionOptionsFactory;
            _options = OnnxBackendOptions.FromEnvironment();
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

            SessionOptions options;
            try
            {
                options = _optionsFactory?.Invoke()
                    ?? BuildSessionOptions(_options);
            }
            catch (Exception ex)
            {
                // Strict-mode EP-append failure (FallbackToCpu=false)
                // surfaces here. Wrap into the family's error type
                // so guests see a consistent RuntimeError instead of
                // EntryPointNotFoundException / DllNotFoundException
                // / TypeInitializationException leaking through.
                throw new WasiNNException(
                    ErrorCode.RuntimeError,
                    $"SessionOptions construction failed: {ex.Message}",
                    backendData: ex.ToString(),
                    innerException: ex);
            }
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

        // Build a SessionOptions instance honoring the typed
        // OnnxBackendOptions: resolves Auto -> platform-pick, then
        // appends the chosen EP. Falls back to plain CPU options
        // (no EP append) on EP-append failure when
        // FallbackToCpu=true — handles the common "CUDA EP NuGet
        // not on the host" or "CoreML EP append refused" cases
        // without breaking inference. Strict mode (FallbackToCpu
        // =false) propagates the failure so misconfiguration
        // surfaces as RuntimeError at graph.load time.
        private static SessionOptions BuildSessionOptions(
            OnnxBackendOptions opts)
        {
            var ep = opts.ExecutionProvider;
            if (ep == OnnxExecutionProvider.Auto)
                ep = ResolveAuto();
            if (ep == OnnxExecutionProvider.Cpu)
                return new SessionOptions();

            var sessionOptions = new SessionOptions();
            try
            {
                AppendEp(sessionOptions, ep, opts);
                return sessionOptions;
            }
            catch (Exception)
            {
                sessionOptions.Dispose();
                if (!opts.FallbackToCpu) throw;
                // CPU fallback. The caller has no log channel to
                // ORT itself, and silent fallback is the contract
                // we documented — keep this quiet.
                return new SessionOptions();
            }
        }

        // Map Auto -> first-choice EP for the running OS. The
        // session-construction path catches append failures and
        // falls back to CPU when FallbackToCpu=true, so a "wrong
        // guess" here just means we pay one EP-append attempt
        // before CPU. The order reflects the typical "best
        // available" pick: each branch picks the EP that's most
        // commonly preinstalled on that OS for ML workloads.
        private static OnnxExecutionProvider ResolveAuto()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return OnnxExecutionProvider.CoreML;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return OnnxExecutionProvider.DirectML;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return OnnxExecutionProvider.Cuda;
            return OnnxExecutionProvider.Cpu;
        }

        private static void AppendEp(
            SessionOptions sessionOptions,
            OnnxExecutionProvider ep,
            OnnxBackendOptions opts)
        {
            switch (ep)
            {
                case OnnxExecutionProvider.CoreML:
                    sessionOptions.AppendExecutionProvider_CoreML(
                        opts.CoreMLFlags);
                    break;
                case OnnxExecutionProvider.Cuda:
                    sessionOptions.AppendExecutionProvider_CUDA(
                        opts.CudaDeviceId);
                    break;
                case OnnxExecutionProvider.DirectML:
                    sessionOptions.AppendExecutionProvider_DML(
                        opts.DirectMLDeviceId);
                    break;
                case OnnxExecutionProvider.Rocm:
                    sessionOptions.AppendExecutionProvider_ROCm(
                        opts.RocmDeviceId);
                    break;
                case OnnxExecutionProvider.Cpu:
                    // No append — ORT runs CPU by default.
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(ep), ep,
                        "Unhandled OnnxExecutionProvider value");
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
