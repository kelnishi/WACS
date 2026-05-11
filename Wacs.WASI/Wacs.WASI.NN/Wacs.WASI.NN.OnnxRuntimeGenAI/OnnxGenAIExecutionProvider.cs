// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.NN.OnnxRuntimeGenAI
{
    /// <summary>
    /// OnnxRuntime-GenAI execution-provider selection knob exposed
    /// through <see cref="OnnxGenAIBackendOptions"/>. Parallels
    /// <c>Wacs.WASI.NN.OnnxRuntime.OnnxExecutionProvider</c>; the
    /// enum-shape symmetry lets embedders reach for the same knob
    /// in both backends.
    ///
    /// <para>Empirical note on auto-promotion: on osx-arm64 against
    /// gemma-3-270m-it-genai, CoreML produces correct output but is
    /// 3-5× slower than CPU because kernel compilation + Metal
    /// command-buffer setup dominates the actual compute for a
    /// 270M-param model. CoreML's win typically kicks in at 1B+
    /// params. <see cref="Cpu"/> is the default for that reason —
    /// not because CoreML is broken (it isn't, for GenAI's
    /// purpose-built layer), but because auto-promotion would
    /// regress small-model perf for the common case.</para>
    /// </summary>
    public enum OnnxGenAIExecutionProvider
    {
        /// <summary>
        /// Pick a platform-appropriate EP at session-construction
        /// time, falling back to CPU if the chosen EP can't load.
        /// Order: <see cref="CoreML"/> on macOS,
        /// <see cref="DirectML"/> on Windows, <see cref="Cuda"/>
        /// then <see cref="Rocm"/> on Linux.
        /// </summary>
        Auto = 0,

        /// <summary>
        /// CPU only — strips the model-declared provider list via
        /// <c>Config.ClearProviders</c> and falls through to GenAI's
        /// platform default kernels. Default for
        /// <see cref="OnnxGenAIBackendOptions"/>; matches the
        /// <c>OnnxBackend</c>'s opt-in posture for hardware
        /// acceleration.
        /// </summary>
        Cpu = 1,

        /// <summary>
        /// Apple CoreML EP (macOS / iOS). Eligible for CPU + GPU
        /// + ANE via the underlying CoreML framework. Bundled in
        /// the stock <c>Microsoft.ML.OnnxRuntimeGenAI</c> osx-arm64
        /// native dylib — no NuGet swap required on Apple Silicon.
        /// Verified correct against gemma-3-270m-it-genai; perf
        /// trails CPU on small models, expected to win on 1B+
        /// param models.
        /// </summary>
        CoreML = 2,

        /// <summary>
        /// NVIDIA CUDA EP. Requires the host to have CUDA toolkit
        /// installed. For deterministic CUDA use, swap the package
        /// reference to <c>Microsoft.ML.OnnxRuntimeGenAI.Cuda</c>.
        /// </summary>
        Cuda = 3,

        /// <summary>
        /// DirectML EP (Windows). Use the
        /// <c>Microsoft.ML.OnnxRuntimeGenAI.DirectML</c> NuGet
        /// variant for full coverage; the base NuGet ships
        /// DirectML symbols on win-arm64 / win-x64 too.
        /// </summary>
        DirectML = 4,

        /// <summary>AMD ROCm EP (Linux). Requires ROCm toolkit on host.</summary>
        Rocm = 5,
    }
}
