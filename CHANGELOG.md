# Changelog

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
