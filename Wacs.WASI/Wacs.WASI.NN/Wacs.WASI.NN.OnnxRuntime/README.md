# WACS.WASI.NN.OnnxRuntime

ONNX Runtime backend for [`WACS.WASI.NN`](https://www.nuget.org/packages/WACS.WASI.NN).
Implements `IBackend` for `graph-encoding.onnx` directly against
[`Microsoft.ML.OnnxRuntime`](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime) — no
ML.NET wrapper, just ORT.

This is the default wasi-nn backend for the WACS CLI. `wacs run --wasip2 --wasi-nn`
auto-loads it; embedders who don't want the ~50 MB of ORT native binaries can use one of
the other backends instead.

## Install

```bash
dotnet add package WACS.WASI.NN.OnnxRuntime
```

## CLI

```sh
# Bundled with WACS.Cli — works out of the box.
wacs run my.component.wasm --wasip2 --wasi-nn -d ./models::/models
```

The Gemma 3 270M ONNX SLM is the canonical end-to-end test target:
[`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md#end-to-end-example)
walks through it.

## Embedder

Interpreter / one-line:

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.OnnxRuntime;
using Wacs.WASI.NN.Types;

var runtime = new WasmRuntime();
runtime.UseWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()));
```

Transpiler-direct-link / DI:

```csharp
services
    .AddWasiPreview2()
    .AddWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()))
    .AddWasiPreview2NNBundle();
```

(`WasiPreview2RuntimeScope` auto-wires `OnnxBackend` when this assembly is on the load
path — no explicit `AddBackend` needed.)

## What it provides

- **`OnnxBackend : IBackend`** — implements `LoadGraph(builders, target)` for byte-loaded
  ONNX models. Suitable for the SLM / inference workflow where the guest reads model
  bytes and passes them through `wasi:nn/graph.load`
- **`WasiNNOnnxBindable : IBindable`** — parameterless adapter for `--bind`. Auto-pulled
  by the CLI's `--wasi-nn` shorthand
- `[assembly: WasiHostPackage]` — picked up by `runtime.AutoDiscoverHostPackages()`

## Backend choice

| Use case | Package |
|---|---|
| Standard ONNX inference (image classification, embeddings, encoder-only LLMs) | **WACS.WASI.NN.OnnxRuntime** (this) |
| ONNX with ML.NET pipeline integration (preprocessing transformers, custom predictors) | [`WACS.WASI.NN.MLNet`](https://www.nuget.org/packages/WACS.WASI.NN.MLNet) |
| GGUF / llama.cpp generative LLMs (`load-by-name` flow) | [`WACS.WASI.NN.LlamaSharp`](https://www.nuget.org/packages/WACS.WASI.NN.LlamaSharp) |

## Documentation

- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
- [`Wacs.WASI/Wacs.WASI.NN/README.md`](https://github.com/kelnishi/WACS/blob/main/Wacs.WASI/Wacs.WASI.NN/README.md)
  — backend matrix + quick-start

## License

Apache-2.0
