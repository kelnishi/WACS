# WACS.WASI.Preview2.DependencyInjection

`Microsoft.Extensions.DependencyInjection` extensions for
[`WACS.WASI.Preview2`](https://www.nuget.org/packages/WACS.WASI.Preview2). One-call
registration of every WASI 0.2.3 subsystem default + a pre-wired `Linker`.

Component-mode WASI Preview 2 hosts assemble a lot of moving pieces — typed `[WitSource]`
interfaces per subsystem, the resource-handle registry, the Linker that fires every
`*Bindings.BindToRuntime`, the composite bundle that exposes Preview 2 + WASI.NN through one
CLR object. This package wires it all in idiomatic .NET DI form.

## Install

```bash
dotnet add package WACS.WASI.Preview2.DependencyInjection
```

## Quick start — `WasiPreview2RuntimeScope` (one-shot embedder)

The simplest path: one disposable that owns the DI scope, fires every binding's
`BindToRuntime`, and resolves the composite bundle.

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.DependencyInjection;

var runtime = new WasmRuntime();
using var wasi = new WasiPreview2RuntimeScope(
    runtime,
    preopens: new[] { ("./models", "/models") });

// wasi.Bundle      → composite hostBundle
//                    (Preview2 + WASI.NN forwarding when the
//                     WASI.NN.DependencyInjection sibling is on
//                     the load path)
// wasi.Resources   → single resource registry across subsystems
// wasi.Runtime     → runtime, with every wasi:* binding wired

// ... transpile the component, instantiate, invoke ...
```

`WasiPreview2RuntimeScope` is what `wacs run --wasip2` uses internally. It auto-discovers
`Wacs.WASI.NN.DependencyInjection` when present and registers the
`WasiPreview2NNBundle` composite — no extra config when chaining wasi-nn alongside.

## Quick start — `IServiceCollection`

For ASP.NET, generic-host worker services, or anywhere with an existing DI container:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Wacs.WASI.Preview2.DependencyInjection;

var services = new ServiceCollection();
services.AddWasiPreview2();   // every WASI 0.2.3 subsystem default registered

// Override individual subsystems via DI's normal TryAdd semantics:
services.AddSingleton<IOutgoingHandler>(_ =>
    new HttpClientOutgoingHandler(new HttpClient { Timeout = ... }));
services.AddSingleton<IEnvironment>(_ =>
    new SandboxedEnvironment(envVars: ..., args: ..., cwd: ...));

using var sp = services.BuildServiceProvider();
using var scope = sp.CreateScope();

var linker = scope.ServiceProvider.GetRequiredService<Linker>();
// linker.Runtime is the WasmRuntime with every wasi:* import bound
```

`AddWasiPreview2(opts => opts.InstanceLifetime = ServiceLifetime.Scoped)` is the default —
fits ASP.NET request-scoped wasm execution. Pass `Transient` for per-call construction or
`Singleton` for single-component apps.

## Composing with WASI.NN

When [`WACS.WASI.NN.DependencyInjection`](https://www.nuget.org/packages/WACS.WASI.NN.DependencyInjection)
is on the load path, the runtime scope auto-discovers it and registers the
`WasiPreview2NNBundle` composite that forwards both Preview 2 and WASI.NN `[WitSource]`
interface properties through one CLR object. The transpiler's direct-link IL casts to the
composite type at the import call site; this package's auto-detection guarantees the
expected type is present without any extra config.

## What's included

- Default impls for every WASI 0.2.3 subsystem: `cli` / `clocks` / `filesystem` / `http`
  / `io` / `random` / `sockets`
- `WasiPreview2Bundle` (single-package) and `WasiPreview2NNBundle` (composite) bundle
  types
- `WasiPreview2Resources` — the canonical resource registry direct-link emit looks up
- `WasiPreview2RuntimeScope` — one-shot scope owning DI lifecycle, Linker resolution, and
  composite-bundle discovery

## Documentation

- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
  — runtime requirements, host-package composition, end-to-end CLI / embedder examples
- Sibling package [`WACS.WASI.Preview2`](https://www.nuget.org/packages/WACS.WASI.Preview2)'s
  README for manual subsystem wiring (no DI), per-impl override patterns

## License

Apache-2.0
