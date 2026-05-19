# WASIp3 Phase 3 — Canon-async dispatcher closeout

Phase 3 of the [WASIp3 plan](#) shipped on the
`stack-switching` branch. This doc records what's in,
what's deferred, and where the seams are for the next
phase.

## What shipped

All commits live on `stack-switching`, 27 commits ahead of
`origin` at closeout. ComponentModel package walked
`0.7.1 → 0.8.17` under the one-minor-per-PR rule.

### Slice index

| Slice | Subject | Commit |
|---|---|---|
| A | Async primitives — `ComponentTask` / `ComponentSubtask` / `ComponentWaitableSet` / `AsyncHandleTable<T>` | `9a7b3e08` |
| B | Data plane — `StreamBuffer<T>` (bounded `Channel<T>`) + `FutureCell<T>` (TCS-backed) | `7fd113b6` |
| C | `AsyncDispatcher` contract + thread-safety docs | `18ed5e78` |
| D | Dispatcher state machine fill-in + canon-async index-counting fix in `ComponentInstance` | `3c9d2aae` |
| E | `CanonAsyncBinder` + `ComponentInstance` integration | `8ae4b5a1` |
| F | `AsyncLiftAdapter` + memory-touching dispatcher ops + producer/consumer test | `cdeabb67` |
| F+ | `WaitableSetWait` suspend bridge + typed primitive `task.return` / `context.*` bindings | `5a534aee` |
| G1 | `CanonAsyncAttribute` + reflective `CanonOpRegistry` | `3f276ce4` |
| G2 | Roslyn source generator replaces reflection in the registry | `e45c823d` |
| G2+ | `NameMangler.ToKebab` + parameterless `[CanonAsync]` | `ea57829a` |
| G3 | `ShimModuleRecognizer` — detection + name extraction | `d21d34ff` |
| G3+ | Structural shim fallback + stripped-names hard limit | `1b29b9ef` |
| H | Multi-core shim integration in `InstantiateMultiCore` | `4cfbdc85` |
| I.1 + I.2 + I.3 | string / list-of-primitive / option-of-primitive / result-of-primitive lift | `4f159f0e` |
| I.4 | tuple-of-same-primitive lift (arities 2–4) | `9fd8f7af` |

### Functional coverage

- **Dispatcher state**: current-task stack, task lifecycle
  (`TaskReturn` / `TaskCancel` / `TaskFail`), backpressure
  monotone counter, per-task sparse `context.get/set` slots,
  subtask cancel propagation, stream / future cancel-read /
  cancel-write, `WaitableSetWait` blocking via
  `Task.WaitAny` over member completion tasks,
  `WaitableSetPoll` synchronous scan.
- **Memory-touching dispatcher ops**: `StreamWriteFromMemory`,
  `StreamReadToMemory`, `ErrorContextNewFromMemory`,
  `ErrorContextDebugMessageToMemory`. Two-half drop
  semantics on streams (the table slot releases only when
  both halves drop).
- **Canon-async binder**: all 30 canon-async builtin op
  families recognized, typed delegates built for all
  flat-lowered single-segment wire shapes the spec exposes.
- **Source-generator name registry**: new
  `WACS.ComponentModel.Async.SourceGen` package
  (netstandard2.0 Roslyn `IIncrementalGenerator`) emits
  `CanonOpRegistry.g.cs` with the literal set of
  `[CanonAsync]`-decorated method names from
  `AsyncDispatcher`. Zero runtime reflection in the
  registry. Default attribute is parameterless — name
  auto-derives from method name via Pascal→kebab (matching
  `NameMangler.ToKebab`).
- **Shim-module recognizer** (`ShimModuleRecognizer`):
  detects wit-component's `"wit-component:shim"` module by
  name section *or* structural fallback (imports from `""`
  with all-digit names — strip-resistant).
  `BindShimImports` walks the shim's function-name custom
  section, pairs each shim function positionally with the
  matching canon-async entry from `component.Canons`,
  validates the kebab-normalized debug-name against the
  canon entry's op-kind, builds a typed delegate via
  `CanonAsyncBinder.TryBuildDelegateForEntry`, registers
  under `("", "<funcIdx>")`. Returns
  `BindResult { Bound, Mismatched, Skipped }` for caller
  diagnostics.
- **Multi-core integration**:
  `ComponentInstance.InstantiateMultiCore` toggles
  `ParseCustomNames`, scans non-primary core binaries for
  shim modules, allocates an `AsyncDispatcher` lazily on
  first shim hit, calls `BindShimImports`, sets
  `dispatcher.Memory` / `StringEncoding` / `Types`
  post-instantiation, then proceeds to primary-module
  instantiation.
- **Lift adapter** for `task.return` flat-lowered
  single-segment shapes:
  - `string` (UTF-8 / UTF-16 / Latin1+UTF-16 via
    `StringMarshal`).
  - `list<T>` for primitive T (`ListMarshal.LiftPrim<T>`
    closed generic) — T ∈ {u8/s8, u16/s16, u32/s32, u64/s64,
    f32, f64}.
  - `option<T>` for primitive T (1 disc + 1 payload → `T?`).
  - `result<T,E>` for primitive T/E with both sides same
    width (1 disc + 2 payloads →
    `(bool isOk, T ok, E err)` ValueTuple).
  - `tuple<T, T, ...>` for arity 2–4 with all elements
    same primitive type T (`ValueTuple<T, T, ...>` closed
    generic).

### Tests at closeout

- 552/552 ComponentModel + 833/833 Transpiler tests pass.
- New canon-async-specific test files (test counts):
  - `AsyncPrimitiveTests` — 11
  - `StreamBufferTests` — 10
  - `AsyncDispatcherTests` — 35
  - `CanonAsyncBinderTests` — 14
  - `AsyncLiftAdapterTests` — 24
  - `CanonOpRegistryTests` — 6
  - `ShimModuleRecognizerTests` — 21
  - `CanonAsyncBuiltinTests` — 16 (Phase 2 parser-level)
  - `AsyncDefTypeBinaryTests` — 7 (Phase 2 binary deftype)
  - `AsyncHandleMarshalTests` — 6 (Phase 2 marshal helpers)

## What's deferred

### Aggregate lift — paired with harness-emitter design

The shapes Phase 3 didn't tackle, each needs a
CLR-type-aware lifter that the interpreter would supply
reflectively and the AOT path would build via the
harness emitter:

- **`record { f1: T1, f2: T2, ... }`** — per-field
  recursive lift into a named CLR class.
- **`variant { c1(T1), c2(T2), ... }`** — joined-payload
  wire shape with discriminated-case lift.
- **Aggregate-payload `option<T>` / `result<T, E>`** —
  Slice I.3 covers primitive payloads only.
- **Mixed-width tuples** (`tuple<u32, u64>`, etc.) —
  combinatorial signature explosion; cleanest as an
  extensibility hook (`Dictionary<int typeIdx,
  Func<...> lifter>`) on `AsyncDispatcher` rather than
  enumerated cases.

The natural shape is an `AsyncDispatcher.AggregateLifters`
extensibility hook that the interpreter populates with
reflective lifters (annotated `[RequiresDynamicCode]` and
guarded behind a non-AOT branch) and the harness-emitter
populates with statically-emitted typed lifters at the
AOT path. Designing this end-to-end pairs with
`Wacs.ComponentModel.Harness.Lib`'s emit pipeline.

### Return-area indirection

Results that overflow the canon-ABI flat-count cap
(~16 i32s) flatten to a single `i32` pointer to a
return-area allocated via `cabi_realloc`. The export-side
`ComponentInstance.LiftStringRetArea` /
`LiftListRetArea` already handle this; the canon-async
lift adapter would need to recognize when a canon entry's
result has > flat-count threshold and switch delegate
shape (single i32 + memory read instead of N flat values).
Not yet implemented.

### Real `.component.wasm` validation

The recognizer + binder + dispatcher pipeline is
structurally complete on the read side. End-to-end
validation against an actual wit-component-emitted
canon-async component awaits Preview 3 RC stabilization
and the corresponding wit-component release. When a fixture
is available:

1. Place under `Spec.Test/components/fixtures/`.
2. Set `BinaryModuleParser.ParseCustomNames = true` before
   parsing.
3. Instantiate via `ComponentInstance.Instantiate` — the
   shim recognizer fires automatically.
4. Bind expected canon-op invocations; assert outcomes via
   the dispatcher's `Tasks` / `Streams` / etc. handle tables.

If wit-component's name convention diverges from WACS's
placeholder, override via
`CanonAsyncBinder.NameResolver` in one place.

### Upstream feedback

Draft issue body for `WebAssembly/component-model` lives
at [`docs/upstream-canon-async-import-convention.md`](upstream-canon-async-import-convention.md).
Proposes a wire-level import convention for canon-async
builtins so name-based host runtimes (WACS, jco's
host-bindings, etc.) survive name-section stripping. Not
yet filed — review checklist included.

## Architecture choices worth remembering

- **Task = Continuation** (Phase 1 cross-cutting constraint).
  `ComponentTask` wraps a Phase 1 `ContInstance` + a
  host-side `TaskCompletionSource<object?>`. Guest
  `suspend`/`resume` and host `await Task` share one
  substrate. JSPI-style host-promise patterns adopt the
  same shape transparently.
- **Strip-resistant shim detection** (Slice G3+). Structural
  fallback to "imports from `""` with all-digit names"
  survives `wasm-opt --strip-debug`. The function-name
  subsection is the hard limit — once stripped, the
  per-shim canon-op identity is unrecoverable.
- **No reflection-paths-live invariant** (Slice G1→G2). G1
  introduced reflection for the registry, G2 replaced it
  with a source generator in the immediately-following
  commit. Pattern for future "discover decorated members"
  needs: stand up reflective, immediately follow with
  source-gen, drop `[RequiresDynamicCode]` annotations.
- **One source of truth for canon-op names**. wasmtime's
  `Trampoline::symbol_name()` is the closest community-
  recognized spelling (`crates/environ/src/component/info.rs`
  in `bytecodealliance/wasmtime`). WACS adopts it verbatim
  via `[CanonAsync]` attributes — kebab-case matches
  wit-component's shim debug-name strings after dot→dash
  normalization.

## Phase 4 and beyond

Per the [WASIp3 plan](#), Phase 4 stands up the
`Wacs.WASI.Preview3` sibling package skeleton mirroring
the Preview 2 shape. Phase 3's canon-async surface is the
dependency that gated it; with closeout, Phase 4 is
unblocked modulo the aggregate-lift and `.component.wasm`
items above.
