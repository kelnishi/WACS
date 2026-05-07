// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.NN.Types
{
    /// <summary>
    /// Backend-agnostic graph encoding tag from the WIT
    /// <c>graph.graph-encoding</c> enum
    /// (wasi:nn@0.2.0-rc-2024-10-28). Discriminants match the
    /// WIT order.
    ///
    /// <para>The legacy WITX <c>graph_encoding</c> enum lacks
    /// <see cref="GGML"/> (GGUF/llama.cpp); the WITX binding
    /// rejects encoding=5 with <c>invalid-encoding</c> rather
    /// than silently routing to ggml.</para>
    /// </summary>
    public enum GraphEncoding : byte
    {
        OpenVINO = 0,
        ONNX = 1,
        TensorFlow = 2,
        PyTorch = 3,
        TensorFlowLite = 4,
        GGML = 5,
        Autodetect = 6,
    }

    /// <summary>
    /// Where the graph runs. Mirrors WIT <c>graph.execution-target</c>
    /// (wasi:nn@0.2.0-rc-2024-10-28). The enum is advisory — backends
    /// that don't support a target should throw
    /// <see cref="WasiNNException"/> with
    /// <c>error-code.unsupported-operation</c>.
    /// </summary>
    public enum ExecutionTarget : byte
    {
        CPU = 0,
        GPU = 1,
        TPU = 2,
    }
}
