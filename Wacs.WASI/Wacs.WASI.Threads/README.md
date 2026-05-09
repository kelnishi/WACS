# Wacs.WASI.Threads

`wasi-threads` proposal — guest-spawned threads against shared linear
memory. Single-project family; layered on top of the atomics +
`memory.atomic.{wait,notify}` instructions already in `Wacs.Core`.

## Contents

- **[Wacs.WASI.Threads/](Wacs.WASI.Threads/)** — `wasi:thread-spawn`
  host binding + the `IWasmThreadHost` glue that spawns OS threads
  against a shared `WasmRuntime`. Targets `netstandard2.1` so
  wasm-thread embedders on the .NET Framework / Mono surface stay
  supported. Tagged `[assembly: WasiHostPackage]` for
  `runtime.AutoDiscoverHostPackages()` discovery.

## Wiring

**CLI:**

```sh
wacs run my.wasm --wasi-threads
# Shorthand for `--bind Wacs.WASI.Threads`. Bundled with the CLI;
# resolves out-of-box.
```

**Embedder:**

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.Threads;

var runtime = new WasmRuntime();
runtime.UseWasiThreads();   // one-liner, returns the WasiThreads instance
```

The threads ABI is core-wasm (not component-model) — the import
flows through `WasmRuntime.BindHostFunction` directly. No DI bundle
or `[WitSource]` interfaces apply.

## Module contract

- Declare or import shared memory (no spec-level check; first atomic
  op traps if memory isn't shared).
- Export `wasi_thread_start (param i32 i32)` — the per-thread entry
  point invoked with `(tid, start_arg)`.
- Import `(wasi) (thread-spawn) (param i32) (result i32)` — call with
  `start_arg`; get back a positive tid, or a negative value on
  failure (no `wasi_thread_start` export, runtime not bound, tid
  counter exhausted, spawn machinery threw).
