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
(via `<EnableDynamicLoading>true</EnableDynamicLoading>`), so `Assembly.LoadFrom`
resolves everything from the LoadFromContext probe — no manual deps staging.

## Requirements

| Concern | Status |
|---|---|
| `LibTorchSharp` + `libtorch_cpu` + `libc10` + `libomp` etc. on the load path | ✅ shipped with the package's `runtimes/<rid>/native/` (`EnableDynamicLoading`) |
| `LoadFrom`'d backend's `runtimes/<rid>/native/` reachable from P/Invoke | ✅ `BindingLoader` registers a `NativeLibrary.SetDllImportResolver` per `--bind`'d backend that probes the assembly's own `runtimes/<rid>/native/` (gap 28 fix in `WACS.Transpiler.Lib` 0.8.13) |
| `libtorch_cpu.dylib` finds bundled `libomp.dylib` on macOS-arm64 | ✅ build-time `install_name_tool -change` rewrites the upstream Homebrew rpath to `@loader_path/libomp.dylib` (gap 29 fix in this csproj) |
| TorchScript model file (`.pt` / `.ts`) on disk | required — supplied by the embedder; see "Building a TorchScript model" below |
| `WACS_WASINN_TORCH_DIR` env var pointing at the model directory | required for the load-by-name path. The byte-loaded path skips this. |

**Supported runtime IDs**: `osx-arm64`, `osx-x64`, `linux-x64`, `win-x64`, `win-x86`,
`win-arm64` (whatever `libtorch-cpu`'s NuGet ships). For GPU, swap `TorchSharp-cpu`
in the project's csproj for `TorchSharp-cuda-12.1` / `-cuda-11.8` / `-rocm-5.2` /
`-macos-arm64` / `-macos-x64` (Apple Metal / MPS) — no source change. The
`EnableDynamicLoading` bin layout copies whichever backend's natives are pulled.

## Two dispatch paths

| Path | Use when |
|---|---|
| `graph.load(builders, PyTorch, target)` — byte-loaded | Small / medium models (~ < 500 MB) where the canonical-ABI lift cost is acceptable |
| `graph.load-by-name(name)` — file-path registry | Big models. Embedder configures a name → path map; libtorch opens the file directly with no host-side copy |

## CLI quick start (load-by-name flow)

TorchSharp isn't bundled with `WACS.Cli` (libtorch's natives are ~1 GB across RIDs).
Pass the explicit path to the backend's bin; everything else is resolved automatically.

```sh
export WACS_WASINN_TORCH_DIR=$(pwd)/models   # *.pt / *.ts files here

# After dotnet build -c Release of the WACS source repo:
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

## Worked example — XOR MLP

A complete end-to-end demo: train a 2-layer MLP on XOR in PyTorch, save as TorchScript,
run inference through WACS from a Rust wasm32-wasip2 component. ~5.9 KB model, deterministic
output.

### 1. Build the model (once, host-side)

```python
# scripts/build_xor_mlp.py
import torch
import torch.nn as nn

class XorMlp(nn.Module):
    def __init__(self):
        super().__init__()
        self.fc1 = nn.Linear(2, 4)
        self.fc2 = nn.Linear(4, 1)
    def forward(self, x):
        return torch.sigmoid(self.fc2(torch.relu(self.fc1(x))))

# Train briefly on the 4 XOR points
model = XorMlp()
X = torch.tensor([[0,0],[0,1],[1,0],[1,1]], dtype=torch.float32)
Y = torch.tensor([[0],[1],[1],[0]], dtype=torch.float32)
opt = torch.optim.Adam(model.parameters(), lr=0.05)
for _ in range(2000):
    opt.zero_grad()
    loss = nn.functional.binary_cross_entropy(model(X), Y)
    loss.backward(); opt.step()

# Trace + save as TorchScript
traced = torch.jit.trace(model, torch.zeros(1, 2, dtype=torch.float32))
traced.save("models/xor-mlp.pt")
print("wrote models/xor-mlp.pt")
```

```sh
mkdir -p models
python scripts/build_xor_mlp.py
```

### 2. Guest component (Rust)

```rust
// guest-torch/src/main.rs (excerpt)
use wasi_nn::{graph::load_by_name, tensor::{Tensor, TensorType}};

fn main() {
    let graph = load_by_name("xor-mlp").expect("graph.load-by-name");
    let ctx = graph.init_execution_context().expect("init-execution-context");

    for (a, b) in [(0u8, 0), (0, 1), (1, 0), (1, 1)] {
        // Single batched input, shape [1, 2], FP32, two scalars (a, b).
        let bytes = [a as f32, b as f32]
            .iter().flat_map(|f| f.to_le_bytes()).collect::<Vec<u8>>();
        let input = Tensor::new(&[1u32, 2u32], TensorType::Fp32, &bytes);

        // Indexed-name positional dispatch convention: "0" → first
        // forward arg.
        let outputs = ctx.compute(vec![("0".to_string(), input)])
            .expect("compute");

        // Single output (sigmoid scalar), shape [1, 1], FP32.
        let logit = f32::from_le_bytes(
            outputs[0].1.data()[..4].try_into().unwrap());
        let pred = if logit > 0.5 { 1 } else { 0 };
        let expected = a ^ b;
        println!("XOR({a}, {b}) -> sigmoid={logit:.4}  pred={pred}  expected={expected}  {}",
            if pred == expected { "OK" } else { "FAIL" });
    }
}
```

Build the guest with `cargo build --target wasm32-wasip2 --release`.

### 3. Run

```sh
export WACS_WASINN_TORCH_DIR=$(pwd)/models

TORCH=$(realpath Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.TorchSharp/bin/Release/net8.0/Wacs.WASI.NN.TorchSharp.dll)
wacs run target/wasm32-wasip2/release/wasi-nn-torch.wasm --wasip2 --bind "$TORCH"
```

Expected output:

```
loading TorchScript 'xor-mlp' (host resolves via $WACS_WASINN_TORCH_DIR)…
ready — xor-mlp via wasi-nn (TorchSharp / libtorch).

  XOR(0, 0) -> sigmoid=0.0000  pred=0  expected=0  OK
  XOR(0, 1) -> sigmoid=1.0000  pred=1  expected=1  OK
  XOR(1, 0) -> sigmoid=0.9994  pred=1  expected=1  OK
  XOR(1, 1) -> sigmoid=0.0000  pred=0  expected=0  OK

all cases pass
```

Numeric output identical to the Python training-time evaluation. Real libtorch FP32
inference, exit 0. No `LD_LIBRARY_PATH` / `DYLD_FALLBACK_LIBRARY_PATH` env vars needed
(both gap-28's resolver hook and gap-29's libomp rpath rewrite are in this package's
build).

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

- **[`docs/WASI_NN_USAGE.md`](https://github.com/kelnishi/WACS/blob/main/docs/WASI_NN_USAGE.md)** —
  unified usage guide (CLI flags, env vars, programmatic embedding, worked examples)
- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
  — runtime requirements + chaining model
- [`Wacs.WASI/Wacs.WASI.NN/README.md`](https://github.com/kelnishi/WACS/blob/main/Wacs.WASI/Wacs.WASI.NN/README.md)
  — backend matrix + package layout

## License

Apache-2.0
