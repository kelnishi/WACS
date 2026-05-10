# WACS.WASI.NN.LlamaSharp

GGUF / llama.cpp backend for [`WACS.WASI.NN`](https://www.nuget.org/packages/WACS.WASI.NN).
Implements `IBackend` for `graph-encoding.ggml` against
[`LLamaSharp`](https://www.nuget.org/packages/LLamaSharp); GPU runtimes pluggable via
LlamaSharp's backend NuGets (CPU default; CUDA / Vulkan / MacMetal swaps with no source
change).

Follows the WasmEdge wasi-nn GGUF convention:

- Models resolved by **name** through `wasi:nn/graph.load-by-name(name)` — multi-GB
  GGUFs aren't passed through the canonical-ABI byte-loader path
- Input / output are **U8 tensors carrying UTF-8 prompt / response bytes**. No
  tokenization or chat-template work in the guest — LlamaSharp does it all host-side

## Install

```bash
dotnet add package WACS.WASI.NN.LlamaSharp
```

The package's bin ships its NuGet transitives + RID-specific native libs (via
`<EnableDynamicLoading>true</EnableDynamicLoading>`), so `Assembly.LoadFrom` resolves
everything from the LoadFromContext probe — no manual deps staging.

## CLI quick start

LlamaSharp isn't bundled with `WACS.Cli` by default (unlike OnnxRuntime — the natives
are too chunky to ride along). Pass the explicit path to the backend's bin:

```sh
mkdir -p ./models
huggingface-cli download Qwen/Qwen2.5-0.5B-Instruct-GGUF \
    qwen2.5-0.5b-instruct-q4_k_m.gguf --local-dir ./models

export WACS_WASINN_GGUF_DIR="$(pwd)/models"

# After dotnet build of this project's repo:
LLAMA=$(realpath Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.LlamaSharp/bin/Release/net8.0/Wacs.WASI.NN.LlamaSharp.dll)

wacs run my-llm.component.wasm --wasip2 --bind "$LLAMA"
```

`--bind` auto-pulls the WASI.NN typed surface + DI sibling onto host-packages when the
identity starts with `Wacs.WASI.NN.`. The Preview 2 DI scope's auto-wire registers the
backend in BOTH `Backends[GGML]` AND `LoadByNameBackend`; guests calling
`wasi:nn/graph.load-by-name(...)` direct-link cleanly.

The full chain (with under-the-hood walkthrough) lives at
[`docs/COMPONENT_CHAINING.md#gguf-inference-example-llamasharp-backend`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md#gguf-inference-example-llamasharp-backend).

## Embedder

Interpreter / one-line:

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.LlamaSharp;
using Wacs.WASI.NN.Types;

var registry = new Dictionary<string, string>
{
    ["qwen2.5-0.5b"] = "/path/to/qwen2.5-0.5b-instruct-q4_k_m.gguf",
};
var backend = LlamaSharpBackend.FromPaths(registry);

var runtime = new WasmRuntime();
runtime.UseWasiNN(b =>
{
    b.AddBackend(GraphEncoding.GGML, backend);
    b.Configuration.LoadByNameBackend = backend;   // route load-by-name
});
```

For the transpiler-direct-link / DI flow, just `--bind <path>` — the Preview 2 DI scope
auto-discovers and wires.

## GPU backend swap

Replace `LLamaSharp.Backend.Cpu` in the project's csproj with one of:

- `LLamaSharp.Backend.Cuda12` — NVIDIA
- `LLamaSharp.Backend.Vulkan` — cross-vendor GPU
- `LLamaSharp.Backend.MacMetal` — Apple Silicon

Then rebuild. The `EnableDynamicLoading` bin layout copies whichever backend NuGet's
natives are pulled — no source change.

## What it provides

- **`LlamaSharpBackend : IBackend`** — implements `LoadGraphByName(name, target)` for
  registry-resolved GGUF files. The byte-loader path
  (`LoadGraph(builders, target)`) traps with `UnsupportedOperation` by design — see
  class docs for why
- **`LlamaSharpBackend.FromPaths(IDictionary<string,string>)`** — convenience static
  factory for the simple "drop GGUFs in a directory" embedder flow
- **`WasiNNLlamaSharpBindable : IBindable`** — parameterless adapter for `--bind`. Reads
  `WACS_WASINN_GGUF_DIR`, scans `*.gguf`, registers each under its filename-sans-extension

## Backend choice

| Use case | Package |
|---|---|
| GGUF / llama.cpp generative LLMs (`load-by-name` flow) | **WACS.WASI.NN.LlamaSharp** (this) |
| Standard ONNX inference | [`WACS.WASI.NN.OnnxRuntime`](https://www.nuget.org/packages/WACS.WASI.NN.OnnxRuntime) |
| ONNX with ML.NET pipeline integration | [`WACS.WASI.NN.MLNet`](https://www.nuget.org/packages/WACS.WASI.NN.MLNet) |

## Documentation

- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
  — runtime requirements + GGUF example + chaining model
- [`Wacs.WASI/Wacs.WASI.NN/README.md`](https://github.com/kelnishi/WACS/blob/main/Wacs.WASI/Wacs.WASI.NN/README.md)
  — backend matrix

## License

Apache-2.0
