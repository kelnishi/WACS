# WASI-NN on WACS — usage guide

How to wire a wasi-nn-aware component onto a WACS host. Three audiences:

- **CLI users** running stock `wasm32-wasip2` components — read [Quick start](#quick-start) + [CLI invocation](#cli-invocation).
- **Library embedders** adding wasi-nn to a `WasmRuntime` they own — read [Programmatic embedding](#programmatic-embedding).
- **Backend authors / contributors** — see [`Wacs.WASI.NN/README.md`](../Wacs.WASI/Wacs.WASI.NN/README.md) and the per-backend READMEs.

---

## Backend matrix

| Backend | Encoding | Dispatch shape | Verified models | Status |
|---|---|---|---|---|
| [`WACS.WASI.NN.OnnxRuntime`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.OnnxRuntime/) | `ONNX` | `graph.load(bytes)` (byte-loaded) | Gemma 3 270M ONNX SLM | ✅ |
| [`WACS.WASI.NN.LlamaSharp`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.LlamaSharp/) | `GGML` | `graph.load-by-name` | Qwen 2.5 0.5B Q4_K_M GGUF | ✅ |
| [`WACS.WASI.NN.TorchSharp`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.TorchSharp/) | `PyTorch` | `graph.load-by-name` (TorchScript `*.pt` / `*.ts`) | XOR MLP | ✅ |
| [`WACS.WASI.NN.OnnxRuntimeGenAI`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.OnnxRuntimeGenAI/) | `ONNX` (GenAI dir format) | `graph.load-by-name` | Gemma 3 270M IT (GenAI export) | ✅ |
| [`WACS.WASI.NN.MLNet`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.MLNet/) | `ONNX` (via ML.NET) | `graph.load(bytes)` | — | scaffolded |

**Backend selection cheat sheet:**

- ONNX tensor-in / tensor-out (image classification, embeddings, encoder-only) → `OnnxRuntime`
- GGUF / llama.cpp generative LLMs → `LlamaSharp` (Metal-accelerated on Apple Silicon out of the box)
- TorchScript modules → `TorchSharp`
- ONNX-format generative LLMs (Gemma 3, Llama 3, Qwen, Phi exported via `onnxruntime-genai` builder) → `OnnxRuntimeGenAI`
- ONNX in ML.NET pipelines → `MLNet`

---

## Quick start

```sh
# 1. ONNX SLM (byte-loaded, bundled with the CLI)
wacs run my-onnx-guest.wasm --wasip2 --wasi-nn -d ./models::/models

# 2. GGUF chat (LlamaSharp, --bind'd)
export WACS_WASINN_GGUF_DIR=./models
LLAMA=$(realpath Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.LlamaSharp/bin/Release/net8.0/Wacs.WASI.NN.LlamaSharp.dll)
wacs run chat-guest.wasm --wasip2 --bind "$LLAMA"

# 3. TorchScript (TorchSharp, --bind'd)
export WACS_WASINN_TORCH_DIR=./models
TORCH=$(realpath Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.TorchSharp/bin/Release/net8.0/Wacs.WASI.NN.TorchSharp.dll)
wacs run torch-guest.wasm --wasip2 --bind "$TORCH"

# 4. ONNX GenAI (Gemma 3 / Llama Instruct / Qwen Instruct / Phi Instruct)
export WACS_WASINN_GENAI_DIR=./models   # subdirs containing genai_config.json
GENAI=$(realpath Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.OnnxRuntimeGenAI/bin/Release/net8.0/Wacs.WASI.NN.OnnxRuntimeGenAI.dll)
wacs run llm-guest.wasm --wasip2 --bind "$GENAI"
```

---

## CLI invocation

### `--wasi-nn` (shorthand for ONNX)

Adds `WACS.WASI.NN + .DependencyInjection + .OnnxRuntime` to the host package list automatically. ONNX Runtime ships bundled with the CLI (no `--bind` needed):

```sh
wacs run my.component.wasm --wasip2 --wasi-nn -d ./models::/models
```

Use this for **byte-loaded ONNX** workloads (image classification, embeddings, encoder-only models, simple decoder LLMs that fit the byte-load path). The composite `WasiPreview2NNBundle` is auto-discovered when both `--wasip2` and `--wasi-nn` are set.

### `--bind <path-to-backend.dll>` (every other backend)

For LlamaSharp, TorchSharp, OnnxRuntimeGenAI, MLNet, or any future sibling backend, point `--bind` at the backend assembly:

```sh
wacs run my.wasm --wasip2 --bind <path-to-Wacs.WASI.NN.X.dll>
```

`--bind` auto-pulls `WACS.WASI.NN + .DependencyInjection` onto host-packages when the bound assembly's identity starts with `Wacs.WASI.NN.`. The bound DLL's `runtimes/<rid>/native/` subtree is probed for transitive native deps (libtorch, libllama, libonnxruntime-genai, etc.) — `BindBackendLoadContext` handles the discovery automatically.

You can stack `--bind` flags to load multiple backends in one host:

```sh
# Run a component that uses BOTH bytes-in ONNX AND GenAI generative LLMs.
wacs run my.wasm --wasip2 --wasi-nn \
    --bind /path/to/Wacs.WASI.NN.OnnxRuntimeGenAI.dll
```

`OnnxBackend` takes the `Backends[ONNX]` slot; `OnnxGenAIBackend` takes the `LoadByNameBackend` slot — they compose cleanly.

### `-d <hostPath>::<guestPath>` (preopens for model files)

wasi-nn guests that read model bytes (`graph.load`) usually do so via `std::fs::read("/models/foo.onnx")`. Make the host directory visible via a preopen:

```sh
wacs run my.wasm --wasip2 --wasi-nn -d ./models::/models
```

Without a preopen the guest gets `Err: NotCapable` from the filesystem. For `load-by-name` backends (LlamaSharp / TorchSharp / GenAI), the host resolves model paths via env vars instead — no preopen needed.

### `--native-memory` (large models)

The default ManagedArray memory storage caps wasm linear memory at ~2 GiB. ONNX SLM guests that byte-load a 1 GiB+ model cross this cap during the guest's `Vec<u8>` copy. Add `--native-memory` for those workloads:

```sh
wacs run slm-guest.wasm --wasip2 --wasi-nn --native-memory -d ./models::/models
```

Not needed for `load-by-name` backends (the model never enters wasm linear memory).

### `--engine transpiler` / `--engine interpreter`

The transpiler engine direct-links wasi-nn imports for ~10× lower per-call overhead. The interpreter engine uses delegate-dispatched `BindHostFunction` handlers — slower per call but no JIT warmup. Default is **transpiler** for `--wasip2`; both paths share the same backend implementations.

---

## Environment variables

Per-backend cheat sheet. All env vars are optional; defaults shown.

### Common (every backend)

| Env var | Default | Effect |
|---|---|---|
| `WACS_DIAG_MEMORY` | unset | `1` enables per-compute stderr snapshot (rss, managed, gc generations, in/out bytes, drops, duration). Diagnostic only. |

### `WACS.WASI.NN.OnnxRuntime`

| Env var | Default | Effect |
|---|---|---|
| `WACS_WASINN_ONNX_EP` | `cpu` | Execution provider: `auto` / `cpu` / `coreml` / `cuda` / `dml` / `directml` / `rocm` |
| `WACS_WASINN_ONNX_COREML_FLAGS` | — | Comma-separated CoreML flag names: `MLProgram`, `UseCpuAndGpu`, `CpuOnly`, `ANE`, `Static`, `Subgraph` |
| `WACS_WASINN_ONNX_CUDA_DEVICE` | 0 | CUDA device index |
| `WACS_WASINN_ONNX_DML_DEVICE` | 0 | DirectML device index |
| `WACS_WASINN_ONNX_ROCM_DEVICE` | 0 | ROCm device index |

### `WACS.WASI.NN.LlamaSharp`

| Env var | Default | Effect |
|---|---|---|
| `WACS_WASINN_GGUF_DIR` | — | Directory scanned for `*.gguf` files. Each is registered under its filename-without-extension. |

LlamaSharp's libllama enables Metal (Apple Silicon) automatically when the native lib's Metal kernels are available — no env-var opt-in.

### `WACS.WASI.NN.TorchSharp`

| Env var | Default | Effect |
|---|---|---|
| `WACS_WASINN_TORCH_DIR` | — | Directory scanned for `*.pt` and `*.ts` files. Each registered under its filename-without-extension. |

### `WACS.WASI.NN.OnnxRuntimeGenAI`

| Env var | Default | Effect |
|---|---|---|
| `WACS_WASINN_GENAI_DIR` | — | Directory whose first-level subdirectories (each containing `genai_config.json`) are registered under the subdirectory name |
| `WACS_WASINN_GENAI_EP` | `cpu` | Execution provider: `auto` / `cpu` / `coreml` / `cuda` / `dml` / `directml` / `rocm` |
| `WACS_WASINN_GENAI_CUDA_DEVICE` | 0 | CUDA device index |
| `WACS_WASINN_GENAI_DML_DEVICE` | 0 | DirectML device index |
| `WACS_WASINN_GENAI_ROCM_DEVICE` | 0 | ROCm device index |
| `WACS_WASINN_GENAI_MAX_LENGTH` | 512 | Hard cap on prompt+response token count |
| `WACS_WASINN_GENAI_DO_SAMPLE` | 0 | `1` enables sampling (`temperature` / `top_p` / `top_k`) |
| `WACS_WASINN_GENAI_TEMPERATURE` | 1.0 | Sampling temperature |
| `WACS_WASINN_GENAI_TOP_P` | 1.0 | Nucleus sampling cutoff |
| `WACS_WASINN_GENAI_TOP_K` | 50 | Top-k truncation |
| `WACS_WASINN_GENAI_INCLUDE_PROMPT` | 0 | `1` returns prompt+response; default returns response only |

> **Note on auto-promotion.** Both ONNX backends default to CPU. Hardware-accelerated EPs are opt-in. The empirical reason: on small models (≤ 1B params) CoreML's kernel-compile + Metal-command-buffer overhead runs 3-5× slower than CPU. CoreML's win typically appears at 1B+ params. For LLM workloads on Apple Silicon, `WACS.WASI.NN.LlamaSharp` + GGUF is the mature Metal-accelerated path today.

---

## Programmatic embedding

### Interpreter path (one-liner)

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.OnnxRuntime;
using Wacs.WASI.NN.Types;

var runtime = new WasmRuntime();
runtime.UseWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()));
// ... load + instantiate component ...
```

`UseWasiNN` returns the runtime so you can chain. Multiple backends:

```csharp
runtime.UseWasiNN(b =>
{
    b.AddBackend(GraphEncoding.ONNX,    new OnnxBackend());
    b.AddBackend(GraphEncoding.PyTorch, TorchSharpBackend.FromPaths(torchModels));
    b.Configuration.LoadByNameBackend = llamaBackend;  // GGUF / load-by-name
});
```

### Transpiler / wasip2 direct-link path (DI)

For the wasip2 direct-link path that the CLI's `wacs run --wasip2` uses internally:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Wacs.WASI.Preview2.DependencyInjection;
using Wacs.WASI.NN.DependencyInjection;
using Wacs.WASI.NN.OnnxRuntime;
using Wacs.WASI.NN.Types;

var services = new ServiceCollection()
    .AddSingleton<WasmRuntime>(runtime)
    .AddWasiPreview2()
    .AddWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()))
    .AddWasiPreview2NNBundle();   // composite for the single hostBundle slot

using var scope = new WasiPreview2RuntimeScope(runtime);
// scope.Bundle is the WasiPreview2NNBundle instance the moduleClass's
// ctor expects as its `object hostBundle` slot.
// scope.Resources is the corresponding WasiPreview2Resources for the
// third ctor slot.
```

`WasiPreview2RuntimeScope`'s ctor auto-detects sibling backend packages on the load path and wires their auto-wire callbacks. So if `WACS.WASI.NN.OnnxRuntimeGenAI` is loaded in the AppDomain (via `Assembly.Load` or a referenced project), its `BuildOnnxGenAIConfigureCallback` runs and registers the GenAI backend's `LoadByNameBackend` automatically — no explicit `AddBackend` for it.

### Typed-options ctors

Each backend takes a typed options object so embedders pick EPs, devices, sampling params, etc. without touching env vars:

```csharp
var onnx = new OnnxBackend(new OnnxBackendOptions
{
    ExecutionProvider = OnnxExecutionProvider.CoreML,
    CoreMLFlags = CoreMLFlags.COREML_FLAG_CREATE_MLPROGRAM,
    FallbackToCpu = true,
});

var genai = new OnnxGenAIBackend(
    name => modelDirs.TryGetValue(name, out var d) ? d : null,
    new OnnxGenAIBackendOptions
    {
        ExecutionProvider = OnnxGenAIExecutionProvider.Cpu,
        MaxLength = 1024,
        DoSample = true,
        Temperature = 0.7,
        TopP = 0.9,
    });

var torch = TorchSharpBackend.FromPaths(new Dictionary<string, string>
{
    ["xor-mlp"] = "/models/xor-mlp.pt",
});
```

### Escape-hatch ctors (full ORT `SessionOptions`)

For `OnnxBackend` only — pass a `Func<SessionOptions>` factory for control beyond what the typed options expose:

```csharp
var onnx = new OnnxBackend(() =>
{
    var opts = new SessionOptions();
    opts.AppendExecutionProvider_CUDA(deviceId: 0);
    opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
    return opts;
});
```

---

## Worked examples

### 1. ONNX SLM — byte-loaded Gemma 3 through `OnnxRuntime`

Guest reads the ONNX model into `Vec<u8>`, calls `graph.load(bytes, ONNX)`, drives its own autoregressive decode loop via repeated `compute` calls. Slow per token (no KV cache), but doesn't need a chat template or generative-LLM-aware host. See [`docs/COMPONENT_CHAINING.md#end-to-end-example`](COMPONENT_CHAINING.md#end-to-end-example).

```sh
scripts/fetch-model.sh  # Gemma 3 270M ONNX + tokenizer
wacs run wasi-nn-slm.wasm \
    --engine transpiler --wasip2 --wasi-nn --native-memory \
    -d ./models::/models
```

### 2. GGUF chat — Qwen 2.5 through `LlamaSharp`

Guest calls `graph.load-by-name("qwen2.5-0.5b-instruct-q4_k_m")`. Host resolves the name to `$WACS_WASINN_GGUF_DIR/qwen2.5-0.5b-instruct-q4_k_m.gguf`. libllama's Metal kernels engage automatically on Apple Silicon.

```sh
scripts/fetch-gguf.sh  # downloads the GGUF
export WACS_WASINN_GGUF_DIR=./models
LLAMA=$(realpath ../WACS/Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.LlamaSharp/bin/Release/net8.0/Wacs.WASI.NN.LlamaSharp.dll)
echo -e "Hello, who are you?\n/bye" | scripts/run-llm.sh
```

### 3. TorchScript inference — XOR MLP through `TorchSharp`

Guest calls `graph.load-by-name("xor-mlp")` then sends `[1, 2]` FP32 tensors through `compute`. Host resolves to `$WACS_WASINN_TORCH_DIR/xor-mlp.pt`. See [`Wacs.WASI.NN.TorchSharp/README.md`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.TorchSharp/README.md) for the Python script that produces the `.pt`.

```sh
export WACS_WASINN_TORCH_DIR=./models
TORCH=$(realpath ../WACS/Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.TorchSharp/bin/Release/net8.0/Wacs.WASI.NN.TorchSharp.dll)
wacs run wasi-nn-torch.wasm --wasip2 --bind "$TORCH"
```

### 4. Generative LLM — Gemma 3 IT through `OnnxRuntimeGenAI`

Guest sends the user message as a `"prompt"` U8 tensor. Host applies the model's chat template, tokenizes, runs the KV-cached decode loop, detokenizes, returns the response as a `"response"` U8 tensor. Same guest harness (`scripts/run-llm.sh`) as the LlamaSharp track — pick by `--bind` and `MODEL_NAME`.

```sh
# Download a GenAI-format model. Either from HuggingFace:
huggingface-cli download onnx-community/gemma-3-270m-it-ONNX \
    --local-dir ./models/gemma-3-270m-it-genai

# Or convert your own ONNX:
python -m onnxruntime_genai.models.builder \
    -m google/gemma-3-270m-it -o ./models/gemma-3-270m-it-genai -p int4

# Run:
export WACS_WASINN_GENAI_DIR=./models
GENAI=$(realpath ../WACS/Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.OnnxRuntimeGenAI/bin/Release/net8.0/Wacs.WASI.NN.OnnxRuntimeGenAI.dll)
BACKEND_DLL=$GENAI MODEL_NAME=gemma-3-270m-it-genai scripts/run-llm.sh
```

Expected:

```
>>> What is 2+2?
2 + 2 = 4

>>> What is the capital of France?
The capital of France is Paris.
```

### 5. Compose ONNX + GenAI in one host

A component that uses `graph.load(bytes)` for image preprocessing AND `graph.load-by-name(...)` for generative captioning can wire both backends:

```sh
GENAI=$(realpath .../Wacs.WASI.NN.OnnxRuntimeGenAI.dll)
export WACS_WASINN_GENAI_DIR=./models
wacs run multimodal.wasm --wasip2 --wasi-nn --bind "$GENAI" -d ./assets::/assets
```

- `--wasi-nn` bundles `OnnxBackend` into `Backends[ONNX]` (byte-load path).
- `--bind <GenAI>` adds `OnnxGenAIBackend` into `LoadByNameBackend` only.
- Both are present in the same `WasiNNConfiguration`; the guest picks via its load API.

---

## Diagnostics

### `WACS_DIAG_MEMORY=1`

Per-compute stderr snapshot. Useful for "RSS is climbing" reports — distinguishes input-driven growth (the guest is accumulating conversation history) from host-side leaks.

```sh
WACS_DIAG_MEMORY=1 wacs run chat.wasm --wasip2 --bind <...> 2> diag.log
```

Each `compute()` call emits:

```
[wacs-diag-memory] turn=42 rss=6.40GiB (+85MiB) managed=1.03GiB (-1.21GiB) gc[g0=1493 g1=197 g2=161] in=4880B out=331MB drops[interp=0 ext=0] took=0.27s
```

- `in` / `out` — guest-supplied / host-returned tensor byte totals
- `drops[interp=X ext=Y]` — `[resource-drop]X` counter (interpreter binding fires + cross-table hook fires). On `--engine transpiler` both stay 0 — drops route through `ComponentMainHost`'s `[resource-drop]X` auto-handler instead (post-PR-#137 fix).

### Component imports inspection

```sh
wacs inspect my.wasm | grep wasi:nn
```

Shows the guest's wasi-nn imports — handy for verifying which `--bind`'d backend is required.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `ErrorCode::InvalidEncoding` at `graph.load` | No backend registered for the requested encoding | Add `--wasi-nn` for ONNX, or `--bind <backend.dll>` for the right encoding |
| `ErrorCode::NotFound: no named-model resolver configured` | Backend that uses `load-by-name` not wired into `LoadByNameBackend` | Set the backend's `*_DIR` env var; check that the model file/directory exists under that root |
| `Unable to load shared library 'libX'` | Bind-asm's transitive native lib not in expected location | Should be auto-resolved by `BindBackendLoadContext` (post-PR-#131). If it persists, file an issue with `otool -L` / `ldd` output |
| Gibberish output from a chat model | Chat template not applied | Use `OnnxRuntimeGenAI` (host-applies chat template) instead of raw `OnnxRuntime` (byte-load path) |
| `mutex lock failed: Invalid argument` after many turns | Pre-PR-#137 leak (`[resource-drop]X` no-op'd) | Update to `WACS.Cli ≥ 1.5.24` / `WACS.Transpiler.Lib ≥ 0.8.15` |
| `Unknown provider name 'nnapi'` (GenAI) | Model's `genai_config.json` hard-codes an Android-only provider | Update to `WACS.WASI.NN.OnnxRuntimeGenAI ≥ 0.1.2` — `Config.ClearProviders()` strips the model-declared list |

---

## Related docs

- [`docs/COMPONENT_CHAINING.md`](COMPONENT_CHAINING.md) — full end-to-end example (Gemma 3 ONNX SLM) with the wasip2 component-model chain
- [`Wacs.WASI/Wacs.WASI.NN/README.md`](../Wacs.WASI/Wacs.WASI.NN/README.md) — architecture deep-dive, package layout
- Per-backend READMEs: [`OnnxRuntime`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.OnnxRuntime/README.md) · [`LlamaSharp`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.LlamaSharp/README.md) · [`TorchSharp`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.TorchSharp/README.md) · [`OnnxRuntimeGenAI`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.OnnxRuntimeGenAI/README.md) · [`MLNet`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.MLNet/README.md)
- [`Wacs.Console/Wacs.Console/README.md`](../Wacs.Console/Wacs.Console/README.md) — CLI reference
