# WACS.WASI.NN.DependencyInjection

DI bundle + concrete resource impls for
[`WACS.WASI.NN`](https://www.nuget.org/packages/WACS.WASI.NN). This is the package the
transpiler-direct-link path needs at scope-construction time — without it,
`[constructor]X` for wasi-nn resources falls back to delegate dispatch and returns handle
0 to the guest.

## Install

```bash
dotnet add package WACS.WASI.NN.DependencyInjection
```

For most wasi-nn workflows, install this package alongside
[`WACS.WASI.Preview2.DependencyInjection`](https://www.nuget.org/packages/WACS.WASI.Preview2.DependencyInjection)
+ a backend (`WACS.WASI.NN.OnnxRuntime` / `WACS.WASI.NN.LlamaSharp` /
`WACS.WASI.NN.MLNet`).

## What's inside

- **`WasiNNBundle`** — exposes WASI.NN's `[WitSource]` interfaces (`IGraphFuncs`,
  `IInferenceFuncs`, `IErrorFuncs`) as properties that direct-link emit binds to
- **`WasiPreview2NNBundle`** — composite bundle that forwards BOTH Preview 2 and WASI.NN
  interfaces through one CLR object. Required when a component imports `wasi:cli/*` +
  `wasi:nn/*` (the typical SLM / inference-CLI shape)
- **Concrete resource impls** — `Tensor`, `Graph`, `GraphExecutionContext`, `Error` —
  parameterless ctor + SourceGen-shape `void Create(args)` that
  `Activator.CreateInstance(impl)` instantiates
- **`AddWasiNN`** + **`AddWasiPreview2NNBundle`** service-collection extensions

## Quick start — DI

```csharp
using Microsoft.Extensions.DependencyInjection;
using Wacs.WASI.Preview2.DependencyInjection;
using Wacs.WASI.NN.DependencyInjection;
using Wacs.WASI.NN.OnnxRuntime;
using Wacs.WASI.NN.Types;

var services = new ServiceCollection();
services
    .AddWasiPreview2()
    .AddWasiNN(opts =>
    {
        opts.AddBackend(GraphEncoding.ONNX, new OnnxBackend());
        // or:
        // opts.Configuration.LoadByNameBackend = new LlamaSharpBackend(...);
    })
    .AddWasiPreview2NNBundle();   // composite for the single hostBundle slot

using var sp = services.BuildServiceProvider();
using var scope = sp.CreateScope();

var linker = scope.ServiceProvider.GetRequiredService<Linker>();
// linker.Runtime is the WasmRuntime ready for component instantiation
```

## Quick start — `WasiPreview2RuntimeScope` (one-shot)

For most embedders the simpler path is `WasiPreview2RuntimeScope` from
[`WACS.WASI.Preview2.DependencyInjection`](https://www.nuget.org/packages/WACS.WASI.Preview2.DependencyInjection),
which auto-detects this package on the load path and registers the composite bundle
without manual configuration:

```csharp
using var wasi = new WasiPreview2RuntimeScope(runtime,
    preopens: new[] { ("./models", "/models") });
// wasi.Bundle is automatically the composite when this DI sibling is loaded
```

Auto-wires `OnnxBackend` if `WACS.WASI.NN.OnnxRuntime` is loadable; auto-wires LlamaSharp
into both `Backends[GGML]` and `LoadByNameBackend` if `WACS.WASI.NN.LlamaSharp` is
loadable.

## Why this is a separate package

The typed resource interfaces (`ITensor`, `IGraph`, `IGraphExecutionContext`, `IError`)
live in [`WACS.WASI.NN`](https://www.nuget.org/packages/WACS.WASI.NN) — they're what
direct-link emit references at the import call site. The concrete impl classes here
(`Tensor`, `Graph`, etc.) are what the transpiler instantiates via
`Activator.CreateInstance(impl) + Create(args)` for `[constructor]X`. Both halves are
needed; splitting them keeps the typed surface light when an embedder doesn't need the
default impls.

## Documentation

- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
  — runtime requirements, host-package composition, end-to-end examples
- Top-level WACS [README — Component Model & WASI Preview 2](https://github.com/kelnishi/WACS#component-model--wasi-preview-2)

## License

Apache-2.0
