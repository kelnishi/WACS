# WACS.WASI.NN.MLNet

ML.NET-flavored ONNX backend for [`WACS.WASI.NN`](https://www.nuget.org/packages/WACS.WASI.NN).
Implements `IBackend` for `graph-encoding.onnx` against
[`Microsoft.ML.OnnxTransformer`](https://www.nuget.org/packages/Microsoft.ML.OnnxTransformer)
under an `MLContext` lifecycle — for embedders composing wasi-nn inference with the rest
of an ML.NET pipeline (preprocessing transformers, custom predictors, IDataView /
PredictionEngine).

For raw tensor inference with no pipeline integration, prefer
[`WACS.WASI.NN.OnnxRuntime`](https://www.nuget.org/packages/WACS.WASI.NN.OnnxRuntime) —
it avoids the Microsoft.ML transitive surface (~70 MB lighter).

## Install

```bash
dotnet add package WACS.WASI.NN.MLNet
```

The package's bin ships its NuGet transitives + RID-specific native libs (via
`<EnableDynamicLoading>true</EnableDynamicLoading>`), so `Assembly.LoadFrom` resolves
everything from the LoadFromContext probe.

## CLI

```sh
# After dotnet build of this project's repo:
MLNET=$(realpath Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.MLNet/bin/Release/net8.0/Wacs.WASI.NN.MLNet.dll)

wacs run my.component.wasm --wasip2 --bind "$MLNET" -d ./models::/models
```

`--bind` auto-pulls the WASI.NN typed surface + DI sibling onto host-packages when the
identity starts with `Wacs.WASI.NN.`. The Preview 2 DI scope wires the ML.NET-backed ORT
into the DI bundle's `WasiNNConfiguration.Backends[ONNX]`.

## Embedder

Interpreter / one-line:

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.MLNet;
using Wacs.WASI.NN.Types;

var runtime = new WasmRuntime();
runtime.UseWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new MLNetBackend()));
```

## What it provides

- **`MLNetBackend : IBackend`** — wraps ORT under `MLContext.Transforms.ApplyOnnxModel`
  / `IDataView`, exposing the same `LoadGraph(builders, target)` /
  `Compute(inputs)` shape as the bare ORT backend. Embedders who want the
  ML.NET preprocessing surface get it; the rest of WACS doesn't notice
- **`WasiNNMLNetBindable : IBindable`** — parameterless adapter for `--bind`
- `[assembly: WasiHostPackage]`

## Why ML.NET over bare ORT

Use this backend when:

- Your wasm guest is one stage in a longer ML.NET pipeline (custom transformers,
  preprocessing, `IDataView` consumers) and you want them composed in one host-side
  process
- You're already bringing in `Microsoft.ML` for adjacent work — the marginal cost of
  routing wasi-nn through `MLContext` is small
- You want `MLContext.Log` / structured ML.NET diagnostics around the inference call

For everything else — image classification, embeddings, encoder-only LLMs, raw tensor
in / raw tensor out — bare
[`WACS.WASI.NN.OnnxRuntime`](https://www.nuget.org/packages/WACS.WASI.NN.OnnxRuntime) is
lighter and simpler.

## Backend choice

| Use case | Package |
|---|---|
| ONNX with ML.NET pipeline integration | **WACS.WASI.NN.MLNet** (this) |
| Standard ONNX inference (lighter footprint) | [`WACS.WASI.NN.OnnxRuntime`](https://www.nuget.org/packages/WACS.WASI.NN.OnnxRuntime) |
| GGUF / llama.cpp generative LLMs | [`WACS.WASI.NN.LlamaSharp`](https://www.nuget.org/packages/WACS.WASI.NN.LlamaSharp) |

## Documentation

- **[`docs/WASI_NN_USAGE.md`](https://github.com/kelnishi/WACS/blob/main/docs/WASI_NN_USAGE.md)** —
  unified usage guide (CLI flags, env vars, programmatic embedding, worked examples)
- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
- [`Wacs.WASI/Wacs.WASI.NN/README.md`](https://github.com/kelnishi/WACS/blob/main/Wacs.WASI/Wacs.WASI.NN/README.md)
  — backend matrix + package layout

## License

Apache-2.0
