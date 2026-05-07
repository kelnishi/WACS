# Wacs.WASI.NN

wasi-nn host bindings — both the component-model WIT
(`wasi:nn@0.2.0-rc-2024-10-28`) and the legacy WITX
(`wasi_ephemeral_nn`) ABIs against a single backend SPI. Backend
implementations ship as sibling NuGets so consumers wiring only one
skip the others' native binaries.

See [Wacs.WASI.NN/README.md](Wacs.WASI.NN/README.md) for the
architecture deep-dive.

## Contents

- **[Wacs.WASI.NN/](Wacs.WASI.NN/)** — core: `WasiNNConfiguration`, `WasiNNHost`, `IBackend` SPI, WIT + WITX bindings, `IdentityBackend` for smoke tests.
- **[Wacs.WASI.NN.Test/](Wacs.WASI.NN.Test/)** — SPI shape, error-path, and binding-registration tests against `IdentityBackend`.
- **[Wacs.WASI.NN.OnnxRuntime/](Wacs.WASI.NN.OnnxRuntime/)** — direct `Microsoft.ML.OnnxRuntime` backend for `graph-encoding.onnx`. Lightest of the three — just ORT, no ML.NET wrapper.
- **[Wacs.WASI.NN.OnnxRuntime.Test/](Wacs.WASI.NN.OnnxRuntime.Test/)** — ORT backend SPI + error-path tests.
- **[Wacs.WASI.NN.MLNet/](Wacs.WASI.NN.MLNet/)** — `Microsoft.ML.OnnxTransformer`-flavored backend wrapping ORT under an `MLContext` lifecycle for embedders chaining wasi-nn with broader ML.NET pipelines.
- **[Wacs.WASI.NN.MLNet.Test/](Wacs.WASI.NN.MLNet.Test/)** — ML.NET backend tests.
- **[Wacs.WASI.NN.LlamaSharp/](Wacs.WASI.NN.LlamaSharp/)** — `LLamaSharp` backend for `graph-encoding.ggml` on the WasmEdge GGUF convention (U8 tensors carrying UTF-8 prompt / response).
- **[Wacs.WASI.NN.LlamaSharp.Test/](Wacs.WASI.NN.LlamaSharp.Test/)** — LlamaSharp backend SPI + load-by-name routing tests.
