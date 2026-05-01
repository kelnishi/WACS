# Changelog

## [WACS.Cli 1.2.0 + WACS.WASI.Preview1 0.11.0 + WACS.HostBindings.* 0.1.0] — `wacs aot` end-to-end + WASI rename

A wasm input is now one CLI call away from a self-contained NativeAOT
native binary:

```bash
wacs aot app.wasm -o app                          # compute-only
wacs aot coremark.wasm --wasi -o coremark         # WASI Preview 1
wacs aot app.component.wasm --wasip2 -o app       # WASI Preview 2
```

Internally `wacs aot` transpiles the wasm to a stable-named .dll,
scaffolds a throwaway consumer csproj with the right reference set
(WACS runtime + the new `WACS.HostBindings.*` source-generated
adapter for WASI), and runs `dotnet publish -p:PublishAot=true`. The
final native binary is copied to the requested output path and the
temp directory is removed (unless `--keep-temp`). No JIT, no
`Reflection.Emit`, no `Assembly.Load`, no `MethodInfo.Invoke` at run
time.

### New: `WACS.HostBindings.*` packages

- **`WACS.HostBindings.Abstractions`** — the `[WacsImport]` /
  `[WacsImportNames]` / `[WacsTranspiledImports]` attributes that mark
  static methods as wasm import bindings. Tiny, attribute-only, AOT-
  trim safe. Both `WACS.WASI.Preview1` and `WACS.WASI.Preview2`
  reference it to annotate their host functions.
- **`WACS.HostBindings.SourceGen`** — a Roslyn incremental source
  generator that, at consumer-build time, scans the assembly's
  `[assembly: WacsTranspiledImports("Ns.IImports")]` reference and
  emits an `IImports` adapter that wires the transpiled wasm's
  imports straight to the `[WacsImport]`-annotated statics. No
  reflection, no DispatchProxy, no runtime IL emission — pure
  source-gen, fully NativeAOT-compatible.
- **`Wacs.WASI.Preview1`** — every host function gets an
  ExecContext-free static entry-point variant alongside the existing
  instance method, so the source generator can wire them in directly.
  Behavior unchanged for embedders using the instance API.
- **`Wacs.WASI.Preview2`** — same treatment for the Component-Model
  hosts, including the existing `WasiPreview2Bundle` DI registration.

### AotLinked emission

`TranspilerOptions.Emission = EmissionTarget.AotLinked` skips the
codec wrapper that normally bridges the saved-to-static-reference
path's empty in-process registry. Direct `new ThinContext(...)` from
inlined IL constants instead. Now covers memories + active data
segments, globals (primitive inits), tables, and active element
segments — i.e. enough to run real wasm modules. Trimmer evidence:
the `__WACSInit` codec holder type is not present in the persisted
.dll's bytes. ~22% binary-size reduction on small modules; larger on
data-segment-heavy ones.

### `WACS.WASIp1` renamed → `WACS.WASI.Preview1`

The `WACS.WASIp1` package has been renamed to `WACS.WASI.Preview1` to
make room for `WACS.WASI.Preview2` (and eventually `.Preview3`) under
a single, consistent prefix. The shipped behavior is identical — same
types, same methods, same conformance posture against
`wasi-testsuite`.

The old `WACS.WASIp1` package id is now a **metapackage**: it
transitively pulls in `WACS.WASI.Preview1`, so existing
`<PackageReference Include="WACS.WASIp1" />` entries continue to
restore. C# `TypeForwardedTo` cannot bridge a namespace rename, so
consumer source code must update `using Wacs.WASIp1;` to
`using Wacs.WASI.Preview1;` (one-shot sed). The metapackage emits a
build-time warning (`WACS_WASIp1_DEPRECATED`) pointing at the
migration guide; suppress with
`<SuppressWacsWasip1DeprecationWarning>true</…>` while you migrate.

The `Wacs.Core.WASIp1` namespace inside `Wacs.Core` (`IBindable`,
`ErrNo`, `SystemExitException`, etc.) is **not** renamed. Those
types are interpreter-wiring conventions, not WASI host code.

See [`docs/MIGRATION_WASIp1_to_WASI.md`](docs/MIGRATION_WASIp1_to_WASI.md)
for the full migration guide and the sed one-liner.

## [WACS.WASIp1 0.10.0] — wasi-testsuite integration + correctness pass

Wires the dormant `Spec.Test/wasi` submodule (now pinned to
`prod/testsuite-base` for the prebuilt fixtures) into a new
`Wacs.WASIp1.Test` xUnit project that runs as part of `dotnet test`
in CI. **43 of 72 conformance fixtures pass** at HEAD; the rest are
in `Wacs.WASIp1.Test/skip.json` with documented Phase-4 follow-ups.

Sockets are no longer stubbed — the four `sock_*` host functions are
implemented over `System.Net.Sockets.Socket`, gated on a default-off
`AllowNetworkSockets` flag plus the requirement that the embedder
hand WACS pre-bound, pre-listening sockets via the new
`PreopenedSockets` config list. WASI Preview 1 has no `sock_open` /
`sock_bind` / `sock_listen`, so this is the same model `wasmtime
serve` uses for HTTP.

### Bug fixes

- `fd_seek` no longer truncates `*newoffset` to 32 bits — it's a u64
  in the spec and was overflowing on files >2 GB and silently
  corrupting the upper 4 bytes of the slot even on small files.
- `fd_prestat_get` / `fd_prestat_dir_name` strip the internal
  leading `/` from the directory name and report exactly
  `pr_name_len` bytes (no nul terminator). Matches what
  wasi-libc's `open_scratch_directory` expects, and was responsible
  for ~80% of the conformance fixtures' baseline failures.
- `fd_pread` / `fd_pwrite` / `fd_advise` / `fd_allocate` /
  `fd_filestat_set_size` accept their `filesize` (u64) arguments as
  `long` in the binding signatures and cast inside, since the binding
  dispatcher can't auto-coerce `wasm i64 → System.Int64 →
  System.UInt64`.
- `poll_oneoff` clock subscriptions correctly compute "now" per the
  subscription's `clock_id` in nanoseconds (was mixing .NET 100 ns
  ticks with the guest's nanoseconds, breaking absolute timeouts
  outright); `clock_id` is now actually consulted; write-readiness no
  longer uses the inverted `Position < Length` gate.
- `fd_filestat_set_times` / `path_filestat_set_times` reject
  `(ATIM | ATIM_NOW)` and `(MTIM | MTIM_NOW)` flag combinations per
  spec instead of silently letting NOW override the explicit value.
- `path_filestat_get` honors `LookupFlags.SymlinkFollow` — without
  the flag set, it reports `SYMBOLIC_LINK` for symlinks instead of
  resolving through them. Required bypassing the path mapper for the
  leaf component (the mapper resolves symlinks for sandbox safety,
  which is correct everywhere except `lstat`).

### New / lifted features

- `path_link` and `path_symlink` are real implementations (P/Invoke
  `link(2)` + `CreateHardLinkW` for hard links;
  `File.CreateSymbolicLink` for symbolic). Both gated on the
  matching `WasiConfiguration.AllowHardLinks` /
  `AllowSymbolicLinks` flags (still default-off).
- `fd_fdstat_set_flags` validates against known `FdFlags` bits and
  stores them on the `FileDescriptor`; `Append` is honored by
  `fd_write` (seek-to-end before write); the others are advisory.
- `fd_fdstat_set_rights` enforces "can only remove rights" per
  spec (returns `NotCapable` on any privilege escalation request);
  `fd_read` / `fd_write` / `fd_pread` / `fd_pwrite` enforce the
  resulting rights bits.

### New configuration knobs

- `WasiConfiguration.AllowNetworkSockets` (default `false`) +
  `PreopenedSockets` list.
- `WasiConfiguration.PreopenHostRootDirectory` (default `true` for
  back-compat with the `Wacs.Console` "fd 3 = cwd" model). Flip
  false to follow the wasmtime convention where fd 3 is the first
  explicit preopen.

### Other

- `FileDescriptor` gains `Flags`, `Socket`, and `IsListening` fields
  (used by the above).
- New `Wacs.WASIp1.SocketStream` — a `Stream` wrapper over a
  `Socket` so the existing `fd_read` / `fd_write` iovec paths work
  unchanged on connected sockets.
- New optional Python adapter at `Spec.Test/wasi-adapters/wacs.py`
  for users who want to run the upstream `wasi-testsuite` Python
  harness against an installed `wacs` global tool.

## [WACS.Cli 1.1.0] — `wacs bindgen` verb

Rolls binding generation into the unified `wacs` tool as a fourth
verb, sequenced before any tag push so users only ever see the
unified surface. Symmetric with the `wasm-transpile → wacs`
consolidation that landed in 0.10.0: one CLI, verb-based, smart
auto-detect.

```bash
wacs bindgen ./wit -o ./gen/        # forward: WIT directory → C# bindings
wacs bindgen ./wit/foo.wit -o ./gen/ # forward: single .wit file
wacs bindgen ./app.dll -o ./regen/  # reverse: regenerate from a transpiled .dll
```

Direction inferred from input shape — `.dll` triggers reverse,
`.wit` is forward single-file, a directory is forward tree (with
`deps/` recursion).

The previously-staged-but-never-published
`WACS.ComponentModel.Bindgen` package + its `wit-bindgen-wacs`
CLI are deleted entirely. The `Wacs.ComponentModel.Bindgen/`
project + the `nuget.yml` workflow's matrix entry would never
have been useful — there are no consumers to migrate, and
shipping a brand-new package alongside its replacement would
have created confusion in the NuGet listing.

`WACS.ComponentModel.Bindgen.Lib` (programmatic surface) is
unaffected — source generators and build-time integrations
keep referencing it directly. `wacs bindgen` is itself a thin
wrapper around the same Lib API.

## [0.10.0] — Component Model

The Component Model release. Adds WebAssembly Component Model
support across the toolchain — six new packages, two existing
packages bumped, and the unified `wacs` CLI replaces the legacy
`wasm-transpile` tool. Single PR; commit-by-commit detail in the
git history (`git log v0.9.1..v0.10.0`).

**New packages.**

- **`WACS.ComponentModel 0.1.0`** — pure-C# parser, decoder, and
  interpreter for WebAssembly components. WIT text parsing, full
  canonical-ABI lift/lower (string / list / option / result /
  variant / record / tuple / resource handles), `ComponentInstance`
  for end-to-end instantiation against a `WasmRuntime`, and
  `ComponentBridge` adapters for cross-engine composition
  (interpreter components consumed as transpiler-side host bundles
  via `DispatchProxy`, and the inverse direction binding typed
  exports as host functions).
- **`WACS.ComponentModel.Bindgen 0.1.0`** — `wit-bindgen-csharp`
  CLI that emits `[WitSource]`-tagged C# interfaces from a WIT
  package directory.
- **`WACS.ComponentModel.Bindgen.Lib 0.1.0`** — programmatic
  surface for the same emitter (used by source generators and
  build-time integrations).
- **`WACS.WASI.Preview2 0.1.0`** — typed C# interfaces + default
  implementations for the 25 WASI Preview 2 host packages
  (cli/clocks/filesystem/http/io/random/sockets), backed by
  `Wacs.ComponentModel`. Includes resource-table state via
  `ResourceContext` so handles allocated by one interface
  (`IStdout.GetStdout` returning `own<output-stream>`) resolve back
  through another's instance methods.
- **`WACS.WASI.Preview2.DependencyInjection 0.1.0`** —
  `Microsoft.Extensions.DependencyInjection` extension that
  registers the full Preview 2 surface plus a `WasiPreview2Bundle`
  aggregate the transpiler's direct-linked path consumes.
- **`WACS.Cli 1.0.0`** — unified `wacs` global tool that
  supersedes `wasm-transpile`. Verb-based subcommand layout
  (`wacs run` / `build` / `inspect`) matching `wasmtime` / `wasmer`
  precedent. Direct-run shortcut (`wacs my.wasm` defaults to `run`),
  smart component-vs-core auto-detect, multi-input ModuleLinker
  composition, full instrumentation surface inherited from the
  legacy `Wacs.Console` (gas, profile, instr-logging, super,
  switch).

**Bumped packages.**

- **`WACS 0.9.1 → 0.10.0`** — `WasmRuntime` gains two methods
  (`EnumerateBoundEntities`, `TryGetBoundHostFunctionType`) used
  by the component-model validation layer. Removes legacy
  `Wacs.Core/Components/` prototypes (replaced by
  `Wacs.ComponentModel`).
- **`WACS.Transpiler.Lib 0.3.0 → 0.4.0`** — major feature lands:
  - `ComponentTranspiler` for component-mode AOT transpilation
    (single-core + multi-core via primary canon-lift detection).
  - `ModuleLinker` cross-module composition for multi-input runs.
  - `MainEntryEmitter` + `ComponentMainEntryEmitter` for
    `--emit-main` output.
  - `DirectLinkedImportEmit`: inline IL through typed host bundles
    (no delegate hop) for every canon-ABI shape: primitives,
    string (utf8/utf16/latin1), `list<T>`, `option<T>`, `Result<T,E>`
    (including `Result<Unit, Variant>`), records, variants with
    payload-bearing cases, resource handles. Resource INSTANCE
    methods returning aggregates work end-to-end.
  - `ExportInterfaceEmit`: `[WitSource]`-tagged `I{Iface}` types
    emitted into transpiled `.dll`s, so a transpiled component
    serves as a host package for downstream transpiles
    (chain mode).
  - `WitContract.FromAssembly` two-path: embedded WIT first,
    fallback to `[WitSource]`-tagged interfaces for
    transpiled-output round-trip.
  - 1300+ new tests (`Wacs.Transpiler.Test`, `Wacs.ComponentModel.Test`,
    `Wacs.WASI.Preview2.Test`).

**Deprecated.**

- **`WACS.Transpiler 0.3.0 → 0.3.1`** — `wasm-transpile` is
  superseded by `wacs`. Every flag still works; every invocation
  prints a stderr deprecation banner pointing at the migration.
  `<PackageDeprecationReason>` baked into the package metadata.
  See the entry below.

**End-to-end demo.** Multi-core WASI Preview 2 components run
through direct-linked imports without a delegate hop:

```bash
$ wacs run --wasip2 --call greet wasi-hello-component.wasm
hello
```

## [WACS.Cli 1.0.0] — Unified CLI

Ships a new `wacs` global tool that supersedes `wasm-transpile`.
Verb-based subcommand layout (`wacs run` / `build` / `inspect`)
matches `wasmtime` / `wasmer` industry precedent — keeps execution
flags (gas, profile, instr-logging) separate from compilation flags
(simd strategy, data-storage, tail-call) instead of cramming both
into a single CLI surface.

**Verbs.**
- `wacs run` — execute via interpreter (default) or transpiler
  engine. Carries the full Wacs.Console instrumentation surface
  (`--profile`, `--gas-limit`, `--log-execution`, `--stats`,
  `--super`, `--switch`) plus the multi-input ModuleLinker
  composition + component-mode auto-detect inherited from
  `wasm-transpile`. With `--wasip2` / `--host-package` for a
  component, implicitly upgrades to the transpiler engine since
  the typed bundle is a transpile-time concept.
- `wacs build` — transpile to a `.dll`. Multi-input runs land
  siblings as `<basename>.dll` alongside the chosen `--output`
  path. `--emit-main` bakes a `Program.Main(string[])` boilerplate
  into the output.
- `wacs inspect` — parse-only diagnostics: stats summary
  (functions / exports / memory / data segment bytes), exports /
  imports listing, `--dump-wat` round-trip via TextModuleWriter.

**Direct-run shortcut.** `wacs my.wasm` defaults to `wacs run my.wasm`
when the first positional arg is a `.wasm` / `.wat` file path that
exists.

**Smart defaults.** Component-vs-core auto-detect via the layer
header byte; multi-file input → ModuleLinker composition.

**Migration.** The legacy `wasm-transpile` (`WACS.Transpiler`)
package stays installable at `0.3.1` so existing pipelines keep
working — every flag still functions, output is byte-identical —
but invocations now print a stderr deprecation banner pointing at
the migration. See
[`Wacs.Console/README.md`](Wacs.Console/README.md) for the
verb-by-verb migration table.

**PackageId.** `WACS.Cli` (the bare `WACS` id is the runtime
library, `Wacs.Core`); the tool command users type is `wacs`.

```bash
dotnet tool install -g WACS.Cli
```

## [WACS.Transpiler 0.3.1] — Deprecation banner

Final release of the legacy `wasm-transpile` CLI before its
sunset. Every flag still works; every invocation prints two
deprecation lines to stderr pointing at `WACS.Cli` (`wacs`).
NuGet metadata's `<PackageDeprecationReason>` baked in. README
fronted with a deprecation block + migration table.

## [0.9.1] — JS String Builtins

Implements the full [JS String Builtins
proposal](https://github.com/WebAssembly/js-string-builtins)
(WebAssembly 3.0, Phase 5) backed by `System.String`. Modules compiled
with `--enable-js-string-builtins` (Binaryen) or equivalent now run on
WACS without modification — wasm manipulates host-owned UTF-16 strings
directly, without copying through linear memory on every boundary
crossing.

**Why it works.** The proposal's 13 imports under `wasm:js-string` are
defined observationally against UTF-16 code units — length is the
code-unit count, `charCodeAt` yields a code unit (not a code point),
`substring` is half-open, and surrogate pairs are preserved verbatim.
`System.String` is also UTF-16 with identical indexing and surrogate
semantics, so a pure environment swap yields observably identical
behavior. Nothing in the spec constrains the underlying representation
— only the input/output behavior.

**Host opt-in.** Register the namespace before instantiation, same
idiom as `Wasi.BindToRuntime`:

```csharp
using Wacs.Core.Runtime.Builtins;

var runtime = new WasmRuntime();
JsStringBuiltins.BindTo(runtime);
var modInst = runtime.InstantiateModule(module);
```

Hosts pass strings to wasm by wrapping as an externref:
`new Value(ValType.Extern, 0L, new JsStringRef("hello"))`.

**The 13 imports.** `test`, `cast`, `length`, `concat`, `substring`,
`equals`, `compare`, `charCodeAt`, `codePointAt`, `fromCharCode`,
`fromCodePoint` (11 simple i32 / externref functions) plus
`fromCharCodeArray` and `intoCharCodeArray` (GC-array-typed bridge
functions that read/write `StoreArray`). All 13 implemented as
`IFunctionInstance` subclasses that pop directly off the operand stack
— host-delegate marshaling can't carry externref through `PopScalars`,
so the builtins bypass it entirely.

**Infrastructure changes.** `InstCall.Link` generalized to dispatch
any `IFunctionInstance`, not just `HostFunction` / `FunctionInstance`
— opens the door for additional recognized-import namespaces in the
future. A new `BindHostFunction((module, entity), IFunctionInstance)`
overload on `WasmRuntime` for non-delegate registrations.

**Transpiler, AOT.** No transpiler changes needed — the transpiler
routes imports through `HostedRunner.BuildImportsProxy` →
`CreateStackInvoker` → `ExecContext.Invoke`, which already dispatches
any `IFunctionInstance`. Full AOT compatibility preserved
(`IsAotCompatible=true`); no `Reflection.Emit`, `DynamicMethod`, or
`Expression.Compile` anywhere in the new code path.

**Docs.** JS String Builtins reclassified from ✅ to ✳️ in the feature
matrix, alongside JS BigInt↔i64 and JSPI — the *wasm-level* semantics
are observably supported, but the *JS-API surface* (the namespace
name `wasm:js-string`, the JS-engine-recognized import handling) is a
browser idiom WACS emulates rather than implements natively. New
[`BROWSER_IDIOMS.md`](docs/BROWSER_IDIOMS.md) explainer covers all three
✳️ features: how each proposal maps to a native .NET primitive
(`long`, `System.String`, `Task`/`async`) and the host-side API for
each.

**Tests.** 34 new tests in `Wacs.Core.Test/JsStringBuiltinsTests.cs`:
28 WAT-based integration tests exercising the 11 simple builtins
through the runtime's standard dispatch (happy path, OOB sentinels,
traps, surrogate round-trip), plus 6 direct-invoke tests for the
GC-array-typed builtins (WAT parser doesn't yet support
`array.new_fixed`, so these construct `StoreArray` directly in C# and
drive the bound `IFunctionInstance` via `CreateStackInvoker`).

## [0.9.0] + WACS.Transpiler / Transpiler.Lib [0.3.0] + WACS.WASI.Threads [0.1.0] — Concurrent wasm execution

Makes the WACS runtime reentrant under concurrent host threads,
hardens shared-mutable state, adds a wasi-threads host adapter, and
lands the type-system foundation for shared-everything-threads.
Five stacked layers, 24 commits. No backwards-incompatible changes
to baseline wasm — all new behavior is opt-in or gated behind a
host-visible primitive.

**Layer 1 — Per-thread execution substrate.** The `WasmRuntime.Context`
singleton `ExecContext` became a `ConcurrentDictionary<ThreadId,
ExecContext>` keyed by `ManagedThreadId`. Each host thread entering the
runtime lazily gets its own operand stack, frame pool, locals pool,
and call stack while sharing a new `SharedRuntimeState` (Store,
Attributes, linked instruction arrays) by reference. `WasmThread` +
`IWasmThreadHost` primitives in `Wacs.Core/Runtime/Concurrency/` —
thread-spawn with task-based completion, cancellation-token
observation at call boundaries, `InterruptedException : TrapException`
propagating through existing trap handlers to `WasmThread.Completion`.
`IConcurrencyPolicy` grows async default-methods
(`Wait32Async`/`Wait64Async`/`NotifyAsync`) that wrap the sync versions
— shape only, enables a truly-yielding wait implementation as a later
additive change.

**Layer 2 — Shared-mutable state hardening.** `GlobalInstance.Value`
(24-byte struct) now serializes concurrent read/write through a
lazy per-instance lock when `IsShared` — non-shared globals stay on
the zero-overhead direct path. `TableInstance.Grow` pre-allocates
`List<T>.Capacity` in a single atomic field-swap before appending,
so concurrent `call_indirect` readers never see a mid-resize state;
readers stay lock-free even for shared tables. `TranspiledFunction`
swaps its reused `_paramBuffer` for `ArrayPool<object?>.Shared.Rent/
Return` per call. Dead `_asideVals` static stacks removed.
`Store.ReplaceFunction` documented as init-only.

**Layer 3 — wasi-threads adapter.** New sibling project
`Wacs.WASI.Threads` with `WasiThreads : IBindable`, 30 lines of actual
logic wiring the `wasi:thread-spawn` host import onto
`IWasmThreadHost.Spawn`. Monotonic positive-i32 tid allocation;
`wasi_thread_start` resolution via `ctx.Frame.Module.Exports` — no
explicit module registration. AOT-compatible (net8.0 + netstandard2.1,
`IsAotCompatible`). Hosts that don't want threads don't pay for them.

**Layer 4 — Soak + integration testing.** 13 new tests:
atomic-op-variety stress matrix (every RMW family × i32/i64 +
subword rmw8/rmw16 under 16-thread × 1k-iter contention), end-to-end
wait/notify producer-consumer through `HostDefinedPolicy` (with
timeout and not-equal precheck paths), and a 60-runtime soak that
would have caught the original Layer 1c `ThreadLocal<ExecContext>`
slot-exhaustion crash.

**Layer 5 — Shared-everything-threads foundation.** Feature-flag
`RuntimeAttributes.EnableSharedEverythingThreads` (default false) gates
the Phase-1-proposal subset that's stable enough to ship:
- `shared` annotations on globals (binary bit 1 of the mutability byte;
  text `(global (shared) ...)`) and tables (leveraging existing Limits
  Shared infrastructure).
- `thread_local` annotations on globals (binary bit 2; text
  `(global (thread_local) ...)`). Each host thread sees its own slot,
  initialized from the declared initializer on first access; storage
  lives on the per-thread `ExecContext` from Layer 1c.
- Declaration-driven `IsShared` wiring through to
  `GlobalInstance.EnableConcurrentAccess` / `TableInstance.EnableConcurrentAccess`.
  Layer 2b's "any shared memory → all globals/tables shared"
  approximation stays as a fallback for threads-1.0 modules that
  predate per-declaration annotations.
- Import-type matching: shared/thread_local must match exactly; a
  non-shared host global can't satisfy a shared import.

Deferred in Layer 5 because the proposal hasn't assigned canonical
opcode bytes: `global.atomic.{get,set,rmw.*}` instructions and
`pause`. Shared globals still work correctly through regular
`global.get`/`global.set` via the locking foundation — atomic ops are
a performance refinement on top.

Deferred as separate programs of work:
- **Emscripten pthreads ABI** (complex Web-flavored runtime surface;
  converging wasi-threads is the forward direction for most workflows).
- **Component Model canonical builtins** (`thread.spawn_ref`,
  `thread.spawn_indirect`) — will wire onto the same
  `IWasmThreadHost.Spawn` primitive when Component Model support lands.
- **Shared struct/array types**, **shared function references** —
  type-system discipline still evolving in the proposal.

**Verification:**
- Wacs.Core.Test: **366/366** (+28 new concurrent-execution tests)
- Wacs.Transpiler.Test: 561/561
- Spec.Test (full wasm-3.0 suite): 723/723
- `dotnet publish -p:PublishAot=true` produces a clean 15MB native
  binary.

## [0.8.3] + WACS.Transpiler / Transpiler.Lib [0.2.1] — Threads proposal

Implements the [WebAssembly threads proposal](https://github.com/webassembly/threads)
across all three execution back-ends. Flips README feature table
**Threads / threads ❌ → ✅**. All 47 atomic instructions — load/store
(full-width + subword zero-extending), RMW (add/sub/and/or/xor/xchg in
i32/i64/subword), cmpxchg, wait/notify, and fence — share the same
phase-1 primitives so correctness is identical across back-ends.

- **Polymorphic interpreter** (phase 1 / #79):
  - New `Wacs.Core.Runtime.Concurrency` namespace:
    `ConcurrencyPolicyMode` (NotSupported / HostDefined),
    `IConcurrencyPolicy`, `NotSupportedPolicy` (single-thread semantics
    — matching-value finite-timeout sleeps then returns 1, infinite
    timeout traps, mismatch returns 2), `HostDefinedPolicy` (real
    wait/notify via `ConcurrentDictionary<(MemoryInstance, addr),
    WaitSlot>` + per-waiter `ManualResetEventSlim`).
  - `MemoryInstance` atomic helpers:
    `AtomicLoad/Store/Add/Exchange/And/Or/Xor/CompareExchange{Int32,
    Int64}`. `Interlocked.*` on net8.0+; `CompareExchange` loop
    fallback on netstandard2.1 for And/Or/Xor.
    Lazy `ReaderWriterLockSlim _growLock` only allocated when shared
    + HostDefined — single-threaded modules pay nothing.
  - 47 instruction classes under `Wacs.Core.Instructions.Atomic/`:
    `InstAtomicMemoryOp` base with exact-alignment + shared-memory
    validation, subword CAS via `SubwordCas.Loop` / `SubwordCas.Cmpxchg`.
  - Factory (`SpecFactoryFE.cs`) + WAT parser extended with
    `TryGetAtomicMemoryOpcode` dispatch.
  - `RuntimeAttributes.ConcurrencyPolicy` with IL2CPP-detecting default
    (`Type.GetType("UnityEngine.Application,…")`, AOT-safe).
    `RelaxAtomicSharedCheck` escape hatch for toolchains that emit
    atomics on non-shared memories.
- **Switch runtime** (phase 2 / #80):
  - `BytecodeCompiler.SizeOfAtom` + `EmitAtom` — 12-byte memarg
    (`[memIdx:u32][offset:u64]`) stream encoding, 0 bytes for
    `atomic.fence`.
  - `AtomicHandlers.cs` with 47 `[OpHandler(AtomCode.X)]` methods.
    The source generator (`DispatchGenerator`) auto-discovers them and
    inlines the bodies into `DispatchFE` — **67 AtomCode references**
    in the regenerated `GeneratedDispatcher.g.cs` vs. 0 before.
- **AOT transpiler** (phase 3 / #81):
  - New `Wacs.Transpiler.Lib/AOT/Emitters/AtomicEmitter.cs` + public
    `AtomicHelpers` class. Functions containing atomics transpile to
    native CIL instead of falling back to the interpreter;
    `FallbackCount` is 0 for mixed-family modules.
  - Wait/notify routes through `ThinContext.ExecContext?.Concurrency-
    Policy ?? _standaloneFallback` — standalone / saved-dll consumers
    get `NotSupportedPolicy` semantics by default.
- **Tests (new):**
  - `Wacs.Core.Test.AtomicInstructionTests` — 28 tests (21 polymorphic
    + 7 switch-runtime parity).
  - `Wacs.Core.Test.SpecWastThreadsTests` — 4 tests over a pinned
    snapshot of `WebAssembly/threads@f521d7b3` at
    `Spec.Test/Data/threads/atomic.wast`.
  - `Wacs.Transpiler.Test.AtomicEquivalenceTests` — 12 polymorphic ↔
    transpiled equivalence tests.
  - `Wacs.Core.Test` total: 338/338. `Wacs.Transpiler.Test` total:
    561/561.
- **AOT stays green.** No runtime `Reflection.Emit` introduced;
  IL2CPP-safe by construction in `Wacs.Core`. Transpiler runtime
  assembly unchanged w.r.t. AOT safety (still uses `Reflection.Emit`
  as before — the produced DLL is AOT-loadable).

Concurrent wasm execution in a single `WasmRuntime` and host
thread-spawn imports remain out-of-scope for this release — the
threads proposal itself doesn't standardize spawning, and WACS's
single-`ExecContext` model is a separate refactor tracked for a
future release.

## [0.8.2] First-class WAT / WAST text format

- **Pure-C# WAT reader + writer.** New `Wacs.Core.Text` namespace
  provides a self-contained WebAssembly text-format pipeline:
  - `Lexer` / `Token` / `SExpr` / `SExprParser` tokenize and tree-ify
    WAT source (line / block comments, string escapes, annotations,
    quoted identifiers with full `\XX` / `\u{…}` UTF-8 decoding).
  - `Mnemonics` builds a `FrozenDictionary<string, ByteCode>` once at
    static-ctor time by reflecting over the `[OpCode(...)]` attributes
    already present on every opcode enum field. Parse and render share
    the same source of truth.
  - `TextModuleParser.ParseWat(Stream|string)` produces the *same*
    `Module` object the binary parser produces — two-pass name
    resolution, rec-group flattening, inline-typeuse synthesis with
    rec-isolated dedup, and per-instruction `ParseText` hooks
    co-located with each instruction's binary `Parse` override.
  - `TextScriptParser.ParseWast(...)` produces `ScriptCommand[]` for
    `.wast` scripts, including `(module binary …)` / `(module quote …)`
    and every `(assert_*)` form.
  - `TextModuleWriter.Write(module)` emits canonical, parser-friendly
    WAT that round-trips back through the text parser to a
    structurally equivalent `Module`. Distinct from the existing
    `ModuleRenderer.RenderWatToStream` debug/display variant, which is
    kept for inspection use.
- **`Wacs.Console` accepts `.wat` input.** `dotnet run --project
  Wacs.Console -- module.wat` runs text-format modules through any
  back-end (`--super`, `--switch`, `-t` / `--aot`) identically to
  `.wasm` input. The `-r` / `--render` flag now uses
  `TextModuleWriter` so the emitted `.wat` round-trips cleanly.
- **Spec-suite coverage: 100%.** New `Wacs.Core.Test` xUnit project
  runs two gates across the full WebAssembly 3.0 spec suite
  (`Spec.Test/spec/test/core/*.wast`):
  - `SpecWastSmokeTests` — **120 / 120** `.wast` files parse without
    error. The `SkipList` is empty; there are no text-only skipped
    tests.
  - `SpecWastEquivalenceTests` — **3457 / 3457** modules embedded in
    the spec scripts produce structurally identical `Module` objects
    under both the text parser and the binary parser (including
    preserved `try_table` shapes, rec-group layouts, GC struct /
    array composite types, annotations, and all Phase-5 / Phase-4
    proposals).
- **WIT IDL parser.** New `Wacs.Core.Components` namespace hosts a
  standalone recursive-descent parser for the component model's WIT
  interface definition language (packages, interfaces, worlds, full
  type system including `own<T>` / `borrow<T>` resource handles,
  `use` statements, world includes). Separate grammar from WAT, so a
  separate pipeline. Groundwork for the component-model work tracked
  in the roadmap.
- **AOT stays green.** No runtime `Reflection.Emit`. Reflection over
  `[OpCode("…")]` attributes is one-shot, at static-ctor time, on the
  same pattern `OpCodeExtensions.LookUp` already uses. `dotnet publish
  Wacs.Console -c Release -r osx-arm64 -p:PublishAot=true` continues
  to pass and the published binary parses + executes `.wat` input.

## WACS.Transpiler / WACS.Transpiler.Lib [0.2.0] Cross-process loading

- **Package split**: WACS.Transpiler remains the `wasm-transpile`
  dotnet-tool CLI; the programmatic surface (AOT namespace + Hosting
  helpers) now ships as a separate NuGet package **WACS.Transpiler.Lib**.
  Consumers who only want the library can reference it without pulling
  the tool packaging.
- **Saved .dlls now run in a fresh process.** Every transpiled assembly
  embeds a codec-encoded `ModuleInitData` as a `byte[]` field on a
  generated `__WACSInit` type. The Module constructor dispatches through
  `InitializationHelper.InitializeFromEmbedded`: in-process transpile +
  run keeps the fast `InitRegistry` path; cross-process load decodes the
  embedded bytes and rebuilds memories, tables, globals, data segments,
  and type metadata from the codec with no re-parse of the original
  WASM. Closes the v0.1 "cross-process execution is not yet supported"
  limitation.
- **Codec format documented and versioned.** Format spec in
  `Wacs.Core/Compilation/../../Wacs.Transpiler.Lib/AOT/InitDataFormat.md`:
  8-byte "WACSINIT" magic, u8 major+minor version, TLV-tagged section
  stream. Unknown tags skipped on decode (forward compat); newer-major
  files rejected cleanly. 60+ unit tests cover each section and
  primitive.
- **`TranspiledModuleLoader` (new)**: seamless dynamic-environment
  loading. Reads a saved `.dll`, discovers the Module / IExports /
  IImports types, wires imports (typed object OR by-name delegate
  dictionary via `DispatchProxy`), returns a `LoadedModule` handle
  that exposes the interfaces as first-class reflection objects plus
  `Invoke(name, args)` / `GetExport<TDelegate>(name)` for dispatch.
- **`Wacs.Console` integration**: new `--aot` flag transpiles the
  instantiated module and runs through the transpiled code. Subset of
  `TranspilerOptions` surfaced via `--aot_simd`, `--aot_no_tail_calls`,
  `--aot_max_fn_size`, `--aot_data_storage`; `--aot_save <path>` also
  persists the .dll to disk. CoreMark end-to-end: **17,542 iter/sec**
  on `--aot` vs 376 (`--switch --switch_super`) vs 277 (polymorphic).
- **Still not covered in 0.2** (tracked for v0.3): `--emit-main`
  expansion (auto-bind `--wasi-host`, `--allow-missing-imports` stubs,
  ref-type / v128 argv parsing).
- Spec parity unchanged: 473/473 on WebAssembly 3.0 spec suite; the new
  codec + loader add 70 unit tests + 4 cross-process end-to-end tests
  (549 total transpiler suite).

## [0.8.1] Switch runtime (opt-in, source-generated dispatcher)

- New alternative interpreter backed by a source-generated monolithic
  `switch` over an annotated bytecode stream. Immediates are pre-decoded
  at instantiation (no LEB128 at runtime), branch targets resolved to
  absolute stream offsets, and every reachable function is compiled
  eagerly when `UseSwitchRuntime` is set before `InstantiateModule`.
  AOT-safe — no `Reflection.Emit`, no `DynamicMethod`; build-time source
  generation only.
- Opt-in at the API level:
  ```csharp
  runtime.UseSwitchRuntime = true;
  runtime.ExecContext.Attributes.UseSwitchSuperInstructions = true; // optional stream-fuser
  runtime.InstantiateModule(module);
  ```
- `Wacs.Console` exposes the runtime through two new flags: `--switch`
  routes dispatch through the switch runtime; `--switch_super`
  additionally enables the bytecode-stream super-instruction fuser.
- **Spec parity: 118/118 wast files pass** on the WebAssembly 3.0 spec
  suite (matching the polymorphic runtime).
- Rough microbenchmarks (M1 Pro, .NET 8, median of 3): `switch` +
  `swFuse` is 1.5–2× faster than polymorphic across `fib-iter` / `fac` /
  `sum`. CoreMark: 376 iter/s (`--switch --switch_super`) vs 277 iter/s
  polymorphic — a 36% improvement on a real workload.
- Full architecture walkthrough in
  [`Wacs.Core/Compilation/SWITCH_RUNTIME.md`](Wacs.Core/Compilation/SWITCH_RUNTIME.md)
  (phases A–N, including the iterative Run that eliminates native-stack
  growth per WASM call).
- The polymorphic runtime remains the default and is unaffected.

## WACS.Transpiler [0.1.0] First release

- New NuGet package: `WACS.Transpiler`. Installs as a dotnet global tool
  (command: `wasm-transpile`). Ahead-of-time transpiles a `.wasm` module
  into a .NET assembly.
- CLI surface mirrors `TranspilerOptions`: `--simd`, `--no-tail-calls`,
  `--max-fn-size`, `--data-storage`, `--gc-checking`.
- `--emit-main` / `--entry-point` / `--main-class` bundle a host
  `Program.Main` into the output assembly for modules with no imports
  and scalar exports.
- `--run` invokes the emitted `Program.Main` in-process after
  transpiling, forwarding any trailing positional args — handy for IDE
  run configurations that want to transpile-and-execute in one step.
- Library surface: `Wacs.Transpiler.AOT.ModuleTranspiler.Transpile(...)`
  and `TranspilationResult.SaveAssembly(path)` for programmatic use.
- **Spec-equivalent to the WACS interpreter: 473/473 passing on the
  WebAssembly 3.0 spec test suite**, verified on both macOS ARM64 and
  Linux x64. Includes: multi-result `return` / `call_indirect` dispatch
  (via a MethodInfo registry for targets whose byref out-params don't
  fit Func/Action delegates), `f32.convert_i64_u` / `f64.convert_i64_u`
  routed through the interpreter's spec-exact RTNE helper for
  platform-invariant rounding, `struct.new` / `struct.new_default`
  global initializers with typed field storage, and correct
  sign/zero-extension for packed i8 / i16 struct reads.
- Known limitation: the saved `.dll` is intended for in-process use in
  this release — cross-process standalone execution (init-data embedded
  into the assembly) is a v0.2 milestone. See
  `Wacs.Transpiler/README.md` for details.

## [0.8.0] Public transpiler surface

- Public getters on ~20 instruction classes, `IFunctionInstance.Invoke`
  on the interface, `Store.ReplaceFunction`, and runtime accessors so
  `WACS.Transpiler` can drive transpilation from outside the assembly.
- New `WasmRuntime.TryGetExported{Memory,Table,Global,Tag}` /
  `GetExported{Memory,Table,Global,Tag}` accessors, mirroring the
  existing `TryGetExportedFunction` shape so host code can resolve any
  exported entity without reflecting into internals. Resolves #63.
- **Rename (breaking):** The interpreter super-instruction flag
  `WasmRuntime.TranspileModules` → `WasmRuntime.SuperInstruction`, the
  method `TranspileModule` → `ApplySuperInstructions`, and the
  `Wacs.Core.Runtime.Transpiler` / `Wacs.Core.Instructions.Transpiler`
  namespaces → `...SuperInstruction`. `FunctionTranspiler.TranspileFunction`
  is now `SuperInstructionRewriter.Rewrite`. This disambiguates from the
  new `WACS.Transpiler` AOT package.
- No behavior change for existing consumers beyond the rename — additive otherwise.

## [0.7.5] Fix rollup
- Fix to indirect calls
- Fix to reentrant calls
- Exposing global var index for use in parsing-only contexts

## [0.7.4] Performance
### Link-time optimization
- Instantiated functions are now flattened into a tape at link time
- Labels, branches, and function call targets are now computed during link
- Addressable store elements can now be precomputed and cached during link
- block, loop, trytable, and end instructions are now flagged as nops and will not incur a dispatch function call
### OpStack resident locals
- Local variables are now allocated on the stack
- Local variable operations now have improved cache locality 
- This refactor is prep for link-time register computation

## [0.7.3]
- Reimplemented AOT compatible invoker bindings

## [0.7.2]
- removing Linq.Expression for AOT compatibility

## [0.7.1]
- fixes to CreateInvoker binding

## [0.7.0]
- wasm-3.0 spec support
- exnref/tag support
- memory64 support
- multi-memory support (enabled)

## [0.6.0]
- wasm-gc extension
- function-references extension

## [0.3.0]
- Implemented JSPI-like async binding and execution
- Hooked up more super-instruction threading

## [0.2.0]
- Implemented super-instruction threading
- Precomputed (non-allocating) block labels

## [0.1.6]
- Updating to latest dll
- Fixing package layout
- Fixing Sample importer

## [0.1.4]
- Initial project setup for Unity.
