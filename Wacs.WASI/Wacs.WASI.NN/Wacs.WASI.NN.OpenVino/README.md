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
# 1. Backend DLL (small, RID-agnostic).
dotnet add package WACS.WASI.NN.OpenVino

# 2. Native runtime for your platform (~150-200 MB unpacked).
#    Pick exactly one matching your RID:
dotnet add package OpenVINO.runtime.macos-arm64    # Apple Silicon — 2024.4.0.1 only (see version-skew note)
dotnet add package OpenVINO.runtime.macos-x86_64   # Intel Mac — 2024.4.0.1
dotnet add package OpenVINO.runtime.win            # Windows x64 — 2026.0.0
dotnet add package OpenVINO.runtime.ubuntu.22-x86_64  # — 2024.4.0.1
# (other Linux distros + arm64 variants are published as
#  OpenVINO.runtime.{centos7,rhel8,debian10}-x86_64 /
#  OpenVINO.runtime.ubuntu.{18,20}-arm64 etc. — see
#  https://www.nuget.org/packages?q=openvino.runtime)
```

The runtime pack is shipped as a sibling NuGet rather than bundled with the
backend or CLI because each one is large enough that bundling all four would
push the parent NuGet past the 250 MB upload limit. Native binaries land in
`runtimes/<rid>/native/` of your published output via NuGet's standard
RID-aware restore — `OpenVINO.CSharp.API` P/Invokes against the standard
library names (`openvino.dylib` / `openvino.so` / `openvino.dll`) and finds
them at load time.

### Version skew: bring-your-own native runtime for `2025.x` / `2026.x` IR

`OpenVINO.runtime.macos-arm64`, `macos-x86_64`, and the `ubuntu.*-x86_64`
packs are **stuck at 2024.4.0.1** (Sep 2024). Only `OpenVINO.runtime.win` has
moved forward (2026.0.0). The OpenVINO IR format is backward-compatible
within a major version but **not forward-compatible** — IR exported by
`pip install openvino==2025.x` (or 2026.x) trips
`Incorrect weights in bin file!` at `Core.read_model` when handed to a
2024.4 runtime.

If your model-export side is on a newer OpenVINO than 2024.4, you have two
options:

1. **Pin Python**: `pip install openvino==2024.4.0` and re-export the IR.
   Simplest; works as long as the older `openvino` wheel installs cleanly
   on your platform.

2. **Drop in Intel's official native runtime**: download from
   [`storage.openvinotoolkit.org`](https://storage.openvinotoolkit.org/repositories/openvino/packages/)
   and stage the dylibs over the NuGet-installed `runtimes/<rid>/native/`
   directory. The shipped C# wrapper P/Invokes by soname (`libopenvino_c`)
   so a newer Intel runtime is binary-compatible at the wrapper layer.

A turnkey script for option (2) lives at
[`scripts/fetch-openvino-native.sh`](scripts/fetch-openvino-native.sh) in
this package's repo:

```sh
# Downloads OpenVINO 2025.4.1 macOS arm64, auto-detects the wacs install
# location, and stages the dylibs into runtimes/osx-arm64/native/.
./fetch-openvino-native.sh

# Specify version (default 2025.4.1) and destination explicitly:
./fetch-openvino-native.sh -v 2026.1.0 -d /path/to/runtimes/osx-arm64/native
```

The script currently supports `osx-arm64` only — the macOS arm64 + 2025.x
pip-export case is the gap that prompted writing it. Linux / Windows users
typically have a workable NuGet runtime already; open an issue if you need
the script extended.

## CLI

The `wacs` CLI bundle ships `Wacs.WASI.NN.OpenVino.dll` but **not** the OpenVINO
native runtime (size-bound — see the install note above). You need to drop the
native libraries into the WACS install directory's `runtimes/<rid>/native/`
folder yourself before invoking, or load them via `LD_LIBRARY_PATH` /
`DYLD_LIBRARY_PATH` / `PATH`.

The easiest way to get a matched set is to install `OpenVINO.runtime.<rid>` into
a scratch project and copy the unpacked natives:

```sh
mkdir openvino-native && cd openvino-native
dotnet new console
dotnet add package OpenVINO.runtime.macos-arm64    # match your RID
dotnet build
cp -R bin/Debug/net*/runtimes "$(dirname $(which wacs))/"
```

Then invoke:

```sh
wacs run my.component.wasm --wasip2 --bind WACS.WASI.NN.OpenVino.dll
```

The CLI's `IBindable`-discovery pass auto-picks `WasiNNOpenVinoBindable` and the
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
