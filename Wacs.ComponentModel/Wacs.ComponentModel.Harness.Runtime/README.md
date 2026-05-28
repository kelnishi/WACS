# WACS.ComponentModel.Harness.Runtime

AOT-safe runtime primitives that emitted typed WIT harnesses link
against at runtime. Pure C# — no reflection, no
`Reflection.Emit` — so every type here is reachable from harness
IL transpiled by NativeAOT or Unity IL2CPP.

Multi-targets `net8.0` (annotated `IsAotCompatible=true`) and
`netstandard2.1`.

## What's inside

- **`HarnessLoader.Load(byte[], Action<WasmRuntime>?, string)`** —
  the canonical-ABI boilerplate every emitted `{World}Harness.LoadFrom`
  funnels through: parse the `.component.wasm` wrapper (via
  [`WACS.ComponentModel.Parser`](https://www.nuget.org/packages/WACS.ComponentModel.Parser)),
  pull the first embedded core module, instantiate it on a fresh
  `WasmRuntime`, wire resource handle tables, return a
  `LoadedComponent` for the emitter to walk for typed bindings.
- **`MemoryHelpers`** — little-endian readers / writers over a
  `MemoryInstance`'s backing `byte[]`. Owns the LE-on-every-width
  detail so emitted harness IL just calls `ReadI32LE` /
  `WriteI32LE` / etc.
- **`StringCoding`** — canonical-ABI string lift+lower for
  UTF-8 / UTF-16 / Latin1. Isolated behind one helper so a
  future swap (e.g. `wasi-js-string-builtins` externref) lands
  in one place.
- **`HostHandleTable`** — host-side handle space for `own<R>` /
  resource handles that the guest hands to the host. Separate
  from the wasm-side rep table maintained by the runtime.
- **`Borrowed<T>`** — the lifetime-distinct view of a resource:
  `borrow<R>` references that the host received from wasm
  without participating in the host handle table. The struct
  is intentionally not `IDisposable` — code that took a borrow
  can't accidentally release it.
- **`WitContract` + `WitContractMismatchException`** — typed
  WIT model for runtime + compile-time validation; emitted
  harnesses embed the WIT source as `_WitContract` and the
  validator parses it back to diff against the component's
  embedded WIT custom section.
- **`WitResult`, `WitTupleAccess`** — helpers for the
  `Result<T,E>` and `tuple<...>` shapes emitted harness IL
  needs to construct + destructure.

## How it relates to the other WACS packages

```
.wit files
   │
   ▼  (build time)
WACS.ComponentModel.Harness.Lib   ──► {World}Harness.dll
   │                                       │
   │ references                            │ references
   ▼                                       ▼
WACS.ComponentModel.Parser           WACS.ComponentModel.Harness.Runtime  (this)
                                           │
                                           ▼
                                     WACS.Core (interpreter)
```

A consumer running an emitted harness only needs **this** package
plus [`WACS`](https://www.nuget.org/packages/WACS) at runtime —
the emitter (`Harness.Lib`) is a build-time tool, not a runtime
dependency.

| Package | Role |
|---|---|
| `WACS.ComponentModel.Harness.Runtime` | **this** — runtime primitives the emitted harness DLL calls into |
| [`WACS.ComponentModel.Harness.Lib`](https://www.nuget.org/packages/WACS.ComponentModel.Harness.Lib) | Build-time emitter — produces the `{World}Harness.dll` |
| [`WACS.ComponentModel.Parser`](https://www.nuget.org/packages/WACS.ComponentModel.Parser) | Used internally by `HarnessLoader.Load` to walk the `.component.wasm` |
| [`WACS`](https://www.nuget.org/packages/WACS) | Core interpreter; `HarnessLoader` instantiates the embedded core module on a `WasmRuntime` |
| [`WACS.Transpiler.Lib`](https://www.nuget.org/packages/WACS.Transpiler.Lib) | Alternative engine — emits a `{World}HarnessImpl : I{World}` against the same surface |

## Why a separate package

The emitter (`Harness.Lib`) needs `PersistedAssemblyBuilder` and
targets `net9.0`. The **runtime** the emitter's output links
against has no such requirement — it's pure
canonical-ABI helpers. Splitting them lets consumers ship the
emitted harness DLL alongside this small AOT-safe runtime, on
`net8.0` or `netstandard2.1`, without dragging the emitter into
the application image.

This is the same shape WACS uses elsewhere:
`WACS.Transpiler.Lib` vs `WACS.Transpiler.Runtime`,
`WACS.ComponentModel.Bindgen.Lib` vs the
`WACS.ComponentModel.Bindgen.SourceGen` analyzer.

## Reference

The cross-engine symmetry story (interpreter `{World}Harness` vs
transpiler `{World}HarnessImpl : I{World}`) is documented in
[`docs/WIT_HARNESS_APPROACH.md`](https://github.com/kelnishi/WACS/blob/main/docs/WIT_HARNESS_APPROACH.md).

## License

Apache-2.0
