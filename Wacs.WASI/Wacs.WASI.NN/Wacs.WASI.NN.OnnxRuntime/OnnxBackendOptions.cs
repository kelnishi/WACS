// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Microsoft.ML.OnnxRuntime;

namespace Wacs.WASI.NN.OnnxRuntime
{
    /// <summary>
    /// Typed configuration for <see cref="OnnxBackend"/> — primarily
    /// the execution-provider selection. Sits next to the existing
    /// <see cref="Func{SessionOptions}"/> ctor: the factory is the
    /// full escape hatch (configure anything ORT exposes); this
    /// type is the ergonomic knob for the common case (pick an EP).
    ///
    /// <para>The default-constructed instance picks
    /// <see cref="OnnxExecutionProvider.Cpu"/> — hardware
    /// acceleration is explicit opt-in via either the
    /// <see cref="ExecutionProvider"/> property or the
    /// <c>WACS_WASINN_ONNX_EP</c> environment variable. CPU
    /// default matches the previous wasi-nn ONNX behavior and
    /// avoids the silent op-coverage issues seen with CoreML /
    /// DirectML EPs against generative-LLM ops (the Gemma-3
    /// SLM workflow specifically). To opt in:
    /// <c>WACS_WASINN_ONNX_EP=auto</c> (platform-best),
    /// <c>=coreml</c>, <c>=cuda</c>, <c>=dml</c>, or <c>=rocm</c>;
    /// or construct <see cref="OnnxBackend"/> with
    /// <c>new OnnxBackendOptions { ExecutionProvider = OnnxExecutionProvider.CoreML }</c>.</para>
    /// </summary>
    public sealed class OnnxBackendOptions
    {
        /// <summary>
        /// Which ORT execution provider to append before constructing
        /// the inference session. Default is <see cref="OnnxExecutionProvider.Cpu"/>
        /// — hardware acceleration is opt-in via this property or via
        /// the <c>WACS_WASINN_ONNX_EP</c> environment variable. The
        /// rationale is that ORT's EP partition-and-fallback for
        /// generative-LLM ops (e.g., GroupQueryAttention) is uneven
        /// across CoreML / DirectML, and a CoreML-by-default that
        /// silently breaks Gemma-3-class inference is worse than a
        /// CPU-by-default that requires an opt-in for acceleration.
        /// Set this property (or
        /// <c>WACS_WASINN_ONNX_EP=auto|coreml|cuda|dml|rocm</c>) when
        /// you've verified your model works with the chosen EP.
        /// <see cref="OnnxExecutionProvider.Auto"/> resolves at
        /// session-construction time based on the running OS
        /// (CoreML on macOS, DirectML on Windows, CUDA / ROCm on Linux).
        /// </summary>
        public OnnxExecutionProvider ExecutionProvider { get; set; }
            = OnnxExecutionProvider.Cpu;

        /// <summary>
        /// Device index for the CUDA EP. Ignored for non-CUDA
        /// providers. Defaults to 0 (the first visible GPU).
        /// </summary>
        public int CudaDeviceId { get; set; } = 0;

        /// <summary>
        /// Device index for the ROCm EP. Ignored for non-ROCm
        /// providers. Defaults to 0.
        /// </summary>
        public int RocmDeviceId { get; set; } = 0;

        /// <summary>
        /// Device index for the DirectML EP. Ignored for non-DML
        /// providers. Defaults to 0.
        /// </summary>
        public int DirectMLDeviceId { get; set; } = 0;

        /// <summary>
        /// Flags passed to <c>AppendExecutionProvider_CoreML</c>.
        /// Defaults to <see cref="CoreMLFlags.COREML_FLAG_USE_CPU_AND_GPU"/>
        /// which lets the EP route ops to CPU+GPU (ANE excluded by default
        /// for numerical-stability reasons — set
        /// <see cref="CoreMLFlags.COREML_FLAG_ONLY_ENABLE_DEVICE_WITH_ANE"/>
        /// explicitly if your model is ANE-friendly).
        /// </summary>
        public CoreMLFlags CoreMLFlags { get; set; }
            = CoreMLFlags.COREML_FLAG_USE_CPU_AND_GPU;

        /// <summary>
        /// When the chosen EP fails to append (driver missing, wrong
        /// NuGet variant, unsupported platform), build the session
        /// with default CPU options instead of propagating the
        /// failure. Defaults to true — out-of-box behavior favors
        /// "inference still works" over "EP misconfiguration is
        /// loud". Set false for strict mode where any EP failure
        /// surfaces as <see cref="ErrorCode.RuntimeError"/> at
        /// guest <c>graph.load</c> time.
        /// </summary>
        public bool FallbackToCpu { get; set; } = true;

        /// <summary>
        /// Construct an options instance from environment variables.
        /// Reads <c>WACS_WASINN_ONNX_EP</c> for the EP name
        /// (case-insensitive: <c>auto</c>, <c>cpu</c>, <c>coreml</c>,
        /// <c>cuda</c>, <c>dml</c>/<c>directml</c>, <c>rocm</c>), plus
        /// <c>WACS_WASINN_ONNX_CUDA_DEVICE</c> / <c>_ROCM_DEVICE</c> /
        /// <c>_DML_DEVICE</c> for device indices. Unrecognized values
        /// fall through to <see cref="OnnxExecutionProvider.Auto"/>.
        /// </summary>
        public static OnnxBackendOptions FromEnvironment()
        {
            var opts = new OnnxBackendOptions();
            var epStr = Environment.GetEnvironmentVariable("WACS_WASINN_ONNX_EP");
            if (!string.IsNullOrWhiteSpace(epStr))
                opts.ExecutionProvider = ParseEp(epStr);

            if (int.TryParse(
                    Environment.GetEnvironmentVariable("WACS_WASINN_ONNX_CUDA_DEVICE"),
                    out var cuda))
                opts.CudaDeviceId = cuda;
            if (int.TryParse(
                    Environment.GetEnvironmentVariable("WACS_WASINN_ONNX_ROCM_DEVICE"),
                    out var rocm))
                opts.RocmDeviceId = rocm;
            if (int.TryParse(
                    Environment.GetEnvironmentVariable("WACS_WASINN_ONNX_DML_DEVICE"),
                    out var dml))
                opts.DirectMLDeviceId = dml;
            return opts;
        }

        private static OnnxExecutionProvider ParseEp(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "auto": return OnnxExecutionProvider.Auto;
                case "cpu": return OnnxExecutionProvider.Cpu;
                case "coreml": return OnnxExecutionProvider.CoreML;
                case "cuda": return OnnxExecutionProvider.Cuda;
                case "dml":
                case "directml": return OnnxExecutionProvider.DirectML;
                case "rocm": return OnnxExecutionProvider.Rocm;
                default: return OnnxExecutionProvider.Auto;
            }
        }
    }
}
