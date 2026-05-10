# WACS.WASI.Threads

`wasi-threads` host adapter for the
[WACS WebAssembly runtime](https://github.com/kelnishi/WACS) — implements
`wasi:thread-spawn` on top of `Wacs.Core`'s built-in atomics + `wait`/`notify` operations.
Lets shared-memory wasm modules spawn worker threads against a real `System.Threading`
backend.

## Install

```bash
dotnet add package WACS.WASI.Threads
```

## Module requirements

The wasm module must:

1. Import or declare a **shared memory** (memory with the `shared` flag set).
2. Export `wasi_thread_start (param i32 i32)` — the worker entry point. WACS calls
   this with the thread ID (i32) and a user-supplied start argument (i32) per
   wasi-threads' spec.

## Quick start — interpreter / runtime extension

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.Threads;

var runtime = new WasmRuntime();
runtime.UseWasiThreads();   // wires wasi:thread-spawn
// (optionally chain other host packages: UseWasiPreview1 / UseWasiPreview2 / ...)

var module = BinaryModuleParser.ParseWasm(File.OpenRead("threaded.wasm"));
var inst = runtime.InstantiateModule(module);
runtime.RegisterModule("app", inst);

if (runtime.TryGetExportedFunction(("app", "_start"), out var addr))
    runtime.CreateInvokerAction(addr).Invoke();
```

## Quick start — CLI

```sh
wacs run threaded.wasm --wasi-threads
```

`--wasi-threads` is shorthand for `--bind Wacs.WASI.Threads`. The CLI verifies the module
declares shared memory and exports `wasi_thread_start` before instantiation; missing
either trips a clear startup error.

## What it provides

- **`WasiThreadsBindable : IBindable`** — discovers `WACS_WASINN_GGUF_DIR`-style host
  surface, wires `wasi:thread-spawn` against the shared runtime
- **`runtime.UseWasiThreads()`** — chained extension method
- **`[assembly: WasiHostPackage]`** marker so `runtime.AutoDiscoverHostPackages()` picks
  this assembly up automatically when it's loaded into the AppDomain

## Documentation

- Top-level [WACS README — Threads](https://github.com/kelnishi/WACS#webassembly-feature-extensions)
- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
  for runtime-requirements composition with other WASI host packages

## License

Apache-2.0
