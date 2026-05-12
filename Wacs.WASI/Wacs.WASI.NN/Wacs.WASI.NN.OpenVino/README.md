# WACS.WASI.NN.OpenVino

OpenVINO backend for [`WACS.WASI.NN`](https://www.nuget.org/packages/WACS.WASI.NN).
Implements `IBackend` for `graph-encoding.openvino` via
[`OpenVINO.CSharp.API`](https://www.nuget.org/packages/OpenVINO.CSharp.API) — Intel's
C# wrapper around `libopenvino`.

OpenVINO is the one wasi-nn encoding the spec explicitly designed the multi-builder
`graph.load` shape around: guests pass `[IR-XML, IR-bin]` to mirror OpenVINO's two-file
distribution format (Model + Weights). This backend reads those two builders directly into
`Core.read_model(xmlBytes, weightsTensor)` with no intermediate file I/O.

## Install

```bash
dotnet add package WACS.WASI.NN.OpenVino
```

The package depends on `OpenVINO.CSharp.API` but **not** on any
`OpenVINO.runtime.<rid>` native pack — pick the runtime matching your deployment
target separately. The CLI bundle wires up matching native packs across the
common RIDs already.

## CLI

```sh
wacs run my.component.wasm --wasip2 --bind WACS.WASI.NN.OpenVino.dll
```

Drop `Wacs.WASI.NN.OpenVino.dll` on the load path and pass it via `--bind`. The
CLI's `IBindable`-discovery pass auto-picks `WasiNNOpenVinoBindable` and the
guest's `graph.load` calls with `encoding=openvino` route through this backend.

## Embedder

Interpreter / one-line:

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.OpenVino;
using Wacs.WASI.NN.Types;

var runtime = new WasmRuntime();
runtime.UseWasiNN(b => b.AddBackend(GraphEncoding.OpenVINO, new OpenVinoBackend()));
```

Transpiler-direct-link / DI:

```csharp
services
    .AddWasiPreview2()
    .AddWasiNN(b => b.AddBackend(GraphEncoding.OpenVINO, new OpenVinoBackend()))
    .AddWasiPreview2NNBundle();
```

## What it provides

- **`OpenVinoBackend : IBackend`** — implements `LoadGraph(builders, target)` for the
  two-builder OpenVINO IR shape (`builders[0]` = XML, `builders[1]` = weights bin).
  Single-builder calls are tolerated for IRs with constants inlined.
- **`OpenVinoBackendOptions` / `OpenVinoDevice`** — typed config for device pinning
  (CPU / GPU / NPU / AUTO) plus an arbitrary OpenVINO-property bag forwarded into
  `compile_model`.
- **`WasiNNOpenVinoBindable : IBindable`** — parameterless adapter for `--bind`.
- `[assembly: WasiHostPackage]` — picked up by `runtime.AutoDiscoverHostPackages()`.

## Device selection

Default is **AUTO** — OpenVINO's AUTO plugin picks across whatever is installed.
The wasi-nn guest's `ExecutionTarget` request (CPU / GPU / TPU) maps to OpenVINO
device strings only when the typed options are at their default — a host that
pinned a device through `OpenVinoBackendOptions.Device` keeps that pin regardless
of the guest.

| Guest target | OpenVINO device string |
|---|---|
| `ExecutionTarget.CPU` | `CPU` |
| `ExecutionTarget.GPU` | `GPU` |
| `ExecutionTarget.TPU` | `NPU` (Intel Neural Processing Unit) |
| (default / unset) | `AUTO` |

Enable via environment:

```sh
WACS_WASINN_OPENVINO_DEVICE=auto wacs run my.wasm --wasip2 --bind WACS.WASI.NN.OpenVino.dll
WACS_WASINN_OPENVINO_DEVICE=cpu  wacs run my.wasm ...
WACS_WASINN_OPENVINO_DEVICE=gpu  wacs run my.wasm ...
WACS_WASINN_OPENVINO_DEVICE=npu  wacs run my.wasm ...

# Forward arbitrary compile_model properties:
WACS_WASINN_OPENVINO_PROPERTIES=PERFORMANCE_HINT=LATENCY,INFERENCE_PRECISION_HINT=f16 \
    wacs run my.wasm ...

# Strict mode — propagate compile failures instead of falling back to CPU:
WACS_WASINN_OPENVINO_FALLBACK_CPU=false wacs run my.wasm ...
```

Or via typed config:

```csharp
var backend = new OpenVinoBackend(new OpenVinoBackendOptions
{
    Device = OpenVinoDevice.Gpu,
    FallbackToCpu = true,
    Properties =
    {
        ["PERFORMANCE_HINT"] = "LATENCY",
        ["INFERENCE_PRECISION_HINT"] = "f16",
    },
});
```

Default `FallbackToCpu = true` means a `compile_model` failure on the chosen
device (plugin missing, driver issue) silently retries on `CPU` — the CPU plugin
ships in every OpenVINO runtime distribution. Set false to surface compile
failures as `ErrorCode.RuntimeError` at `graph.load` time.

## Tensor element types

OpenVINO covers every primitive type the wasi-nn WIT enum supports:

| WIT `tensor.tensor-type` | `OpenVinoSharp.ElementType` |
|---|---|
| `fp16` | `F16` |
| `fp32` | `F32` |
| `fp64` | `F64` |
| `bf16` | `BF16` |
| `u8`   | `U8`  |
| `i32`  | `I32` |
| `i64`  | `I64` |

(OpenVINO's enum has many more — `F8E4M3`, `NF4`, `I4`, etc. — but they aren't
exposed by the wasi-nn spec, so guest inputs/outputs using them throw
`InvalidArgument` / `UnsupportedOperation`.)

## Backend choice

| Use case | Package |
|---|---|
| OpenVINO IR (`.xml` + `.bin`) inference | **WACS.WASI.NN.OpenVino** (this) |
| Standard ONNX inference (image classification, encoder-only LLMs) | [`WACS.WASI.NN.OnnxRuntime`](https://www.nuget.org/packages/WACS.WASI.NN.OnnxRuntime) |
| ONNX with ML.NET pipeline integration | [`WACS.WASI.NN.MLNet`](https://www.nuget.org/packages/WACS.WASI.NN.MLNet) |
| GGUF / llama.cpp generative LLMs | [`WACS.WASI.NN.LlamaSharp`](https://www.nuget.org/packages/WACS.WASI.NN.LlamaSharp) |

## Documentation

- **[`docs/WASI_NN_USAGE.md`](https://github.com/kelnishi/WACS/blob/main/docs/WASI_NN_USAGE.md)** —
  unified usage guide (CLI flags, env vars, programmatic embedding, worked examples)
- [`Wacs.WASI/Wacs.WASI.NN/README.md`](https://github.com/kelnishi/WACS/blob/main/Wacs.WASI/Wacs.WASI.NN/README.md)
  — backend matrix + package layout

## License

Apache-2.0
