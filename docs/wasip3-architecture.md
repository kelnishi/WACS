# WASI Preview 3 — architecture & coverage

This doc captures the end-to-end layering of WACS's WASI
Preview 3 stack as of `WACS.WASI.Preview3 0.2.2` /
`WACS.ComponentModel 0.10.2` /
`WACS.ComponentModel.Async.SourceGen 0.4.24` /
`WACS.ComponentModel.Harness.Runtime 0.7.4`. WASIp3 sits on
top of the [Component Model async ABI][cm-async], which in
turn sits on top of the [Stack Switching proposal][prop]
documented in [stack-switching-architecture.md](./stack-switching-architecture.md).

[cm-async]: https://github.com/WebAssembly/component-model/blob/main/design/mvp/Async.md
[prop]: https://github.com/WebAssembly/stack-switching

## Layered stack

```
┌─────────────────────────────────────────────────────────────────────┐
│ AOT spike  Wacs.WASI.Preview3.AotSpike/Program.cs                   │
│ Whole-program PublishAot=true binary that drives                    │
│ ComponentInstance.InstantiateAot end-to-end on run-with-err.wasm    │
│ — 14 MB native binary, no reflection, proves the dispatcher path    │
│ is AOT-safe.                                                        │
└────────────────────────────────┬────────────────────────────────────┘
                                 │ instantiate + invoke
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│ WASI Preview 3 host  Wacs.WASI.Preview3/                            │
│                                                                     │
│   WasiPreview3Host (IBindable composite)                            │
│   ├─ Cli       (run / get-stdin / get-stdout / get-stderr)          │
│   ├─ Clocks    (monotonic-clock / wall-clock)                       │
│   ├─ Filesystem (preopens / types)                                  │
│   ├─ Http      (incoming-handler / outgoing-handler / types)        │
│   ├─ Io        (streams via System.IO.Stream <-> stream<u8> bridge) │
│   ├─ Random    (insecure / insecure-seed / random)                  │
│   ├─ Resources (handle tables for the typed resource imports)       │
│   └─ Sockets   (tcp / udp / ip-name-lookup, 10/10 fixtures pass)    │
│                                                                     │
│   wit/ — vendored 0.3.0-rc-2026-03-15 WIT definitions               │
│                                                                     │
│   Wacs.WASI.Preview3.DependencyInjection — IServiceCollection       │
│   fluent builder                                                    │
└────────────────────────────────┬────────────────────────────────────┘
                                 │ canon-async ops
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Component Model async runtime  Wacs.ComponentModel/Async/           │
│                                                                     │
│   AsyncDispatcher (1678 LOC) — one instance per ComponentInstance   │
│     ├─ Shared handle store: Task / Subtask / WaitableSet /          │
│     │   Stream / Future / ErrorContext all in one int → kind+payload│
│     │   table, with per-kind AsyncHandleTable<T> facades            │
│     ├─ Current-task stack + per-task sparse context.* slots         │
│     ├─ Memory-touching ops: StreamWriteFromMemory /                 │
│     │   StreamReadToMemory / ErrorContextNewFromMemory /            │
│     │   ErrorContextDebugMessageToMemory                            │
│     ├─ Callback-driven lift loop (Driver suspends → CLR awaits →   │
│     │   resumes on TCS completion via the WaitableSetWait suspend   │
│     │   bridge)                                                     │
│     └─ Backpressure monotone counter + waitable-set sema            │
│                                                                     │
│   CanonAsyncBinder (1183 LOC) — every spec canon-async builtin      │
│     resolved to a typed delegate. Flat lowering walks the           │
│     canon-options + wire shape; bound delegates wrap the dispatcher │
│     methods so interpreter + transpiler both invoke the same        │
│     signatures.                                                     │
│                                                                     │
│   ShimModuleRecognizer (521 LOC) — recognizes wit-component's       │
│     emitted "indirect-MOD-METHOD" shim modules from the function-   │
│     name section + the component-level alias map; falls back to a   │
│     structural recognizer when names are stripped.                  │
│                                                                     │
│   AsyncLiftAdapter — wraps each async lift body in a continuation;  │
│     converts task.return / task.cancel / task.fail into             │
│     TaskCompletionSource.{SetResult, SetCanceled, SetException}.    │
│                                                                     │
│   CanonOpRegistry (g.cs) — Roslyn source-generated static name set  │
│     scanned from [CanonAsync] on AsyncDispatcher; replaces the      │
│     Slice-G1 reflection path → AOT-safe.                            │
│                                                                     │
│   StreamBuffer<T> — bounded Channel<T> producer/consumer            │
│   FutureCell<T>  — single-shot TaskCompletionSource cell            │
│   ComponentTask / ComponentSubtask / ComponentWaitableSet           │
│     — handle-table payload objects                                  │
└────────────────────────────────┬────────────────────────────────────┘
                                 │ IContinuationContext
                                 │ suspend / resume
                                 ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Stack switching runtime  Wacs.Core/Runtime/Concurrency/             │
│ See stack-switching-architecture.md.                                │
└─────────────────────────────────────────────────────────────────────┘
```

## Async dispatcher — the central API

`Wacs.ComponentModel/Wacs.ComponentModel/Async/AsyncDispatcher.cs`
is the engine-agnostic surface for every canon-async builtin.
One dispatcher lives per `ComponentInstance`; the interpreter
dispatches canon entries directly through these methods, and
the transpiler emits `callvirt` against the same methods —
symmetric API per `feedback_symmetric_engines`.

### Handle space

All six waitable kinds share one integer namespace, so a single
canon-async handle never resolves to two different objects.
Per-kind facades (`AsyncHandleTable<ComponentTask>`,
`AsyncHandleTable<StreamBuffer<...>>`, etc.) reject
cross-kind lookups by checking the `WaitableKind` tag stored
alongside each `HandleEntry`. Dropped handles return to a
single freelist shared across kinds.

### Built-in ops

The dispatcher implements every canon-async builtin op family:

| Family               | Examples                                           |
|----------------------|----------------------------------------------------|
| Task lifecycle       | `task.return`, `task.cancel`, `task.fail`          |
| Subtasks             | `subtask.cancel`, `subtask.drop`                   |
| Waitable sets        | `waitable.join`, `waitable-set.{new,wait,poll}`    |
| Streams (data plane) | `stream.{new,read,write,cancel-read,cancel-write,close-readable,close-writable}` |
| Futures              | `future.{new,read,write,cancel-read,cancel-write}` |
| Error contexts       | `error-context.{new,debug-message,drop}`           |
| Backpressure         | `backpressure.set`                                 |
| Per-task context     | `context.get`, `context.set` (sparse slot map)     |
| Yield / scheduling   | `thread.yield`                                     |
| Memory-touching      | `stream.read-to-memory`, `stream.write-from-memory`, `error-context.new-from-memory`, `error-context.debug-message-to-memory` |

Each is marked `[CanonAsync]`; the source generator scans
these and emits `CanonOpRegistry.g.cs` so the binder can ask
"is this canon name a dispatcher op?" without reflection.

### Suspend bridge

`WaitableSetWait` is the canonical blocking operation. When
a guest task awaits a waitable set, the dispatcher:

1. Builds a `Task.WaitAny` over each member's completion task
   (set element `ComponentTask.CompletionSource.Task`, future
   readiness Task, stream availability Task).
2. The wasm body issues `suspend` on a dispatcher-allocated
   tag; the suspension throws `SuspensionException` back to
   the AsyncLiftAdapter's handler arm.
3. The handler arm `await`s the WaitAny; on completion it
   schedules a `resume` back into the suspended continuation
   with the index of the firing waitable.

This is the load-bearing mechanism that lets idiomatic CLR
`async`/`await` host code receive `Task<T>` results from wasm
component tasks (per `feedback_jspi_on_cm_async`).

## Canon binder — wiring the dispatcher to wasm

`Wacs.ComponentModel/Wacs.ComponentModel/Async/CanonAsyncBinder.cs`
turns the canon-options + wire signature for a builtin into a
typed `IDelegateRef`:

- Looks up the op name in `CanonOpRegistry` (gen'd from
  `[CanonAsync]`).
- Reads the canon options (memory + realloc + post-return).
- Materializes the dispatcher delegate via a per-op typed
  builder; the delegate matches the flat-lowered wire shape
  wasm sees.
- Stamps the result into the component-instance's typed
  delegate table; subsequent `call`s find it without
  re-binding.

Coverage at 0.10.2: **all 30 canon-async op families** plus
their flat-lowering variants for single-segment wire shapes.
Multi-segment aggregates (records with options inside lists
inside tuples, etc.) walk through the typed builders too —
the binder reuses the harness-emit pipeline rather than
duplicating canon-ABI marshal logic.

## Shim module recognizer

`Wacs.ComponentModel/Wacs.ComponentModel/Async/ShimModuleRecognizer.cs`
handles the `wit-component`-emitted shim module that wraps
each async import. It:

1. Walks the shim's function-name section for
   `indirect-MOD-METHOD` entries.
2. Cross-references the **component-level alias map** in the
   parent component
   (`alias core export $shim-instance "<N>" (core func ...)`)
   to recover the qualified import name when the shim's
   function-name section is stripped — per
   `project_wit_component_shim_slot_map.md`, the slot→name
   correspondence lives in the component aliases, not in the
   shim module itself.
3. Falls back to a structural recognizer (matching the shim's
   `call_indirect`-based body shape) when name sections are
   absent entirely — necessary for `--strip` builds.

The recognizer is what lets the Preview3 host bind to a guest's
async imports without the guest carrying any host-specific
metadata.

## Source-generator-driven harness

[WACS_ComponentModel_Async_SourceGen 0.4.24][srcgen] emits the
typed `[AsyncComponentHarness]` partial-class bodies that
consumer code calls. It covers nearly the full canon-ABI
surface — see [the surface memory at
`project_async_sourcegen_surface.md`][surface] for the full
shape catalogue. The output:

- Lazy-resolves the underlying core exports and `cabi_realloc`
  / `cabi_post_<name>` cleanup functions.
- Lifts and lowers strings, primitive arrays, options,
  results, tuples, records, lists, nested records, lists of
  records (with primitive / ptr/len / option / result /
  nested-record / list fields), mixed primitive ↔ ptr/len
  result arms (including 8-byte primitives with i64 slot
  widening), and `string[][]` / `T[][]` in option/result arms.
- Is AOT-safe — no runtime `Type.MakeGenericType`, no
  reflection-driven lookup.

[srcgen]: ../Wacs.ComponentModel/Wacs.ComponentModel.Async.SourceGen/
[surface]: ../.claude/projects/-Users-kelvinnishikawa-wasm-WACS/memory/project_async_sourcegen_surface.md

## WASI Preview 3 host packages

`Wacs.WASI/Wacs.WASI.Preview3/` ships the host bindings for
the 6 WASIp3 worlds. Layout:

```
Wacs.WASI.Preview3/                — host bindings
Wacs.WASI.Preview3.DependencyInjection/ — IServiceCollection fluent builder
Wacs.WASI.Preview3.Test/           — fixture-driven xUnit suite
Wacs.WASI.Preview3.AotSpike/       — whole-program AOT proof
```

Each WIT world has its own `IBindable` implementation under
the matching subdirectory (`Cli`, `Clocks`, `Filesystem`,
`Http`, `Io`, `Random`, `Resources`, `Sockets`). `wit/`
vendors the `0.3.0-rc-2026-03-15` IDL definitions; the host
package re-emits them on build via the wit-harness
infrastructure documented in
[WIT_HARNESS_APPROACH.md](./WIT_HARNESS_APPROACH.md).

### Fixture coverage (Preview3.Test)

WASIp3 fixtures live at
`Spec.Test/wasi/tests/rust/wasm32-wasip3/`, executed by the
test project. Per the project memory at closeout (0.2.2):

- **sockets**: 10 / 10 fixtures pass — closes the sockets
  arc started in 0.1.71 / 0.1.72.
- **http**: full coverage of fields, request, response,
  service shapes (0.1.65 — 0.1.70).
- **filesystem**: preopens, types, hard-link / symlink-at,
  rename-at, get-flags / is-same-object, fd-survives-unlink
  (0.1.58 — 0.1.64).
- **cli**: run + std{in,out,err} + the stream<u8> bridge to
  host `System.IO.Stream` backings (Phase 4 close).
- **clocks** / **random**: passing.

A handful of fixtures stay xfail behind opt-in capability
gates — see the WASIp1→p3 migration notes at
[MIGRATION_WASIp1_to_WASI.md](./MIGRATION_WASIp1_to_WASI.md).

## NativeAOT end-to-end

`Wacs.WASI.Preview3.AotSpike/` is the whole-program
`PublishAot=true` proof that the dispatcher path is reachable
from AOT-safe consumer code:

- 14 MB self-contained native binary.
- Drives `ComponentInstance.InstantiateAot` (the AOT entry
  point, distinct from reflective `Invoke`) end-to-end on
  the minimal `run-with-err.wasm` fixture.
- Single-poll body returning `Err(())`.

The spike confirms three pieces are AOT-compatible: the
`CanonOpRegistry` source-gen replacement for the slice-G1
reflection registry; the typed `IDelegateRef` cache built by
`CanonAsyncBinder`; and the `AsyncLiftAdapter`'s suspend
bridge against `StackSwitchingHelpers`.

`feedback_aot_requirement` is the hard rule: no runtime
reflection / `Reflection.Emit`. Verification is per-phase via
`PublishAot=true` plus `AotAcceptanceTests` (gated by
`WACS_AOT_TEST=1`).

## Cross-cutting design

- **Engine symmetry** — interpreter and transpiler expose
  identical surface. Same `AsyncDispatcher` methods, same
  `IContinuationContext` impl, same binder. Engine choice is
  deployment, not API (`feedback_symmetric_engines`).
- **Stack Switching first** — the dispatcher is built atop
  Stack Switching's `ContInstance` rather than a callback-
  only model, so `task.return` / `subtask.cancel` /
  `waitable.join` are thin wrappers over `resume` / `suspend`
  rather than ad-hoc state-machine code (`feedback_stack_switching_first`).
- **JSPI rides CM async** — single shared dispatcher across
  Stack Switching / CM async / JSPI-style patterns; host
  surface uses `Task<T>` (`feedback_jspi_on_cm_async`).
- **Permissive-stub exit caveat** — first hypothesis for an
  apparent fixture hang is a guest `assert!`/`exit(1)` being
  swallowed by an overly-permissive default impl, NOT
  canon-async (`feedback_wasip3_permissive_stub_exit_spins`).

## Versioning at a glance

| Package                                       | Current |
|-----------------------------------------------|---------|
| `WACS.ComponentModel`                         | 0.10.2  |
| `WACS.ComponentModel.Parser`                  | (tracked w/ CM) |
| `WACS.ComponentModel.Harness.Lib`             | 0.27.1  |
| `WACS.ComponentModel.Harness.Runtime`         | 0.7.4   |
| `WACS.ComponentModel.Async.SourceGen`         | 0.4.24  |
| `WACS.WASI.Preview3`                          | 0.2.2   |
| `WACS.WASI.Preview3.DependencyInjection`      | 0.2.2   |

Family tag: `WACS-WASI-Preview3-v*` per
`feedback_release_versioning` — every PR touching a package
bumps its csproj point version + a CHANGELOG entry naming
the package.

## What's deferred

- **Wider async fixture coverage** — beyond the sockets +
  http + filesystem + cli arcs already in
  `Spec.Test/wasi/tests/rust/wasm32-wasip3/`, additional
  proposal fixtures land as Preview 3 RC stabilizes.
- **Composition tests** — chaining two component instances
  together (one component's async export feeds another's
  async import) is in the
  [COMPONENT_CHAINING.md](./COMPONENT_CHAINING.md) plan but
  hasn't shipped end-to-end async chaining yet.
- **Standalone-mode resume** — see
  [stack-switching-architecture.md](./stack-switching-architecture.md);
  not on the critical path for WASIp3 since the CM runtime
  always provides an `ExecContext`.

## Related docs

- [stack-switching-architecture.md](./stack-switching-architecture.md)
  — the substrate.
- [WIT_HARNESS_APPROACH.md](./WIT_HARNESS_APPROACH.md) — how
  generated C# bindings call into the dispatcher.
- `Wacs.Core/Wacs.Core/Runtime/CanonResourceBinder.cs` +
  `ResourceHandleTable.cs` — the Phase-0 resource-table
  machinery that streams / futures / error-contexts share
  for handle allocation. Wired into `ComponentInstance`
  (`_resourceTables` field) and into the transpiler CLI
  via the same binder.
