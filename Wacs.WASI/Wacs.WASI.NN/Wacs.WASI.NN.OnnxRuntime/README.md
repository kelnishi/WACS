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
- **`OnnxBackendOptions` / `OnnxExecutionProvider`** — typed config for execution-provider
  selection (CoreML / CUDA / DirectML / ROCm, with auto-detect + CPU fallback)
- **`WasiNNOnnxBindable : IBindable`** — parameterless adapter for `--bind`. Auto-pulled
  by the CLI's `--wasi-nn` shorthand
- `[assembly: WasiHostPackage]` — picked up by `runtime.AutoDiscoverHostPackages()`

## Hardware acceleration

Default is **CPU** — hardware acceleration is **opt-in** via the
`WACS_WASINN_ONNX_EP` env var or `OnnxBackendOptions.ExecutionProvider`. CPU
default avoids the silent op-coverage issues seen with the CoreML / DirectML
EPs against generative-LLM ops (e.g., GroupQueryAttention in Gemma 3): partition-
and-fallback inside ORT can produce numerically wrong results without raising
an error, which manifests as "the LLM doesn't respond" against the Gemma 3 270M
SLM workflow. Pin the EP per-model after you've verified your model works with it.

| OS                | `WACS_WASINN_ONNX_EP=auto` resolves to | Notes                                                              |
|-------------------|----------------------------------------|--------------------------------------------------------------------|
| macOS (arm64/x64) | CoreML (CPU + GPU)                    | EP symbol ships in stock `Microsoft.ML.OnnxRuntime` — no NuGet swap |
| Windows           | DirectML                              | Add `Microsoft.ML.OnnxRuntime.DirectML` for full DML coverage      |
| Linux             | CUDA then ROCm                        | Requires CUDA toolkit / ROCm runtime on host                       |
| Other             | CPU                                   |                                                                    |

EP-append failure silently falls back to CPU regardless — out-of-box behavior
favors "inference still works" over "EP misconfiguration is loud".

Enable via environment:

```sh
# Platform-best pick (CoreML on macOS, DirectML on Windows, CUDA on Linux)
WACS_WASINN_ONNX_EP=auto wacs run my.wasm --wasip2 --wasi-nn

# Force a specific provider
WACS_WASINN_ONNX_EP=coreml wacs run my.wasm --wasip2 --wasi-nn
WACS_WASINN_ONNX_EP=cuda   WACS_WASINN_ONNX_CUDA_DEVICE=1 wacs run my.wasm --wasip2 --wasi-nn
WACS_WASINN_ONNX_EP=dml    wacs run my.wasm --wasip2 --wasi-nn
WACS_WASINN_ONNX_EP=rocm   wacs run my.wasm --wasip2 --wasi-nn

# Explicitly stay on CPU (default — no env var also gets you CPU)
WACS_WASINN_ONNX_EP=cpu wacs run my.wasm --wasip2 --wasi-nn
```

Override via typed config (library embedders):

```csharp
using Wacs.WASI.NN.OnnxRuntime;
using Microsoft.ML.OnnxRuntime;

var backend = new OnnxBackend(new OnnxBackendOptions
{
    ExecutionProvider = OnnxExecutionProvider.CoreML,
    CoreMLFlags = CoreMLFlags.COREML_FLAG_USE_CPU_AND_GPU
                | CoreMLFlags.COREML_FLAG_ONLY_ENABLE_DEVICE_WITH_ANE,
    FallbackToCpu = true,
});
```

Full escape hatch (custom `SessionOptions` factory):

```csharp
var backend = new OnnxBackend(() =>
{
    var opts = new SessionOptions();
    opts.AppendExecutionProvider_CUDA(deviceId: 0);
    opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
    return opts;  // factory wins over OnnxBackendOptions
});
```

`OnnxBackendOptions.FallbackToCpu = false` propagates EP-append failures as
`ErrorCode.RuntimeError` at `graph.load` time — useful for environments where silent CPU
fallback would mask a misconfiguration.

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
