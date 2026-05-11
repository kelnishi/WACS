// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.NN.OnnxRuntime
{
    /// <summary>
    /// ONNX Runtime execution-provider selection knob exposed
    /// through <see cref="OnnxBackendOptions"/>. The values are
    /// the EPs Microsoft.ML.OnnxRuntime ships managed API bindings
    /// for; whether a given EP's native dependencies are present
    /// on the host is a separate question — the
    /// <see cref="OnnxBackend"/> falls back to CPU when the EP
    /// append fails (driver missing, wrong NuGet variant, etc.)
    /// so a misconfigured EP doesn't bring down inference.
    /// </summary>
    public enum OnnxExecutionProvider
    {
        /// <summary>
        /// Pick a platform-appropriate EP at session-construction
        /// time, falling back to CPU if the chosen EP can't load.
        /// Order: <see cref="CoreML"/> on macOS,
        /// <see cref="DirectML"/> on Windows,
        /// <see cref="Cuda"/> then <see cref="Rocm"/> on Linux.
        /// The default for <see cref="OnnxBackendOptions"/>.
        /// </summary>
        Auto = 0,

        /// <summary>CPU EP only — ORT's default, baseline numerics.</summary>
        Cpu = 1,

        /// <summary>
        /// Apple CoreML EP (macOS / iOS). Eligible for CPU + GPU + ANE
        /// via <see cref="OnnxBackendOptions.CoreMLFlags"/>. Bundled in
        /// the stock Microsoft.ML.OnnxRuntime osx-arm64 native dylib —
        /// no NuGet swap required on Apple Silicon.
        /// </summary>
        CoreML = 2,

        /// <summary>
        /// NVIDIA CUDA EP. Requires the host to have a matching CUDA
        /// toolkit + cuDNN installed; on systems without, the EP
        /// append throws and we fall back to CPU. For deterministic
        /// CUDA use, swap the package reference to
        /// Microsoft.ML.OnnxRuntime.Gpu.
        /// </summary>
        Cuda = 3,

        /// <summary>
        /// DirectML EP (Windows). Hardware-vendor-agnostic GPU
        /// acceleration via Direct3D 12. Requires
        /// Microsoft.ML.OnnxRuntime.DirectML on Windows hosts that
        /// don't already have the DML runtime.
        /// </summary>
        DirectML = 4,

        /// <summary>AMD ROCm EP (Linux). Requires ROCm toolkit on host.</summary>
        Rocm = 5,
    }
}
