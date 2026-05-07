# Wacs.WASI.Threads

`wasi-threads` proposal — guest-spawned threads against shared linear
memory. Single-project family; layered on top of the atomics +
`memory.atomic.{wait,notify}` instructions already in `Wacs.Core`.

## Contents

- **[Wacs.WASI.Threads/](Wacs.WASI.Threads/)** — `wasi_thread_spawn` host binding + the `IWasmThreadHost` glue that spawns OS threads against a shared `WasmRuntime`. Targets `netstandard2.1` so wasm-thread embedders on the .NET Framework / Mono surface stay supported.
