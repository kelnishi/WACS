// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.NN.OnnxRuntimeGenAI
{
    /// <summary>
    /// Typed configuration for <see cref="OnnxGenAIBackend"/> —
    /// generation defaults that map to OnnxRuntime-GenAI's
    /// <c>GeneratorParams.SetSearchOption</c> calls. Reads from
    /// environment variables (<c>WACS_WASINN_GENAI_*</c>) for
    /// the CLI <c>--bind</c> path; library embedders construct
    /// directly.
    /// </summary>
    public sealed class OnnxGenAIBackendOptions
    {
        /// <summary>
        /// Maximum generation length, in tokens (prompt + response).
        /// Maps to GenAI's <c>max_length</c> search option. Default
        /// 512; override with <c>WACS_WASINN_GENAI_MAX_LENGTH</c>.
        /// </summary>
        public int MaxLength { get; set; } = 512;

        /// <summary>
        /// Sampling temperature. Maps to GenAI's <c>temperature</c>
        /// search option. Default 1.0 (no scaling). Set
        /// <see cref="DoSample"/> true to actually use temperature
        /// — GenAI ignores it under greedy decoding. Override with
        /// <c>WACS_WASINN_GENAI_TEMPERATURE</c>.
        /// </summary>
        public double Temperature { get; set; } = 1.0;

        /// <summary>
        /// Top-p nucleus sampling. Maps to GenAI's <c>top_p</c>
        /// search option. Default 1.0 (no truncation). Active only
        /// when <see cref="DoSample"/> is true. Override with
        /// <c>WACS_WASINN_GENAI_TOP_P</c>.
        /// </summary>
        public double TopP { get; set; } = 1.0;

        /// <summary>
        /// Top-k truncation. Maps to GenAI's <c>top_k</c> search
        /// option. Default 50. Active only when
        /// <see cref="DoSample"/> is true. Override with
        /// <c>WACS_WASINN_GENAI_TOP_K</c>.
        /// </summary>
        public int TopK { get; set; } = 50;

        /// <summary>
        /// When true, use sampling (temperature / top_p / top_k);
        /// when false, greedy argmax. Default false (greedy —
        /// deterministic for testing). Override with
        /// <c>WACS_WASINN_GENAI_DO_SAMPLE=1</c>.
        /// </summary>
        public bool DoSample { get; set; } = false;

        /// <summary>
        /// When true, the prompt+response sequence is returned;
        /// when false, only the generated portion (without the
        /// prompt). Default false — most chat / completion guests
        /// expect just the model's reply. Override with
        /// <c>WACS_WASINN_GENAI_INCLUDE_PROMPT=1</c>.
        /// </summary>
        public bool IncludePromptInResponse { get; set; } = false;

        /// <summary>
        /// Which GenAI execution provider to append after
        /// <c>Config.ClearProviders</c>. Default
        /// <see cref="OnnxGenAIExecutionProvider.Cpu"/> — hardware
        /// acceleration is opt-in via this property or via the
        /// <c>WACS_WASINN_GENAI_EP</c> environment variable.
        ///
        /// <para>The default is CPU rather than
        /// <see cref="OnnxGenAIExecutionProvider.Auto"/> because
        /// on small models (e.g., gemma-3-270m-it-genai) the
        /// CoreML kernel-compile + Metal-command-buffer overhead
        /// runs 3-5× slower than CPU through GenAI's purpose-built
        /// layer; auto-promotion would regress the common SLM
        /// case. Large-model embedders opt in explicitly.</para>
        /// </summary>
        public OnnxGenAIExecutionProvider ExecutionProvider { get; set; }
            = OnnxGenAIExecutionProvider.Cpu;

        /// <summary>
        /// Device index for the CUDA EP. Ignored for non-CUDA
        /// providers. Default 0.
        /// </summary>
        public int CudaDeviceId { get; set; } = 0;

        /// <summary>
        /// Device index for the DirectML EP. Ignored for non-DML
        /// providers. Default 0.
        /// </summary>
        public int DirectMLDeviceId { get; set; } = 0;

        /// <summary>
        /// Device index for the ROCm EP. Ignored for non-ROCm
        /// providers. Default 0.
        /// </summary>
        public int RocmDeviceId { get; set; } = 0;

        /// <summary>
        /// When the chosen EP fails to append (provider not
        /// compiled into the platform's GenAI dylib, driver
        /// missing, etc.), build the Model with default options
        /// (CPU). Default true. Set false for strict mode where
        /// any EP failure surfaces as
        /// <see cref="ErrorCode.RuntimeError"/> at
        /// <c>graph.load-by-name</c> time.
        /// </summary>
        public bool FallbackToCpu { get; set; } = true;

        /// <summary>
        /// Read options from the standard env-var set.
        /// </summary>
        public static OnnxGenAIBackendOptions FromEnvironment()
        {
            var opts = new OnnxGenAIBackendOptions();
            if (int.TryParse(
                    Environment.GetEnvironmentVariable("WACS_WASINN_GENAI_MAX_LENGTH"),
                    out var ml) && ml > 0)
                opts.MaxLength = ml;
            if (double.TryParse(
                    Environment.GetEnvironmentVariable("WACS_WASINN_GENAI_TEMPERATURE"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var t))
                opts.Temperature = t;
            if (double.TryParse(
                    Environment.GetEnvironmentVariable("WACS_WASINN_GENAI_TOP_P"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var tp))
                opts.TopP = tp;
            if (int.TryParse(
                    Environment.GetEnvironmentVariable("WACS_WASINN_GENAI_TOP_K"),
                    out var tk) && tk > 0)
                opts.TopK = tk;
            if (ParseBool(Environment.GetEnvironmentVariable(
                    "WACS_WASINN_GENAI_DO_SAMPLE")))
                opts.DoSample = true;
            if (ParseBool(Environment.GetEnvironmentVariable(
                    "WACS_WASINN_GENAI_INCLUDE_PROMPT")))
                opts.IncludePromptInResponse = true;

            var epStr = Environment.GetEnvironmentVariable("WACS_WASINN_GENAI_EP");
            if (!string.IsNullOrWhiteSpace(epStr))
                opts.ExecutionProvider = ParseEp(epStr);

            if (int.TryParse(
                    Environment.GetEnvironmentVariable("WACS_WASINN_GENAI_CUDA_DEVICE"),
                    out var cuda))
                opts.CudaDeviceId = cuda;
            if (int.TryParse(
                    Environment.GetEnvironmentVariable("WACS_WASINN_GENAI_ROCM_DEVICE"),
                    out var rocm))
                opts.RocmDeviceId = rocm;
            if (int.TryParse(
                    Environment.GetEnvironmentVariable("WACS_WASINN_GENAI_DML_DEVICE"),
                    out var dml))
                opts.DirectMLDeviceId = dml;
            return opts;
        }

        private static OnnxGenAIExecutionProvider ParseEp(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "auto": return OnnxGenAIExecutionProvider.Auto;
                case "cpu": return OnnxGenAIExecutionProvider.Cpu;
                case "coreml": return OnnxGenAIExecutionProvider.CoreML;
                case "cuda": return OnnxGenAIExecutionProvider.Cuda;
                case "dml":
                case "directml": return OnnxGenAIExecutionProvider.DirectML;
                case "rocm": return OnnxGenAIExecutionProvider.Rocm;
                default: return OnnxGenAIExecutionProvider.Cpu;
            }
        }

        private static bool ParseBool(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }
    }
}
