# WACS.Transpiler.Lib

Programmatic API for WACS's ahead-of-time WebAssembly → .NET IL transpiler. Reference this
package when you want to drive transpilation, loading, and dispatch of saved
`.dll`s **from inside a host process** — Unity / Godot embedders, ASP.NET workers, custom
toolchains, build pipelines.

For one-shot CLI use (`wacs build app.wasm -o app.dll`), install
[`WACS.Cli`](https://www.nuget.org/packages/WACS.Cli) instead. This package is the
library API behind that CLI.

## Install

```bash
dotnet add package WACS.Transpiler.Lib
```

## Quick start — in-process transpile + run

JIT-capable hosts (desktop, server, `dotnet run`, Godot Mono) can transpile and execute
in one step using `Reflection.Emit`. Roughly **64×** the WACS interpreter on
compute-bound workloads (~17 500 vs ~270 iter/s on CoreMark, M3 Max).

```csharp
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;

var runtime = new WasmRuntime();
runtime.BindHostFunction<Action<int>>(("env", "log"), v => Console.WriteLine(v));

using var fs = File.OpenRead("app.wasm");
var module = BinaryModuleParser.ParseWasm(fs);
var moduleInst = runtime.InstantiateModule(module);

var transpiler = new ModuleTranspiler("MyApp.Wasm", new TranspilerOptions
{
    Simd = SimdStrategy.HardwareIntrinsics,
});
var result = transpiler.Transpile(moduleInst, runtime, "WasmModule");

// result.ModuleClass is a fresh CLR Type. Construct + invoke through
// the generated IExports / IImports interfaces.
```

## Quick start — pre-compile + load (AOT-safe runtime)

For AOT-only targets that can't run `Reflection.Emit` (Unity IL2CPP, NativeAOT, iOS, Mono
AOT), pre-compile on a JIT host and ship the `.dll`:

```bash
# On the build machine:
wacs build app.wasm -o app.dll
```

Then load it at runtime via `TranspiledModuleLoader` — same throughput, no
`Reflection.Emit` dependency:

```csharp
using Wacs.Core.Runtime;
using Wacs.Transpiler.Hosting;

var runtime = new WasmRuntime();
runtime.BindHostFunction<Action<int>>(("env", "log"), v => Console.WriteLine(v));

var loader = new TranspiledModuleLoader();
var loaded = loader.Load("app.dll", runtime);
// loaded.Invoke / loaded.Exports give you the typed surface
```

## Component model

For component-mode wasm (the wasi-p2 / `.component.wasm` shape), pair this package with
[`WACS.ComponentModel`](https://www.nuget.org/packages/WACS.ComponentModel) and use
`ComponentTranspiler.TranspileSingleModule`:

```csharp
using Wacs.Transpiler.AOT.Component;

using var fs = File.OpenRead("app.component.wasm");
var result = ComponentTranspiler.TranspileSingleModule(
    fs,
    assemblyNamespace: "MyApp.Wasm",
    moduleName: "Module",
    options: new TranspilerOptions
    {
        // HostPackageResolver auto-built when null — walks HostPackages
        HostPackages = new[]
        {
            typeof(Wacs.WASI.Preview2.Cli.CliBindings).Assembly,
            typeof(Wacs.WASI.Preview2.DependencyInjection.WasiPreview2Bundle).Assembly,
        },
    },
    configureImports: rt =>
    {
        // Wire WASI Preview 2 + custom IBindable host packages
    });
```

For WASI Preview 2 host wiring, the natural pairing is
[`WACS.WASI.Preview2.DependencyInjection`](https://www.nuget.org/packages/WACS.WASI.Preview2.DependencyInjection)'s
`WasiPreview2RuntimeScope` — the same scope `wacs run --wasip2` uses internally.

## What's inside

- **`ModuleTranspiler`** — core wasm → CIL emitter. Walks parsed instruction stream,
  emits CIL into a `TypeBuilder`, runs `CilValidator` at emit time
- **`ComponentTranspiler`** — component-model variant; lifts/lowers canonical-ABI
  aggregates, integrates with `HostPackageResolver` for direct-link host imports
- **`TranspiledModuleLoader`** — load saved `.dll`s without `Reflection.Emit`. Suitable
  for AOT targets
- **`BindingLoader`** — `--bind` assembly resolution + `IBindable` activation
- **`HostPackageResolver`** — typed `[WitSource]` interface discovery + AppDomain
  fallback for impl-class lookups
- **Engine knobs** — `--simd {scalar | intrinsics | interpreter}`, `--no-tail-calls`,
  `--max-fn-size`, `--data-storage {compressed | raw | static}`,
  `EmissionTarget.{Auto | Standard | AotLinked}`

## Mixed-mode + cold-start

Transpilation is opportunistic: any function the transpiler declines (e.g., very large
bodies under `--max-fn-size`) falls back to the WACS interpreter for that function only,
so the module still runs.

Cold-start ranges from ~30–55 ms on a default `dotnet publish` to ~1 ms with
`PublishReadyToRun` + saved-`.dll` flow, and 501 µs cold for a fully-NativeAOT-published
consumer. Full breakdown in
[`docs/COLDSTART.md`](https://github.com/kelnishi/WACS/blob/main/docs/COLDSTART.md).

## Spec compliance

Spec-equivalent to the WACS interpreter on the WebAssembly 3.0 test suite (473/473),
verified continuously on macOS ARM64 and Linux x64.

## Documentation

- Top-level WACS [README — AOT Transpiler](https://github.com/kelnishi/WACS#aot-transpiler-wacstranspiler)
- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
  — component-mode runtime requirements + host-package composition
- [`docs/COLDSTART.md`](https://github.com/kelnishi/WACS/blob/main/docs/COLDSTART.md) —
  startup cost across runtime / publish-flag combinations
- [`Wacs.Transpiler.Lib/ARCHITECTURE.md`](https://github.com/kelnishi/WACS/blob/main/Wacs.Transpiler/Wacs.Transpiler.Lib/ARCHITECTURE.md)
  — internal pipeline / design deep-dive

## License

Apache-2.0
