# WACS.WASI.NN.OnnxRuntimeGenAI

[OnnxRuntime-GenAI](https://github.com/microsoft/onnxruntime-genai) backend for
[`WACS.WASI.NN`](https://www.nuget.org/packages/WACS.WASI.NN). Wraps
Microsoft's generative-LLM runtime — first-class tokenizer + KV cache +
sampling — and surfaces it through wasi-nn as a `load-by-name` backend.

Where [`WACS.WASI.NN.OnnxRuntime`](https://www.nuget.org/packages/WACS.WASI.NN.OnnxRuntime)
serves single-shot tensor-in / tensor-out inference (image classification,
embeddings, encoder-only models), this serves generative LLMs:
**Gemma 3**, **Llama 3**, **Qwen 2.5**, **Phi 4** — anything the upstream
`onnxruntime-genai` `model_builder.py` script can produce.

## Install

```bash
dotnet add package WACS.WASI.NN.OnnxRuntimeGenAI
```

## How model resolution works

GenAI models ship as **directories**, not single ONNX files:

```
gemma-3-270m-it/
├── genai_config.json     <- required descriptor
├── tokenizer.json
├── tokenizer_config.json
├── special_tokens_map.json
├── model.onnx
└── model.onnx.data        <- external weights
```

Get a GenAI-ready model one of two ways:

```sh
# Pre-built from Hugging Face (recommended)
huggingface-cli download onnx-community/gemma-3-270m-it-ONNX \
    --local-dir ./models/gemma-3-270m-it

# Or convert your own ONNX with onnxruntime-genai's builder
python -m onnxruntime_genai.models.builder \
    -m google/gemma-3-270m-it \
    -o ./models/gemma-3-270m-it \
    -p int4
```

Then point the bindable at the directory:

```sh
export WACS_WASINN_GENAI_DIR=./models
ls $WACS_WASINN_GENAI_DIR
# gemma-3-270m-it  qwen2.5-1.5b-instruct  phi-4-mini

wacs run --wasip2 --bind Wacs.WASI.NN.OnnxRuntimeGenAI.dll my.wasm
```

Each first-level subdirectory that contains a `genai_config.json` is
registered under its directory name. A guest call to
`graph.load-by-name("gemma-3-270m-it")` resolves to the
`./models/gemma-3-270m-it/` directory.

## Two compute shapes

The backend dispatches by the **first input tensor's name**:

### `compute(["prompt" → utf-8 bytes])` → `["response" → utf-8 bytes]`

Single-shot generation. The host:

1. Decodes UTF-8 bytes → prompt string
2. Tokenizes via GenAI's `Tokenizer.Encode`
3. Builds `GeneratorParams` from env-var defaults (max_length / sampling /
   temperature / top_p / top_k)
4. Runs the decode loop with GenAI's KV cache hot across `GenerateNextToken`
   calls (this is where the GenAI win materializes vs. raw ORT)
5. Detokenizes back to a string
6. Returns the generated portion (or full prompt+response with
   `WACS_WASINN_GENAI_INCLUDE_PROMPT=1`)

Best for new chat / completion guests. **Streaming output isn't supported**
— wasi-nn's compute is a single call. The whole response arrives at once.

### `compute(["input_ids" → int64 tensor])` → `["logits" → float32 tensor]`

Single forward pass. The host:

1. Reinterprets the int64 tensor bytes as token IDs (narrowed to int32 — GenAI
   uses 32-bit tokens internally)
2. Constructs a fresh `Generator`, appends the tokens, runs one
   `GenerateNextToken`, extracts the `logits` output
3. Returns the FP32 logits tensor of shape `[batch, seq_len, vocab]`

**Stateless** — each call gets a fresh generator (KV cache wiped). The guest
drives its own decode loop. Useful when an existing wasi-nn ONNX guest is
already structured around per-token forward passes and you want a drop-in
replacement that uses GenAI's kernels.

## Configuration

| Env var                              | Default | Description                                                |
|--------------------------------------|---------|------------------------------------------------------------|
| `WACS_WASINN_GENAI_DIR`              | —       | Root containing GenAI model subdirectories                 |
| `WACS_WASINN_GENAI_MAX_LENGTH`       | 512     | Hard cap on prompt+response token count                    |
| `WACS_WASINN_GENAI_DO_SAMPLE`        | 0       | `1` enables sampling (temperature / top_p / top_k)         |
| `WACS_WASINN_GENAI_TEMPERATURE`      | 1.0     | Sampling temperature (when `DO_SAMPLE=1`)                  |
| `WACS_WASINN_GENAI_TOP_P`            | 1.0     | Nucleus sampling cutoff                                    |
| `WACS_WASINN_GENAI_TOP_K`            | 50      | Top-k truncation                                           |
| `WACS_WASINN_GENAI_INCLUDE_PROMPT`   | 0       | `1` returns prompt+response; default returns response only |
| `WACS_WASINN_GENAI_EP`               | `cpu`   | Execution provider: `auto` / `cpu` / `coreml` / `cuda` / `dml` / `rocm` |
| `WACS_WASINN_GENAI_CUDA_DEVICE`      | 0       | CUDA device index (when `EP=cuda`)                         |
| `WACS_WASINN_GENAI_DML_DEVICE`       | 0       | DirectML device index (when `EP=dml`)                      |
| `WACS_WASINN_GENAI_ROCM_DEVICE`      | 0       | ROCm device index (when `EP=rocm`)                         |

Library embedders pass an `OnnxGenAIBackendOptions` to the ctor instead.

## Hardware acceleration

Default is **CPU** — hardware acceleration is **opt-in** via
`WACS_WASINN_GENAI_EP` or `OnnxGenAIBackendOptions.ExecutionProvider`. CPU
default is empirical: on osx-arm64 against `gemma-3-270m-it-genai`,
CoreML produces correct output but runs **3-5× slower** than CPU because
kernel-compile + Metal-command-buffer overhead dominates the actual
compute for small models. CoreML's win typically kicks in at 1B+ params.

| OS                | `WACS_WASINN_GENAI_EP=auto` resolves to | Notes                                                          |
|-------------------|-----------------------------------------|----------------------------------------------------------------|
| macOS (arm64/x64) | CoreML                                  | osx-arm64 GenAI dylib links `CoreML.framework` — no NuGet swap |
| Windows           | DirectML                                | Substitute `Microsoft.ML.OnnxRuntimeGenAI.DirectML` for full coverage |
| Linux             | CUDA then ROCm                          | Substitute `.Cuda` / `.Rocm` variants for native deps          |
| Other             | CPU                                     |                                                                |

EP-append failure silently falls back to CPU (`FallbackToCpu = true` by
default) — embedders that want strict-mode set it false to surface
EP-misconfiguration as `WasiNNException(RuntimeError)`.

Enable via environment:

```sh
# Platform-best pick (CoreML on macOS, DirectML on Windows, CUDA on Linux)
WACS_WASINN_GENAI_EP=auto wacs run my.wasm --wasip2 --bind <...>

# Force a specific provider
WACS_WASINN_GENAI_EP=coreml wacs run my.wasm --wasip2 --bind <...>
WACS_WASINN_GENAI_EP=cuda WACS_WASINN_GENAI_CUDA_DEVICE=1 \
    wacs run my.wasm --wasip2 --bind <...>

# Explicitly stay on CPU (the default — no env var also gets you CPU)
WACS_WASINN_GENAI_EP=cpu wacs run my.wasm --wasip2 --bind <...>
```

Library embedder (typed config):

```csharp
var backend = new OnnxGenAIBackend(
    name => Directory.Exists($"./models/{name}") ? $"./models/{name}" : null,
    new OnnxGenAIBackendOptions
    {
        ExecutionProvider = OnnxGenAIExecutionProvider.CoreML,
        FallbackToCpu = true,
    });
```

## Composes with WACS.WASI.NN.OnnxRuntime

Both can be loaded in the same process. This package registers only as
`LoadByNameBackend`, leaving `Backends[ONNX]` for the regular `OnnxBackend`:

- Guest call `graph.load(bytes, ONNX)` → `WACS.WASI.NN.OnnxRuntime`
- Guest call `graph.load-by-name("gemma-3-270m-it")` → this package

```sh
wacs run --wasip2 --wasi-nn \
         --bind Wacs.WASI.NN.OnnxRuntimeGenAI.dll \
         my.wasm
```

## Backend choice

| Use case | Package |
|---|---|
| Image classification, embeddings, encoder-only LLMs (byte-loaded ONNX) | [`WACS.WASI.NN.OnnxRuntime`](https://www.nuget.org/packages/WACS.WASI.NN.OnnxRuntime) |
| Generative LLMs in ONNX/GenAI format (Gemma 3, Llama 3, Qwen 2.5, Phi 4) | **WACS.WASI.NN.OnnxRuntimeGenAI** (this) |
| Generative LLMs in GGUF format (llama.cpp models — Metal on Apple Silicon works out of the box) | [`WACS.WASI.NN.LlamaSharp`](https://www.nuget.org/packages/WACS.WASI.NN.LlamaSharp) |
| TorchScript modules (`.pt` / `.ts`, PyTorch ecosystem) | [`WACS.WASI.NN.TorchSharp`](https://www.nuget.org/packages/WACS.WASI.NN.TorchSharp) |

## License

Apache-2.0
