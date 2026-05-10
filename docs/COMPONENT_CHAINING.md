# Component chaining and runtime requirements

How WACS composes WASI host packages when a component imports from
multiple WIT interfaces (`wasi:cli/*` + `wasi:nn/*`, etc.), and what
needs to be on the load path for each capability.

## TL;DR

```bash
# CLI: all chaining handled automatically
wacs run my.component.wasm --wasip2 --wasi-nn -d ./models::/models
```

```csharp
// Embedder: one-shot scope wires every loaded WASI package
using var wasi = new WasiPreview2RuntimeScope(
    runtime,
    preopens: new[] { ("./models", "/models") });
// wasi.Bundle / wasi.Resources are ready for component instantiation.
```

The CLI's `--wasip2` / `--wasi-nn` shorthands and
`WasiPreview2RuntimeScope` both produce a single composite
`hostBundle` + a single `Resources` registry — the two CLR
objects the transpiler emits direct-link IL against.

## Runtime requirements

Each capability needs a specific package set on the load path. The
CLI shorthands include the implementation siblings automatically;
embedders bypassing the CLI need to either include them in
`HostPackages` or rely on the resolver's AppDomain fallback.

| Capability | Packages on load path | Why each is needed |
|---|---|---|
| `--wasip2` | `WACS.WASI.Preview2`<br>`WACS.WASI.Preview2.DependencyInjection` | Typed `[WitSource]` interfaces (Preview 2)<br>Bundle + impl classes for direct-link emit |
| `--wasi-nn` | adds `WACS.WASI.NN`<br>`WACS.WASI.NN.DependencyInjection`<br>`WACS.WASI.NN.OnnxRuntime` | wasi-nn typed surface<br>`Tensor` / `Graph` / `GraphExecutionContext` / `Error` impls + `WasiPreview2NNBundle` composite<br>ONNX backend (default; swap with `--bind` for other backends) |
| `--wasi-threads` | adds `WACS.WASI.Threads` | `wasi:thread-spawn` |
| Custom host binding | `--bind <Asm>` | Any `IBindable` host package (game APIs, custom imports, etc.) |

**Why both `WASI.NN` and `WASI.NN.DependencyInjection`?** Typed
resource interfaces (`ITensor`, `IGraph`, `IGraphExecutionContext`)
live in `Wacs.WASI.NN`. The SourceGen-shape impl classes that
direct-link emit instantiates via `Activator.CreateInstance(impl)
+ void Create(args)` live in the DI sibling. Without the DI
package on the load path, `[constructor]X` fails the resolver's
`TryFindResourceImpl` check, falls back to delegate dispatch, and
returns handle 0 to the guest.

`HostPackageResolver.TryFindResourceImpl` walks `HostPackages`
first, then falls back to AppDomain assemblies — so dynamically
loaded DI siblings (e.g. via
`WasiPreview2RuntimeScope.ReflectivelyAddWasiNN`'s `Assembly.Load`)
are still discoverable. The CLI lists the DI siblings explicitly
so the first-tier search is complete.

## How chaining works

A component that imports multiple WASI packages (e.g.
`wasi:cli/run` + `wasi:nn/inference`) needs:

1. **A single CLR `hostBundle` object** exposing every
   `[WitSource]` interface as a property. Direct-link IL
   reads them via `ResolveBundleProperty(bundleType, iface)`,
   which uses property type → property name fallback to find each
   interface. The bundle is the second ctor arg of the generated
   `ModuleClass`; the transpiler picks ONE bundle type at
   transpile time, so there has to be a single composite at
   instantiation time.
2. **A single `Resources` object** with the
   `(GetResource, AllocateResource)` convention. Every resource
   handle the component sees — `wasi:filesystem/descriptor`,
   `wasi:nn/tensor`, `wasi:nn/graph`, etc. — flows through this
   one registry, so `[method]X.foo(self)` lookups land on the
   right instance regardless of which subsystem minted the
   handle. (Round-13 of the wasi-nn arc collapsed two registries
   into one to fix this exact failure mode.)

`WasiPreview2NNBundle` (in `Wacs.WASI.NN.DependencyInjection`)
forwards both `WasiPreview2Bundle`'s and `WasiNNBundle`'s typed
properties through one CLR object. `HostPackageResolver`
auto-discovers it when both packages are on the AppDomain (via
`FindBundleType`'s three-tier search: explicit `HostPackages` →
AppDomain → `Assembly.Load` fallback).

This pattern extends to additional WASI subsystems — register a
new package's bundle as a property on a custom composite that
forwards both, and the resolver's
`ResolveBundleProperty(bundle, iface)` finds it by interface
type or name match. No emit changes needed.

## End-to-end example

A component that uses both `wasi:cli` (stdout, stdin, args) and
`wasi:nn` (load model, run inference) — the wasi-nn SLM shape:

### CLI

```bash
wacs run my-slm.component.wasm \
    --wasip2 \
    --wasi-nn \
    --native-memory \
    -d ./models::/models
```

What this does:

- Loads `WACS.WASI.Preview2` + `.DependencyInjection` (cli / io /
  fs / clocks / random)
- Loads `WACS.WASI.NN` + `.DependencyInjection` +
  `.OnnxRuntime` (typed surface + impl classes + ONNX backend)
- `WasiPreview2RuntimeScope` (under the hood) builds the DI
  graph, fires `Linker.Bind` for every subsystem, and resolves
  `WasiPreview2NNBundle` as the composite hostBundle for the
  transpiler
- `--native-memory` keeps linear memory in pinned native pages —
  recommended for large model bytes (avoids GC compaction)
- `-d ./models::/models` exposes the host's `./models` dir to
  the guest at `/models` (matching wasmtime's mount-pair syntax)

For other wasi-nn backends, swap the ONNX default with
`--bind`:

```bash
# ML.NET-flavored ORT wrapping
wacs run my.component.wasm --wasip2 --wasi-nn --bind Wacs.WASI.NN.MLNet

# LlamaSharp / GGUF
WACS_WASINN_GGUF_DIR=./models \
  wacs run my.component.wasm --wasip2 --wasi-nn --bind Wacs.WASI.NN.LlamaSharp
```

### Embedder (one-shot)

For programmatic embedding, `WasiPreview2RuntimeScope` does
exactly what the CLI does — and works equally for interpreted or
transpiled execution:

```csharp
using Wacs.ComponentModel.Runtime;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.DependencyInjection;

var runtime = new WasmRuntime();

// One scope wires every WASI package on the load path. Auto-
// detects WASI.NN.DependencyInjection and registers the
// composite WasiPreview2NNBundle. Auto-registers OnnxBackend
// if WASI.NN.OnnxRuntime is loadable.
using var wasi = new WasiPreview2RuntimeScope(
    runtime,
    preopens: new[] { ("./models", "/models") });

// Parse + transpile + instantiate the component.
using var fs = File.OpenRead("my-slm.component.wasm");
var component = ComponentBinaryParser.ParseComponent(fs);
var transpiler = new ComponentTranspiler(component, runtime);
var moduleClass = transpiler.Transpile();
var instance = Activator.CreateInstance(
    moduleClass,
    transpiler.Imports,
    wasi.Bundle,        // composite (Preview2 + NN forwarding)
    wasi.Resources)!;   // single registry for all resource handles

// Invoke the component's wasi:cli/run.run export, or any other
// public export the component declares.
var run = moduleClass.GetMethod("Run")!;
run.Invoke(instance, Array.Empty<object>());
```

Adding a new WASI capability — e.g. a custom `my:audio` package
that imports `wasi:cli/stdio` — follows the same shape:

```csharp
using var wasi = new WasiPreview2RuntimeScope(runtime,
    configure: services =>
    {
        // Standard DI extension on the IServiceCollection.
        services.AddMyAudioPackage();
    });
```

The bundle composition is a one-line property forward in your
custom bundle type; the resolver finds it through the same
`ResolveBundleProperty` walk used for the bundled WASI subsystems.

## Validation

Optional but recommended for production embeddings: validate the
runtime's bound functions against the WIT contract embedded in
the bindings assembly. Catches drift at link time before the
component runs.

```csharp
using Wacs.ComponentModel.Validation;

var contract = WitContract.FromAssembly(
    typeof(Wacs.WASI.Preview2.Cli.CliBindings).Assembly);
linker.Validate(contract);   // throws ValidationException on mismatch
```

`WitContract` reads the embedded WIT files from the bindings
assembly's resources — no need to ship the WIT files alongside
the component.

## Related docs

- [`Wacs.WASI.Preview2/Wacs.WASI.Preview2/README.md`](../Wacs.WASI/Wacs.WASI.Preview2/Wacs.WASI.Preview2/README.md)
  — manual subsystem wiring (no DI), per-impl override patterns,
  default impl table.
- [`Wacs.WASI.NN/Wacs.WASI.NN/README.md`](../Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN/README.md)
  — backend SPI, named-model resolver, the three backend
  packages.
- [`Wacs.ComponentModel/Wacs.ComponentModel/Validation/README.md`](../Wacs.ComponentModel/Wacs.ComponentModel/Validation/README.md)
  — WIT contract validation deep-dive.
- [`Wacs.Console/Wacs.Console/README.md`](../Wacs.Console/Wacs.Console/README.md)
  — CLI verb reference, all `--wasip2` / `--wasi-nn` /
  `--bind` flag combinations.
