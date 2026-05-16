# WACS.WASI.NN

Host bindings for [wasi-nn](https://github.com/WebAssembly/wasi-nn) — the
WebAssembly System Interface for neural-network inference. Both the
component-model WIT (`wasi:nn@0.2.0-rc-2024-10-28`) and the legacy
WITX (`wasi_ephemeral_nn`) ABIs are wired against a single backend SPI;
embedders configure which backend handles which graph encoding and the
host orchestrator routes accordingly.

## Packages

| Package | Role |
|---|---|
| `WACS.WASI.NN` | Core: `WasiNNConfiguration`, `WasiNNHost` (`IBindable`), `IBackend` SPI, WIT + WITX bindings, source-gen `[WitSource]` interfaces under `Wacs.WASI.NN.Nn.{Tensor, Errors, Graph, Inference}`, `IdentityBackend`, `runtime.UseWasiNN(...)` extension |
| `WACS.WASI.NN.DependencyInjection` | DI bundle for the transpiler-direct-link path: `WasiNNBundle`, `WasiPreview2NNBundle` composite, concrete resource impls (`Tensor`, `Graph`, `GraphExecutionContext`, `Error`), `services.AddWasiNN(...)` registration |
| `WACS.WASI.NN.OnnxRuntime` | Direct ONNX Runtime backend (`graph-encoding.onnx`). Ships `WasiNNOnnxBindable` parameterless adapter for `--bind` |
| `WACS.WASI.NN.OnnxRuntimeGenAI` | ONNX Runtime GenAI backend (`graph-encoding.ggml` named-input convention) for first-class generative LLM inference (Gemma 3, Llama, Qwen, Phi families). Exposes `"prompt"` (utf-8) and `"input_ids"` (int64) compute shapes; CoreML acceleration on osx-arm64. Ships `WasiNNOnnxGenAIBindable` |
| `WACS.WASI.NN.MLNet` | Microsoft.ML-flavored backend wrapping ONNX Runtime under an `MLContext` lifecycle. Ships `WasiNNMLNetBindable` for `--bind` |
| `WACS.WASI.NN.LlamaSharp` | LlamaSharp / llama.cpp backend (`graph-encoding.ggml`). Ships `WasiNNLlamaSharpBindable` with `WACS_WASINN_GGUF_DIR`-driven name registry |
| `WACS.WASI.NN.TorchSharp` | TorchSharp / libtorch backend (`graph-encoding.pytorch`). Loads TorchScript modules via `graph.load` (bytes) or `graph.load-by-name` (registry). CPU default; swap `LibTorch-cuda-12.1` / `-macos-arm64` etc. in the consumer csproj for accelerators. Ships `WasiNNTorchSharpBindable` |
| `WACS.WASI.NN.OpenVino` | OpenVINO backend (`graph-encoding.openvino`) via the OpenVINO.CSharp.API NuGet. Loads OpenVINO IR (xml + bin) from the wasi-nn multi-builder shape. Ships `WasiNNOpenVinoBindable`, bundles macOS arm64 runtime; other RIDs ride on system OpenVINO install |

The packages are siblings — consumers wiring only one backend skip the
others' NuGet transitives (ORT native binaries, `Microsoft.ML`,
LlamaSharp's llama.cpp runtime, libtorch's C++ runtime, OpenVINO's
native libs).

## Quick start

**CLI (zero code) — wasi-p2 component path:**

```sh
wacs run my.component.wasm --wasip2 --wasi-nn -d ./models
# --wasi-nn loads Wacs.WASI.NN.OnnxRuntime; bundled with the CLI.
# Component auto-dispatches wasi:cli/run@<version>#run.
```

**CLI — wasi-p1 WITX core-wasm path** (guests built against `wasi-nn = "0.6"` for `wasm32-wasip1`):

```sh
wacs run my-witx-guest.wasm \
    --bind /path/to/Wacs.WASI.NN.LlamaSharp.dll \
    -e MODEL_NAME=qwen2.5-0.5b-instruct-q4_k_m
```

Drop `--wasip2`; WITX guests import `wasi_ephemeral_nn.*` directly. WASI Preview 1 does not auto-forward the host's process environment, so pass each var the guest reads via `std::env::var` with `-e KEY=VALUE`. (On `--wasip2`, host env auto-forwards through `wasi:cli/environment.get-environment`; a plain `export` is enough.) See [`docs/WASI_NN_USAGE.md`](https://github.com/kelnishi/WACS/blob/main/docs/WASI_NN_USAGE.md) for the full invocation reference.

**Embedder one-liner (interpreter path):**

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.OnnxRuntime;
using Wacs.WASI.NN.Types;

var runtime = new WasmRuntime();
using var host = runtime.UseWasiNN(b =>
    b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()));
// runtime now satisfies wasi-nn imports for both ABIs.
```

**Verbose path (still supported):**

```csharp
var cfg = WasiNNConfiguration.DefaultConfiguration();
cfg.Backends[GraphEncoding.ONNX] = new OnnxBackend();
using var host = new WasiNNHost(cfg);
host.BindToRuntime(runtime);
```

**Transpiler-direct-link (component-model perf path):**

```csharp
using Wacs.WASI.Preview2.DependencyInjection;
using Wacs.WASI.NN.DependencyInjection;

services
    .AddWasiPreview2()
    .AddWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()))
    .AddWasiPreview2NNBundle();   // composite for the single hostBundle slot
```

`HostPackageResolver` finds the `WasiPreview2NNBundle` composite when
both packages are loaded — its forwarding properties cover both
Preview2 + WASI.NN `[WitSource]` interfaces through one CLR object,
no transpiler emit changes required.

For LLM workloads on the WasmEdge GGUF convention:

```csharp
var llama = LlamaSharpBackend.FromPaths(new Dictionary<string, string>
{
    ["llama-7b"] = "/models/llama-7b-q4.gguf",
});

var cfg = WasiNNConfiguration.DefaultConfiguration();
cfg.LoadByNameBackend = llama;       // takes precedence over NamedModelResolver
// Optional: cfg.Backends[GraphEncoding.ONNX] = new OnnxBackend();

using var host = new WasiNNHost(cfg);
host.BindToRuntime(runtime);
```

Guest pseudocode (any wasi-nn binding library targeting either ABI):

```rust
let graph = wasi_nn::load_by_name("llama-7b")?;
let mut ctx = graph.init_execution_context()?;
let prompt = "What is 2 + 2?";
let input = Tensor::new(&[prompt.len() as u32], TensorType::U8, prompt.as_bytes());
let outputs = ctx.compute(&[("0", &input)])?;
let response = std::str::from_utf8(outputs[0].1.data())?;
```

## Architecture

```
Guest (compiled against either WIT or WITX)
   │
   │  imports
   ▼
WasmRuntime — wasi:nn/{tensor,graph,inference,errors}@0.2.0-rc-2024-10-28
              + wasi_ephemeral_nn (legacy)
   │
   ▼
WitBindings.cs / WitxBindings.cs — canonical-ABI lift/lower per import
   │
   ▼
WasiNNHost — resource tables + LoadGraphByNameDispatch + ResolveBackend
   │
   ▼
IBackend (encoding-keyed in WasiNNConfiguration.Backends, plus the
          optional LoadByNameBackend slot for backend-internal registries)
   │
   ▼
IBackendGraph → IBackendContext → Compute(NamedTensor[]) → NamedTensor[]
```

### Dual ABIs, one SPI

Both ABIs converge on the same `IBackend` surface. The WITX side
synthesizes input names by index (`"0"`, `"1"`, …) so the backend never
needs to know whether the guest came in through the legacy or
component-model path. Resource handles (graph / context / tensor /
error) live in shared `ResourceTable`s on `WasiNNHost`, so the two
ABIs are interchangeable — a guest could in principle mint a graph
through WITX and call its WIT methods, though no real guest does that.

### Zero-copy graph-load

`graph.load(builders, ...)` lifts the guest's `list<list<u8>>` directly
as `ReadOnlyMemory<byte>` views over the linear-memory array — no
host-side copy. Backends MUST consume the bytes before `LoadGraph`
returns; ORT / LlamaSharp / Microsoft.ML all naturally satisfy this
(their load APIs copy/pin model bytes into native memory at session/
weights construction). For multi-MB ONNX models or multi-GB GGUFs, this
saves one full copy on every load. See `IBackend.LoadGraph` for the
ownership contract.

### Encoding routing

| Encoding | Default backend route |
|---|---|
| `onnx` | `OnnxBackend` (or `MLNetBackend` if both registered, last-write-wins) |
| `ggml` | `LlamaSharpBackend` via `LoadByNameBackend` slot; the `OnnxRuntimeGenAIBackend` also registers here (with `LoadByNameBackend`) for generative LLMs through the ONNX-Runtime stack |
| `pytorch` | `TorchSharpBackend` (load from bytes or by name) |
| `openvino` | `OpenVinoBackend` (loads OpenVINO IR from the multi-builder `xml + bin` shape) |
| `tensorflow` / `tensorflowlite` | unwired — embedder provides their own `IBackend` |
| `autodetect` | whichever backend the embedder registered for it |

Encodings without a registered backend reject `graph.load` with
`error-code.invalid-encoding`; the host never silently routes between
encodings.

### Type support

`FP32`, `FP64`, `U8`, `I32`, `I64` round-trip through every backend.
`FP16` and `BF16` throw `error-code.unsupported-operation` — backends
that need them have to opt in once a .NET-side half-precision exchange
type is wired.

## Testing

`Wacs.WASI.NN.Test` and the per-backend test siblings cover the SPI
surface + every error path. Real-model end-to-end tests live under
`Spec.Test/components/fixtures/wasi-nn-*` and consume real ONNX /
GGUF / OpenVINO / TorchScript model files; CI doesn't provision
GB-scale models, so backend-specific generation tests gate behind
the appropriate `WACS_*_MODEL_PATH` / `WACS_WASINN_*_DIR` env vars.

## Pinning

`wit/wasi-nn.wit` and `wit/wasi-nn.witx` are vendored verbatim from
upstream `WebAssembly/wasi-nn` at commit `71320d9` (2024-10-28). The
WIT package version is `wasi:nn@0.2.0-rc-2024-10-28`. Re-fetch via the
commands in `wit/deps.lock`.

The legacy WITX is retained "for consistency only" per the upstream
header — but real wasi-nn guests today still target it (notably most
Rust crates predating the WIT cut). Both ABIs are first-class here.

## Standardization status

The wasi-nn proposal is at [WASI Phase 2](https://github.com/WebAssembly/WASI/blob/main/Proposals.md);
the WIT surface may change as the proposal evolves toward stable
(Phase 4). Public API stability follows that cadence — bumps follow
upstream WIT revisions in addition to host-side changes.
