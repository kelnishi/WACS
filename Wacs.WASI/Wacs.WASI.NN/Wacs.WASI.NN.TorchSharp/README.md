# WACS.WASI.NN.TorchSharp

TorchSharp / libtorch backend for [`WACS.WASI.NN`](https://www.nuget.org/packages/WACS.WASI.NN).
Implements `IBackend` for `graph-encoding.pytorch` against
[`TorchSharp`](https://www.nuget.org/packages/TorchSharp); GPU runtimes pluggable via
TorchSharp's backend NuGets (CPU default; CUDA / Metal / ROCm swaps with no source change).

Loads TorchScript modules (`torch.jit.save` output, typically `.pt` or `.ts`) and runs
inference through libtorch's C++ runtime — same shape as Python `torch.jit.load(model).forward(*inputs)`.

## Install

```bash
dotnet add package WACS.WASI.NN.TorchSharp
```

The package's bin ships `TorchSharp.dll` + `libtorch-cpu`'s RID-specific native libs
(via `<EnableDynamicLoading>true</EnableDynamicLoading>`), so `Assembly.LoadFrom` resolves
everything from the LoadFromContext probe — no manual deps staging.

## Two dispatch paths

| Path | Use when |
|---|---|
| `graph.load(builders, PyTorch, target)` — byte-loaded | Small / medium models (~ < 500 MB) where the canonical-ABI lift cost is acceptable |
| `graph.load-by-name(name)` — file-path registry | Big models. Embedder configures a name → path map; libtorch opens the file directly with no host-side copy |

## CLI quick start (load-by-name flow)

TorchSharp isn't bundled with `WACS.Cli` (libtorch's natives are too chunky to ride along).
Pass the explicit path to the backend's bin:

```sh
mkdir -p ./models
# Drop a .pt file in there, e.g. resnet50.pt:
# (any TorchScript module saved via torch.jit.save in Python)

export WACS_WASINN_TORCH_DIR="$(pwd)/models"

# After dotnet build of this project's repo:
TORCH=$(realpath Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.TorchSharp/bin/Release/net8.0/Wacs.WASI.NN.TorchSharp.dll)

wacs run my-pytorch.component.wasm --wasip2 --bind "$TORCH"
```

`--bind` auto-pulls the WASI.NN typed surface + DI sibling onto host-packages when the
identity starts with `Wacs.WASI.NN.`. The Preview 2 DI scope's auto-wire registers the
backend in BOTH `Backends[PyTorch]` AND `LoadByNameBackend`; guests calling
`wasi:nn/graph.load-by-name("resnet50")` direct-link cleanly to a model at
`$WACS_WASINN_TORCH_DIR/resnet50.pt`.

The full chain (with under-the-hood walkthrough naming each fix) lives at
[`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md#gguf-inference-example-llamasharp-backend).
The TorchSharp flow is identical modulo encoding (`PyTorch` instead of `GGML`) and file
extension (`.pt` / `.ts` instead of `.gguf`).

## Embedder

Interpreter / one-line:

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.TorchSharp;
using Wacs.WASI.NN.Types;

var registry = new Dictionary<string, string>
{
    ["resnet50"] = "/path/to/resnet50.pt",
    ["mobilenet"] = "/path/to/mobilenet_v3.pt",
};
var backend = TorchSharpBackend.FromPaths(registry);

var runtime = new WasmRuntime();
runtime.UseWasiNN(b =>
{
    b.AddBackend(GraphEncoding.PyTorch, backend);
    b.Configuration.LoadByNameBackend = backend;
});
```

For the transpiler-direct-link / DI flow, just `--bind <path>` — the Preview 2 DI scope
auto-discovers and wires.

## GPU backend swap

Replace `TorchSharp-cpu` in this project's csproj with one of:

- `TorchSharp-cuda-12.1` — NVIDIA CUDA
- `TorchSharp-cuda-11.8` — older CUDA
- `TorchSharp-rocm-5.2` — AMD ROCm
- `TorchSharp-macos-x64` / `TorchSharp-macos-arm64` — Apple Metal / MPS

Then rebuild. The `EnableDynamicLoading` bin layout copies whichever backend NuGet's
natives are pulled — no source change.

## Input / output naming convention

TorchScript modules consume positional `forward(t1, t2, …)` args and return either a single
tensor or a tuple. wasi-nn's WIT contract is name-keyed (`list<tuple<string, tensor>>`), so
the binding follows the WasmEdge convention:

- Input names are `"0"`, `"1"`, …  (positional index as decimal string)
- Output names are emitted under the same indexed scheme

A guest calling a 2-input / 1-output module:

```rust
let outputs = ctx.compute(vec![
    ("0".to_string(), input_ids),
    ("1".to_string(), attention_mask),
])?;
let logits = &outputs[0].1;   // outputs[0].0 == "0"
```

Non-numeric input names trip `InvalidArgument`. Sparse / non-contiguous indices (e.g.,
`"0"` + `"2"` skipping `"1"`) trip the same — TorchScript dispatch is positional, so
indices must be `0..n-1`.

## Supported tensor types

| WIT `tensor-type` | libtorch `ScalarType` |
|---|---|
| `FP16` | `Float16` |
| `FP32` | `Float32` |
| `FP64` | `Float64` |
| `BF16` | `BFloat16` |
| `U8` | `Byte` |
| `I32` | `Int32` |
| `I64` | `Int64` |

Other torch dtypes that show up in model outputs (e.g., `Bool`, `QInt8` quantized) trip
`RuntimeError` — convert the model's outputs to one of the supported dtypes via a final
`.to(torch.float32)` before saving the TorchScript module.

## What it provides

- **`TorchSharpBackend : IBackend`** — implements `LoadGraph(builders, target)` (byte-loaded
  TorchScript) AND `LoadGraphByName(name, target)` (file-path registry). Both paths
  produce a graph that wraps a `torch.jit.ScriptModule` in `eval()` mode.
- **`TorchSharpBackend.FromPaths(IDictionary<string,string>)`** — convenience static
  factory for the simple "drop TorchScript files in a directory" embedder flow.
- **`WasiNNTorchSharpBindable : IBindable`** — parameterless adapter for `--bind`. Reads
  `WACS_WASINN_TORCH_DIR`, scans `*.pt` + `*.ts`, registers each under its filename-sans-
  extension. Wires the backend into BOTH `Backends[PyTorch]` AND `LoadByNameBackend`.
- `[assembly: WasiHostPackage]` — picked up by `runtime.AutoDiscoverHostPackages()`.

## Backend choice

| Use case | Package |
|---|---|
| TorchScript / PyTorch model inference | **WACS.WASI.NN.TorchSharp** (this) |
| Standard ONNX inference | [`WACS.WASI.NN.OnnxRuntime`](https://www.nuget.org/packages/WACS.WASI.NN.OnnxRuntime) |
| ONNX with ML.NET pipeline integration | [`WACS.WASI.NN.MLNet`](https://www.nuget.org/packages/WACS.WASI.NN.MLNet) |
| GGUF / llama.cpp generative LLMs | [`WACS.WASI.NN.LlamaSharp`](https://www.nuget.org/packages/WACS.WASI.NN.LlamaSharp) |

## Documentation

- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
  — runtime requirements + chaining model
- [`Wacs.WASI/Wacs.WASI.NN/README.md`](https://github.com/kelnishi/WACS/blob/main/Wacs.WASI/Wacs.WASI.NN/README.md)
  — backend matrix

## License

Apache-2.0
