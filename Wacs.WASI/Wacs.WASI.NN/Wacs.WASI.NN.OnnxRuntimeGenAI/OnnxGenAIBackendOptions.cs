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
            return opts;
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
