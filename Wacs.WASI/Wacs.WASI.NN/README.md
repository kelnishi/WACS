# Wacs.WASI.NN

wasi-nn host bindings — both the component-model WIT
(`wasi:nn@0.2.0-rc-2024-10-28`) and the legacy WITX
(`wasi_ephemeral_nn`) ABIs against a single backend SPI. Backend
implementations ship as sibling NuGets so consumers wiring only one
skip the others' native binaries.

**👉 New here? Read [`docs/WASI_NN_USAGE.md`](../../docs/WASI_NN_USAGE.md)** — the
unified usage guide covering CLI flags, environment variables, programmatic
embedding, and worked examples for every backend.

This README is the architecture / package map.

## Contents

- **[Wacs.WASI.NN/](Wacs.WASI.NN/)** — core: `WasiNNConfiguration`, `WasiNNHost` (`IBindable`), `IBackend` SPI, WIT + WITX bindings, source-gen `[WitSource]` interfaces under `Wacs.WASI.NN.Nn.{Tensor, Errors, Graph, Inference}`, `IdentityBackend` for smoke tests, plus the `runtime.UseWasiNN(b => b.AddBackend(...))` extension.
- **[Wacs.WASI.NN.Test/](Wacs.WASI.NN.Test/)** — SPI shape, error-path, binding-registration, and resource-impl coverage.
- **[Wacs.WASI.NN.DependencyInjection/](Wacs.WASI.NN.DependencyInjection/)** — DI bundle for the transpiler-direct-link path. Ships `WasiNNBundle`, the `WasiPreview2NNBundle` composite (forwards both Preview2 + WASI.NN `[WitSource]` interface properties through one CLR object), and concrete resource impls (`Tensor`, `Graph`, `GraphExecutionContext`, `Error`).
- **[Wacs.WASI.NN.OnnxRuntime/](Wacs.WASI.NN.OnnxRuntime/)** — direct `Microsoft.ML.OnnxRuntime` backend for `graph-encoding.onnx`. Lightest of the three — just ORT, no ML.NET wrapper. Ships `WasiNNOnnxBindable` (parameterless adapter for `--bind`).
- **[Wacs.WASI.NN.OnnxRuntime.Test/](Wacs.WASI.NN.OnnxRuntime.Test/)** — ORT backend SPI + error-path tests.
- **[Wacs.WASI.NN.MLNet/](Wacs.WASI.NN.MLNet/)** — `Microsoft.ML.OnnxTransformer`-flavored backend wrapping ORT under an `MLContext` lifecycle for embedders chaining wasi-nn with broader ML.NET pipelines. Ships `WasiNNMLNetBindable` (parameterless adapter for `--bind`).
- **[Wacs.WASI.NN.MLNet.Test/](Wacs.WASI.NN.MLNet.Test/)** — ML.NET backend tests.
- **[Wacs.WASI.NN.LlamaSharp/](Wacs.WASI.NN.LlamaSharp/)** — `LLamaSharp` backend for `graph-encoding.ggml` on the WasmEdge GGUF convention (U8 tensors carrying UTF-8 prompt / response). Ships `WasiNNLlamaSharpBindable` with `WACS_WASINN_GGUF_DIR`-driven name registry.
- **[Wacs.WASI.NN.LlamaSharp.Test/](Wacs.WASI.NN.LlamaSharp.Test/)** — LlamaSharp backend SPI + load-by-name routing tests.
- **[Wacs.WASI.NN.TorchSharp/](Wacs.WASI.NN.TorchSharp/)** — `TorchSharp` / libtorch backend for `graph-encoding.pytorch`. Loads TorchScript modules (`torch.jit.save` output) byte-loaded OR via `WACS_WASINN_TORCH_DIR`-driven name registry. Ships `WasiNNTorchSharpBindable`.
- **[Wacs.WASI.NN.TorchSharp.Test/](Wacs.WASI.NN.TorchSharp.Test/)** — TorchSharp backend SPI + name-registry routing tests.

## Quick start

**CLI — ONNX (bundled with the CLI):**

```sh
wacs run my.component.wasm --wasip2 --wasi-nn
```

**CLI — LlamaSharp (GGUF / llama.cpp):** the LlamaSharp backend
isn't bundled with the CLI, so pass the project bin's path
explicitly. The backend csproj uses `EnableDynamicLoading`, so
its bin carries `LLamaSharp.dll` + native runtimes + transitive
NuGets — the LoadFromContext probe satisfies them all locally:

```sh
export WACS_WASINN_GGUF_DIR=/path/to/models
LLAMA=$(realpath Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.LlamaSharp/bin/Release/net8.0/Wacs.WASI.NN.LlamaSharp.dll)
wacs run my.component.wasm --wasip2 --bind "$LLAMA"
```

`--bind` auto-pulls `WACS.WASI.NN` +
`WACS.WASI.NN.DependencyInjection` onto host-packages when the
identity starts with `Wacs.WASI.NN.`; the wasip2 DI scope's
auto-wire registers the backend in BOTH `Backends[GGML]` AND
`LoadByNameBackend`, so guests calling
`wasi:nn/graph.load-by-name(name)` direct-link cleanly. See
[`docs/COMPONENT_CHAINING.md#gguf-inference-example-llamasharp-backend`](../../docs/COMPONENT_CHAINING.md#gguf-inference-example-llamasharp-backend)
for the full chain. ML.NET-flavored ORT works the same shape:
`--bind <wacs-source>/Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.MLNet/bin/Release/net8.0/Wacs.WASI.NN.MLNet.dll`.

**Embedder** — interpreter path:

```csharp
runtime.UseWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()));
```

**Embedder** — transpiler-direct-link (component-model perf path):

```csharp
services
    .AddWasiPreview2()
    .AddWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()))
    .AddWasiPreview2NNBundle();   // composite for the single hostBundle slot
```

The bundle is auto-discovered by `HostPackageResolver` when the
component imports both `wasi:cli/*` and `wasi:nn/*`.

## Tagged for auto-discovery

Each backend package carries `[assembly: WasiHostPackage]`, so
`runtime.AutoDiscoverHostPackages()` finds whichever ones the host
process has loaded.
