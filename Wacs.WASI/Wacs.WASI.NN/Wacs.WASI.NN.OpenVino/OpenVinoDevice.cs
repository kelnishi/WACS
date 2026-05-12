// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.NN.OpenVino
{
    /// <summary>
    /// Target device the OpenVINO runtime should compile against,
    /// exposed through <see cref="OpenVinoBackendOptions"/>. Maps
    /// to OpenVINO's device-name strings ("CPU", "GPU", "NPU",
    /// "AUTO") which <c>Core.compile_model</c> accepts directly.
    ///
    /// <para>The wasi-nn <see cref="Types.ExecutionTarget"/> enum
    /// is a coarser shape (CPU / GPU / TPU) — guest requests are
    /// translated to OpenVINO device strings inside
    /// <see cref="OpenVinoBackend.LoadGraph"/>, with TPU mapped to
    /// NPU since that's the closest analog Intel's runtime
    /// exposes.</para>
    /// </summary>
    public enum OpenVinoDevice
    {
        /// <summary>
        /// Let OpenVINO pick the best device at compile time
        /// ("AUTO" plugin). Default for
        /// <see cref="OpenVinoBackendOptions"/> — OpenVINO's
        /// AUTO plugin falls back gracefully across CPU / GPU /
        /// NPU based on what's installed, so the backend "just
        /// works" out of the box without device-pinning.
        /// </summary>
        Auto = 0,

        /// <summary>CPU plugin — always present in the OpenVINO runtime.</summary>
        Cpu = 1,

        /// <summary>
        /// Intel iGPU / dGPU (Compute Library for OpenCL). Requires
        /// the GPU plugin to be present in the runtime distribution
        /// and the host to have a compatible Intel graphics driver
        /// installed. Falls back to CPU when the GPU plugin's
        /// initialization fails.
        /// </summary>
        Gpu = 2,

        /// <summary>
        /// Intel Neural Processing Unit (Meteor Lake / Lunar Lake
        /// / Arrow Lake AI accelerators). Not all platforms ship
        /// the NPU plugin; falls back to CPU when unavailable.
        /// </summary>
        Npu = 3,
    }
}
