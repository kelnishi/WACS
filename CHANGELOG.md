# Changelog

## WACS.ComponentModel 0.8.0 / WACS.ComponentModel.Parser 0.2.0 — async-ABI handle types (Phase 2 Slice A)

First slice of WASIp3 Phase 2: lands `stream<T>` / `future<T>` /
`error-context` in the type system and the binary type-section
parser. Handle marshaling and canon-builtin parser come in
later slices; this commit only adds shapes + tag dispatch.

### `CtValType` hierarchy

Three new sealed subclasses in
`Wacs.ComponentModel/Types/CtValType.cs`:

- `CtStreamType { CtValType? Element }` — `stream<T>` or
  `stream` (empty element form). 4-byte handle in the same
  handle space as `own` / `borrow`.
- `CtFutureType { CtValType? Element }` — `future<T>` or
  `future`. Same wire shape as `CtStreamType`.
- `CtErrorContextType` — singleton; no inner type parameter
  (the debug message is exchanged via the
  `error-context.debug-message` canon builtin, not part of
  the static type).

### Parser-side deftype entries

Three new `DefTypeEntry` subclasses in
`Wacs.ComponentModel.Parser/Runtime/Parser/TypeSectionReader.cs`:
`ComponentStreamType`, `ComponentFutureType`, and the
`ComponentErrorContextType.Instance` singleton. Three new tag
constants:
`StreamTypeTag = 0x66`, `FutureTypeTag = 0x65`,
`ErrorContextTypeTag = 0x64`. `DecodeEntry` recognizes all
three (mirrors the existing aggregate cases).

The `stream<T>` / `future<T>` payload uses the canonical
`<T>?` (optional) presence-byte encoding — 0x00 = absent,
0x01 t = present (reusing `DecodeOptionalValType`).

### Spec verification

The full canon byte-tag table (0x00–0x2b core + 0x40–0x42
threading) is now in the doc comment on
`CanonSectionReader`, captured against
WebAssembly/component-model main HEAD's
`design/mvp/Binary.md`. Subsequent slices in this phase
implement the parser entries that consume those bytes.

### Verification

`dotnet build` clean on both libs.
`Wacs.ComponentModel.Test` + `Bindgen.Test` suites unchanged
(no test additions yet — those land with the canon-parser
slice + round-trip test).

## WACS 0.16.13 — Phase 1 close-out: standalone ResumeThrow + retention-free ContinuationStore

Closes two Phase 1 stack-switching gaps and adds the longevity
test the WASIp3 plan's acceptance criterion #2 asks for.

### ResumeThrow standalone

`StackSwitchingHelpers.ResumeThrow` no longer throws
`NotSupportedException` when `ExecContext` is null. The
standalone branch now constructs the exception in-line via the
public `ExnInstance` constructor — no `Store.AllocateExn`, no
reflection, AOT-safe — invokes the cont via the typed
`StandaloneContInvoker` mirroring mixed-mode semantics for
fresh-only continuations (body runs, then `WasmException`
propagates), and surfaces a `WasmException` carrying the
synthesized `ExnInstance`.

The synthesized exn idx is minted from a process-wide
`Interlocked` counter — uniqueness only matters for the catch
site's identity comparison, which is by `TagAddr` not by exn
idx. Catch arms compare against the synthesized `TagAddr`
(same convention as the standalone `Suspend` path).

### ContinuationStore: retention-free

`Wacs.Core.Runtime.Concurrency.ContinuationStore` previously
held a `List<ContInstance>` that grew monotonically across the
lifetime of the standalone context — every `Allocate` appended
a strong reference that was never released. A 1M resume cycle
on a `Func<…>` body would retain 1M `ContInstance` objects
plus the list slots.

The store now mints a per-allocation idx via a counter and
returns the freshly-allocated instance without retaining a
reference. The `Get(long idx)` lookup is removed; it was
unused (verified by grep — no callers in either `Wacs.Core`
or `Wacs.Transpiler.Lib`).

Wasm refs reach a continuation through the `Value.GcRef` they
carry. Once those refs are unreachable, the CLR GC reclaims
the instance — matching the spec's "continuations are GC'd
once unreferenced" model. The instance idx is now purely an
allocation counter, not a lookup key.

### 1M-switch longevity test

`StandaloneContInvokerTests.Resume_one_million_iterations_does_not_leak_continuations`
exercises 1M allocate + resume cycles and asserts heap growth
stays under 1 MB. Pre-fix this would have grown >50 MB.

The test is gated behind `WACS_LONG_TESTS=1` to keep the
default unit cycle short (~72 ms for 1M cycles when enabled,
which still adds noticeable time to the suite). Enable it
locally with `WACS_LONG_TESTS=1 dotnet test`.

### Test coverage

3 new standalone tests for `ResumeThrow` (payload, missing
invoker, non-Fresh trap) + 1 longevity test. 520/520
`Wacs.Core` and 833/833 `Wacs.Transpiler` tests pass.

### Phase 1 acceptance — close-out

- ✓ Mixed-mode interpreter parity (cont.new / cont.bind /
  suspend / resume / resume_throw / switch all execute end-to-end)
- ✓ Transpiler CIL emit parity (all 6 opcodes)
- ✓ Standalone-mode parity (5 of 6: cont.new / cont.bind /
  suspend / resume / switch; resume_throw now closes)
- ✓ 1M-switch longevity (no retention leak)
- ✗ Multi-result standalone invokers — blocked by the
  underlying multi-result func representation
  (`MultiReturnMethodRegistry` uses runtime `DynamicMethod`;
  `BuildDelegateTypeForFunc` returns null for multi-result;
  `StandaloneDelegate` is null for those conts). Closing
  requires a different invoker contract
  (`Value[]→Value[]` adapter generated at transpile time).
  Deferred — bounded follow-up.
- ✗ True re-resumable continuations (current impl is one-shot;
  cont state is set to `Completed` after first resume).
  Requires frame snapshotting — architectural future work,
  not Phase 1 scope.
- ⚠ Official spec-suite fixtures not vendored — the
  stack-switching proposal repo doesn't publish `.wast`
  fixtures we can run through our spec harness. Phase 1
  acceptance is validated via the hand-written
  `StackSwitchingExecutionTests` and
  `StackSwitchingEquivalenceTests`.

## AOT enforcement test

`Wacs.Core/Wacs.Core.Test/AotAcceptanceTests.cs` (new) enforces
the hard requirement that `Wacs.Core` is AOT-safe. The test
publishes `Wacs.Bench/Wacs.Bench.Aot` with
`-p:TrimmerSingleWarn=false` (expands the `IL2104` umbrella
into per-site warnings), parses the analyzer output, filters
for warnings whose source path contains `Wacs.Core/`, and
asserts the set matches an in-source `KnownBaseline` allow-list.

### Policy

- The base C# AOT analyzer fires only during `PublishAot=true`
  publishes — not during `dotnet build`. Without this test
  step, a new `Type.GetMethod` / `GetField` / `DynamicInvoke`
  / etc. could slip into `Wacs.Core` unnoticed and only break
  a downstream consumer's AOT image.
- The `KnownBaseline` allow-list is **not** for adding new
  AOT-unsafe code — it tracks pre-existing violations slated
  for fix. Adding to it requires explicit reviewer sign-off.
  Removing entries (when the underlying code is fixed) is the
  only direction the list should move.
- Current baseline (2026-05-18): one entry —
  `Wacs.Core/Runtime/WasmRuntimeExecution.cs(459)` `IL2075`,
  a `Type.GetConstructor` reflection call in the host
  exception-rethrow path. Pre-dates the stack-switching work.

### Gating + CI

- Gated via the `WACS_AOT_TEST=1` env var. Publish takes
  ~60-90s on a cold cache; gating keeps dev cycles fast.
  Without the env var, the test logs "Skipping" and exits in
  ~3ms.
- `.github/workflows/ci.yml` "Run Core Tests" step sets the
  env var — CI enforces the check on every PR/merge.

### Negative-test verified

Temporarily removing the baseline entry produces:
```
AOT-unsafe pattern introduced in Wacs.Core:
  IL2075 at Wacs.Core/Wacs.Core/Runtime/WasmRuntimeExecution.cs(459)
```
followed by guidance on the AOT-safe rewrite options
(interface dispatch, source generators).

516 Wacs.Core tests pass (515 + 1 new gated test).

## WACS.Transpiler.Lib 0.12.4 — Stack Switching standalone-mode dispatch (Slice B.2)

Transpiler now generates one `StandaloneContInvoker` subclass
per unique continuation typeidx in a module. Each generated
invoker is sealed, has a public-static `Instance` singleton
field, and overrides `Invoke` with strongly-typed IL: cast
`cont.StandaloneDelegate` to the typed `Func` / `Action`,
unbox `Value[]` args per the signature's params, call the
delegate (ThinContext is closed into the target by
`PopulateFuncTable`, so the delegate type omits it), wrap the
typed result back into `Value[]`.

### New emit pass

`ContInvokerEmitter` (new file). Plugged into
`ModuleTranspiler.Transpile` as Pass 0c — between GC type
emit and function method-stub creation. Scans every wasm
function body for `InstContNew` / `InstContBind` /
`InstResume` / `InstResumeThrow` / `InstSwitch`, collects
distinct continuation typeidxs, generates one invoker class
per typeidx, bakes them via `TypeBuilder.CreateType()` so
the emit downstream can `ldsfld` their `Instance` fields.

Multi-result continuations (arity > 1) are skipped in this
slice — the registry entry is absent and the helper falls
back to its documented `NotSupportedException` for that
signature.

### Function emit wiring

`FunctionCodegen` gains an optional
`Dictionary<int, FieldInfo>` parameter (the invoker registry
from `ContInvokerEmitter.InvokerFields`).
`StackSwitchingEmitter.Emit` accepts it and threads through
to `EmitResume` / `EmitResumeThrow` / `EmitSwitch`. Each
call site `ldsfld`'s the invoker for its cont typeidx (or
`ldnull` if absent) and passes it as the trailing
`StandaloneContInvoker?` argument to the helper.

### End-to-end test

`Standalone_resume_via_generated_invoker` —
`TranspiledModuleWrapper.Instantiate()` +
`InvokeExport()` on a module that uses `cont.new` + `resume`,
no `WasmRuntime` involved. The wrapper's
`Activator.CreateInstance` path leaves
`ThinContext.ExecContext` null; the helper routes through
`ResumeStandalone`, which calls the generated
`Invoker_Cont1.Invoke`, which casts the
`StandaloneDelegate` to `Func<int>` (`ThinContext` closed
in by `PopulateFuncTable`), invokes, wraps the result.
Asserts the expected value.

### Verification

- 834 transpiler + 515 Wacs.Core + 380 ComponentModel tests
  green.
- `dotnet publish Wacs.Bench.Aot` clean — no new IL2026 /
  IL3050 / IL2070 / IL2075 warnings; the only umbrella
  IL2104 on Wacs.Core is pre-existing
  (FluentValidation surface, unrelated).

### Remaining

`ResumeThrow` standalone still surfaces
`NotSupportedException` — it requires an AOT-safe
`WasmException` construction path without
`Store.AllocateExn`. Multi-result continuations also remain
gated. Both are bounded follow-ups.

## WACS 0.16.12 — Standalone Cont Invoker contract (Slice B.2 v0)

Establishes the AOT-safe dispatch contract for standalone-mode
`resume` / `resume_throw` / `switch`. The transpiler-side
per-signature invoker generation that lights this up is the
focused follow-up.

### Contract

New abstract class
`Wacs.Core.Runtime.Concurrency.StandaloneContInvoker` with a
single virtual `Invoke(IContinuationContext, ContInstance,
Value[]) → Value[]`. Concrete subclasses (one per unique
continuation signature in a module) implement the typed cast
on `cont.StandaloneDelegate`, the per-arg unbox from `Value`,
the typed delegate call, and the result wrap back to `Value[]`.
Virtual dispatch instead of reflection — AOT-safe.

### Helper changes

`StackSwitchingHelpers.Resume`, `ResumeThrow`, and `Switch`
now take an optional `StandaloneContInvoker? standaloneInvoker
= null` last parameter. Mixed-mode callers
(`ExecContext != null`) ignore it. Standalone callers
(`ExecContext == null`):

- `Resume` and `Switch`: route through new private
  `ResumeStandalone` / `SwitchStandalone` paths that validate
  the continuation, call `invoker.Invoke`, manage the state
  transition (Running → Completed), and propagate any thrown
  exception (e.g., `SuspensionException`) to the caller's
  emitted try/catch arm.
- `ResumeThrow`: still throws `NotSupportedException` in
  standalone — the exception-injection path additionally
  needs an AOT-safe `WasmException` construction without
  going through `Store.AllocateExn`.

The error message clearly identifies the missing piece:
`"Standalone-mode resume / resume_throw / switch require a
typed StandaloneContInvoker generated by the transpiler for
the continuation's signature. The current emit does not yet
generate these invokers; the call site passed null."`

### Tests

4 new `StandaloneContInvokerTests` in `Wacs.Core.Test`:
- `Resume_standalone_with_invoker_returns_typed_result` —
  hand-rolled invoker for `() → i32`, asserts the result.
- `Resume_standalone_without_invoker_throws_clear_NotSupported`
  — asserts the error message identifies the missing piece.
- `Resume_standalone_propagates_invoker_exception` —
  exceptions through the invoker bubble, cont is marked
  Completed.
- `Resume_standalone_rejects_non_Fresh_cont` — second resume
  on a completed cont traps.

515 Wacs.Core + 833 transpiler + 380 ComponentModel tests
green. AOT publish unchanged (no new IL2026/IL3050 warnings
on the stack-switching surface).

### Still pending

Transpiler emit-side work (Task #16): generate one
`StandaloneContInvoker` subclass per unique continuation
signature in a module at transpile time, wire the resume /
switch / resume_throw emit to load and pass the right
subclass instance. This is bounded work — parallels how the
transpiler already generates per-function methods — but
genuinely a separate slice.

## WACS 0.16.11 / WACS.Transpiler.Lib 0.12.3 — Stack Switching helpers go AOT-safe

Replaces the runtime reflection the 0.16.10 helpers used to
access ThinContext fields with interface dispatch. AOT
publish of `Wacs.Core` is back at zero analyzer warnings on
the stack-switching code path.

### New interfaces in `Wacs.Core.Runtime.Concurrency`

- `IContinuationContext`: the contract the transpiler's
  `ThinContext` satisfies. Exposes `ExecContext?`,
  `Continuations`, `Tags`, `FuncTable` as properties.
- `IDelegateRef : IGcRef`: exposes `Target` (the delegate);
  `Wacs.Transpiler.AOT.DelegateRef` implements it.

### Changes

- `StackSwitchingHelpers.{ContNew, ContBind, Suspend, Resume,
  ResumeThrow, Switch, FindHandlerMatch, ReifyHandlerArgs}`
  now take `IContinuationContext hctx` instead of
  `object thinCtx`. Internal access is direct property
  reads — no `GetField` / `GetValue` calls at runtime.
- `ExtractDelegateRefTarget` is now a one-line `gcRef as
  IDelegateRef`?.Target downcast.
- `ThinContext` declares `: IContinuationContext` with
  explicit-interface forwarders that delegate to its existing
  public fields. `DelegateRef` declares `: IDelegateRef`.

### Verification

- `dotnet publish Wacs.Bench.Aot` (the project that exercises
  `PublishAot=true` against Wacs.Core net8.0) produces zero
  IL2026 / IL3050 / IL2070 / IL2075 warnings on
  stack-switching helpers. The only remaining IL2104
  umbrella warning on `Wacs.Core` is pre-existing
  (FluentValidation / TagInstance lookups outside this
  surface).
- 833 transpiler + 511 Wacs.Core + 380 ComponentModel tests
  green.

## WACS 0.16.10 / WACS.Transpiler.Lib 0.12.2 — Stack Switching standalone-mode parity (3 of 6 ops)

Closes the standalone-mode gap for `cont.new`, `cont.bind`, and
`suspend`: transpiled `Module` classes instantiated via
`Activator.CreateInstance` (no host `WasmRuntime`) can now
execute these three opcodes through emitted CIL. The helpers
branch on `ExecContext != null` and use `ThinContext`-local
state when standalone.

### Mode-aware helpers

`StackSwitchingHelpers.{ContNew, ContBind, Suspend, Resume,
ResumeThrow, Switch, FindHandlerMatch, ReifyHandlerArgs}` now
take `object thinCtx` (the transpiler's `ThinContext`) directly
and extract the optional `ExecContext` via reflection, keeping
`Wacs.Core` free of a `Wacs.Transpiler.Lib` dependency.

- **Mixed mode** (`ExecContext != null`): unchanged behavior
  — uses runtime's `Store` + `Frame` + `OpStack`.
- **Standalone mode** (`ExecContext == null`):
  - `cont.new`: extracts the function delegate from the
    funcref's `GcRef` via duck-typed reflection on a `Target`
    field (works against `Wacs.Transpiler.AOT.DelegateRef`
    without referencing the type); falls back to
    `ThinContext.FuncTable[Data.Ptr]`. Allocates via
    `ThinContext.Continuations.Allocate(typeIdx, delegate)`.
  - `cont.bind`: allocates the new continuation via
    `ThinContext.Continuations`, preserves the source's
    delegate reference.
  - `suspend`: synthesizes a `TagAddr` from the raw tag index
    so the in-module CIL catch arm can match against the
    same value (no `Store` renumbering between throw and
    catch in a single module).

### New infrastructure

- `Wacs.Core.Runtime.Concurrency.ContinuationStore` —
  Module-local allocator parallel to `Store`'s continuation
  list, used in standalone mode.
- `ContInstance` gains a second constructor taking
  `System.Delegate` for standalone allocation; the new
  `StandaloneDelegate` field carries the function reference
  when `Function` (FuncAddr) is unused.
- `ThinContext.Continuations` — always-populated
  `ContinuationStore` field.

### Still gated on a future slice

`resume`, `resume_throw`, and `switch` in standalone mode
surface `NotSupportedException` with the explanatory message
`"Standalone-mode transpiled modules do not yet support
resume / resume_throw / switch — these ops invoke the
continuation's function which currently requires the runtime's
interpreter dispatch."` Closing this gap requires reflection-
based delegate marshaling: `delegate.DynamicInvoke(ctx,
args…)` with arg/result conversion between `Value` and the
delegate's typed CLR parameters; a small but real piece of
work tracked separately.

### Tests

- New `Standalone_cont_new_via_module_class` —
  `TranspiledModuleWrapper.Instantiate()` + `InvokeExport()`
  on a module that uses `cont.new` + `ref.func`; asserts the
  result. Confirms the standalone path through the runtime
  helpers and reflection-based delegate extraction.
- 5 existing `StackSwitchingEquivalenceTests` still pass.
- 833 transpiler + 511 Wacs.Core + 380 ComponentModel tests
  green.

## WACS 0.16.9 / WACS.Transpiler.Lib 0.12.1 — Stack Switching CIL emit (all 6 opcodes)

Closes the remaining three transpiler emitters: `resume`,
`resume_throw`, and `switch` now produce CIL that mirrors the
interpreter's runtime behavior. Every cont.* opcode emits real
IL — no function containing them falls back to interpreter
execution anymore (in mixed mode). Standalone mode remains
gated on a separate self-contained continuation runtime.

### `switch`

Straight-line: pack t1* args + cont into the helper call,
unpack the target cont's t2* results on normal completion.
No new handler frame installed — suspends inherit the
caller's chain.

### `resume` / `resume_throw`

Wraps the helper call in a CIL try/catch +
tag-dispatch arm, modeled on `ExceptionEmitter.EmitTryTable`:

1. Save t1* args + cont to typed locals; pack t1* into
   `Value[]`; build a parallel `int[]` of handler tag indices.
2. `BeginExceptionBlock`; call helper which installs the
   handler frame as transpiler-installed
   (`HandlerTargets=null`) and invokes the cont's function.
3. Store normal-completion results.
4. `BeginCatchBlock(SuspensionException)`: call
   `FindHandlerMatch` to identify the matched handler index
   (or -1). Per-handler compare-and-`Leave` chain → dispatch
   labels; `Rethrow` if no match.
5. `EndExceptionBlock`; `Br endLabel` to bypass dispatch.
6. Per-handler dispatch label: call `ReifyHandlerArgs` to
   build the payload + one-shot reified-cont array; unbox
   payload values onto the CIL stack typed per the handler's
   tag params; push the reified cont; `Br` to the wasm
   enclosing handler label via `ControlEmitter.PeekLabel`.
7. `endLabel`: unpack normal-completion results from the
   helper's `Value[]` to typed CIL stack values.

### Dispatcher: transpiler-installed frames

`ResumeHandlerFrame.IsTranspilerInstalled` (= `HandlerTargets is null`)
distinguishes the two installation paths.
`SuspensionDispatcher.TryHandle` now handles the
transpiler-installed case by unwinding the call stack to the
install frame, popping the matched handler, and returning
`false` — letting the CLR propagate `SuspensionException` up
to the transpiled caller's CIL catch arm. The interpreter
path keeps its existing precomputed `BlockTarget` branch
behavior.

### New helpers in `StackSwitchingHelpers`

- `Resume(ExecContext?, int typeIdx, Value[] args, Value cont, int[] handlerTagIdxs) → Value[]`
- `ResumeThrow(ExecContext?, int typeIdx, int tagIdxValue, Value[] excArgs, Value cont, int[] handlerTagIdxs) → Value[]`
- `Switch(ExecContext?, int typeIdx, int tagIdxValue, Value[] args, Value cont) → Value[]`
- `FindHandlerMatch(ExecContext?, int[] handlerTagIdxs, SuspensionException) → int` (catch-arm support)
- `ReifyHandlerArgs(ExecContext?, Value cont, SuspensionException) → Value[]` (catch-arm support)

### Tests

`StackSwitchingEquivalenceTests` previously asserted
`fallbacks > 0` (cont.* known to fall back); now asserts
`fallbacks == 0` for all five tests including
producer/consumer suspend/resume, resume_throw with
try_table catch, and switch with inherited handler chain.

832 transpiler + 511 Wacs.Core + 380 ComponentModel tests
green.

## WACS 0.16.8 / WACS.Transpiler.Lib 0.12.0 — Stack Switching CIL emit (3 of 6 opcodes)

Promotes the transpiler from "fallback only" to "real CIL emit"
for three of the six stack switching opcodes:

- **Emitted via runtime helpers**: `cont.new`, `cont.bind`,
  `suspend`. These are straight-line operations with no non-
  local control transfer back into the caller. Emitted IL
  packs CIL-stack operands into `Value[]`, calls the helper,
  unpacks the result.
- **Still interpreter fallback**: `resume`, `resume_throw`,
  `switch`. They invoke a continuation's function and route
  `SuspensionException` back to handler labels in the caller's
  CIL body — the same try/catch + Leave-to-dispatch-label
  pattern `ExceptionEmitter.EmitTryTable` uses for `try_table`.
  Substantial separate work tracked as a Phase 1 exit gate.

### New surface

- `Wacs.Core.Runtime.Concurrency.StackSwitchingHelpers` —
  static entry points that the transpiler's emitted IL calls.
  `ContNew(ExecContext?, int typeIdx, Value funcRef) → Value`,
  `ContBind(ExecContext?, int targetTypeIdx, Value cont, Value[] prefix) → Value`,
  `Suspend(ExecContext?, int tagIdx, Value[] payload) → throws SuspensionException`.
- `Wacs.Transpiler.AOT.Emitters.StackSwitchingEmitter` —
  `CanEmit` now returns `true` for the three emittable
  opcodes, dispatches to per-op CIL emitters that wrap CIL
  stack operands as `Value` and call the helper.

### Caveats

Helpers require a live `ExecContext` (mixed mode). In
standalone mode (`Module` instantiated via
`Activator.CreateInstance` with no host runtime), the helpers
throw `NotSupportedException("Stack switching opcodes (cont.*)
require a WasmRuntime host context …")` with a clear
explanation — replacing the prior opaque "Function N not
transpiled" message. A self-contained continuation runtime for
standalone mode is a separate design effort.

### Tests

- New `Cont_new_and_suspend_emit_without_fallback` —
  transpiles a function containing only `cont.new` and asserts
  `result.FallbackCount == 0`, then invokes the function and
  asserts the expected result.
- Existing 4 `StackSwitchingEquivalenceTests` still pass; the
  producer/consumer test's producer function (cont.new +
  suspend) now transpiles, while the host function (resume)
  still falls back — `fallbacks > 0` remains true overall but
  represents fewer functions than before.

832 transpiler + 511 Wacs.Core + 380 ComponentModel tests green.

## WACS.Transpiler.Lib 0.11.2 — Stack Switching mixed-mode parity tests + standalone caveat

Pins down the transpiler's behavioral guarantee for the six
stack switching opcodes:

- **Mixed-mode parity** (invoked through a `WasmRuntime` stack
  invoker): functions containing cont.* opcodes fall back to
  interpreter execution and produce identical results to a
  pure-interpreter run. Four new equivalence tests in
  `Wacs.Transpiler.Test.StackSwitchingEquivalenceTests` cover
  cont.new+resume, full producer/consumer suspend/resume,
  resume_throw with try_table catch, and switch with
  inherited handler chain.
- **Standalone-mode caveat** (transpiled `Module` class
  instantiated via `Activator.CreateInstance` without a host
  runtime): `CallEmitter.InvokeFallback` throws
  `NotSupportedException("Function N not transpiled and no
  interpreter available")` on the first call to any
  cont.*-bearing function. Until CIL emission for the six
  opcodes lands, cont.*-bearing modules must be hosted by a
  `WasmRuntime` to be callable.

`StackSwitchingEmitter`'s XML doc now spells this out
explicitly so future readers understand which fallback path
is wired and which is not.

830 transpiler + 511 Wacs.Core + 380 ComponentModel tests green.

## WACS 0.16.7 — `resume_throw` runtime parity

Brings `resume_throw $ct $tag handler*` (0xE4) to runtime
parity with `resume` and `switch`. `InstResumeThrow.Execute`
was `NotImplementedException`; now it:

1. Pops the target continuation, validates `Fresh`.
2. Pops the exception tag's params from the operand stack.
3. Allocates an `ExnInstance` via `Store.AllocateExn`.
4. Installs the resume handlers (precomputed at Link time, same
   shape as `InstResume`).
5. Pushes the cont's bound prefix args (from any prior
   `cont.bind`) and invokes the cont's function — sets up the
   inner frame with the function's locals initialized.
6. Pushes the exception ref onto the inner frame's opstack and
   runs `InstThrowRef.ExecuteInstruction`, which unwinds the
   cont's empty control stack, pops the cont's frame (auto-
   pruning the resume handler), and continues unwinding
   through the caller's enclosing `try_table` chain until
   caught or surfaced as `UnhandledWasmException`.

For one-shot semantics, this models the spec's
function-entry-throw behavior: an outer `try_table` catching
the tag receives the exception's payload as designed; an
inner `try_table` inside the cont's function body would not
fire since the throw injects before the function's first
instruction. Re-entering a suspended cont via `resume_throw`
isn't reachable today (suspended conts are marked Completed
at suspension dispatch); the throw-at-entry path covers what
WASIp3 cancellation semantics need.

### Tests

- New `ResumeThrow_injects_exception_caught_by_outer_try_table`:
  resume_throw injects an exception that the caller's
  try_table catches, the catch handler captures the payload
  (77).
- The `Execute_throws_NotImplemented` theory is gone — all six
  stack-switching opcodes have runtime implementations.

511 Wacs.Core + 380 ComponentModel + 826 Transpiler tests green.

## WACS 0.16.6 — `switch` runtime parity

Brings `switch $ct $tag` (0xE5) to runtime parity with `resume`.
`InstSwitch.Execute` was `NotImplementedException`; now it pops
the target continuation, validates it's `Fresh`, allocates a
one-shot Completed placeholder for the reified caller, marshals
the call's parameter stack, and dispatches via `InvokeResolved`.

The validator's stack shape now matches the proposal: switch's
input stack is `[t1* (ref $ct)]` where `t1*` comes from the
**tag's** params (excluding its trailing self-ref), not the
target function's params. The earlier draft popped the target's
full `ft.ParameterTypes`, which conflated the call-site shape
with the callee shape.

`switch` does not install a new resume handler frame — it
inherits the current handler chain. A suspend raised inside the
switched-to cont continues to walk up through whatever resume
frame installed matching handlers, which is the producer-
consumer-trampoline shape exercised by the new test.

### Tests

- New `Switch_into_cont_inherits_outer_resume_handlers` —
  switch into a fresh cont whose body suspends; outer resume's
  on-yield handler captures the suspended value.
- `Execute_throws_NotImplemented` theory shrinks to one case:
  only `InstResumeThrow` remains unimplemented at the Execute
  level.

511 Wacs.Core + 380 ComponentModel + 826 Transpiler tests green.

## WACS 0.16.5 — Stack Switching: WAT parsing + end-to-end execution tests

Adds the text-format parser entries that allow hand-written WAT
modules to use the six Stack Switching opcodes plus the
`(cont $ft)` type form, and lands the producer/consumer
co-routine tests proving the suspend/resume substrate works
end-to-end. Also closes a `Unknown CompositeType: ContType` gap
in `ValType.IsSubType`'s DefType arm that surfaced once the
text parser made it easy to land a module-typed cont through
validation.

### Text parser

- `(type $ct (cont $ft))` — continuation type form in the type
  section.
- Plain instruction keywords: `cont.new $ct`, `cont.bind $ct1
  $ct2`, `suspend $tag`, `switch $ct $tag`.
- Folded forms for `resume` / `resume_throw`:
  `(resume $ct (on $tag $label)* operand*)`,
  `(resume_throw $ct $tag (on $tag $label)* operand*)`. The
  `(on … switch)` variant is recognized via a trailing
  `switch` keyword inside the clause.

### Type system

- `ValType.IsSubType` DefType arm now handles `ContType` —
  matches against `ValType.ContRef` / `ContRefNN`.
- `TypeWriters.WriteCompositeType` and
  `TextModuleWriter.WriteCompositeBody` round-trip `ContType`
  via `CompType.ContCt = 0x5D` (binary) and `(cont N)` (text).

### Execution tests

Four new `Wacs.Core.Test.StackSwitchingExecutionTests`:

- `Resume_runs_continuation_to_completion` — `cont.new` + bare
  `resume` runs the wrapped function and returns its result.
- `Suspend_branches_to_matching_on_handler_with_payload` —
  full producer/consumer: producer suspends with a tag, the
  on-tag handler captures the payload and returns it.
- `Unhandled_suspend_propagates_as_trap` — a suspend whose
  tag isn't installed by any active resume frame surfaces as
  a trap.
- `Second_resume_of_already_handled_cont_traps` — the
  one-shot continuation handed to a handler can't be
  re-resumed; the second resume sees a non-Fresh cont and
  traps.

511 Wacs.Core + 380 ComponentModel + 826 Transpiler tests green.

## WACS.Transpiler.Lib 0.11.1 — Stack Switching extension point + intentional interpreter fallback

Documents the transpiler's handling of the six Stack Switching
opcodes (`cont.new`, `cont.bind`, `suspend`, `resume`,
`resume_throw`, `switch`) as an intentional interpreter
fallback rather than a generic "unsupported opcode" rejection.

- New `Wacs.Transpiler.AOT.Emitters.StackSwitchingEmitter` —
  `CanEmit` returns `false`, `IsStackSwitchingOpcode`
  identifies the family. Reserves the extension point so the
  real CIL emit lands in one place.
- `FunctionCodegen.HasEmitter` consults the new emitter; the
  rejection reason in `LastRejectionReason` now distinguishes
  cont.* (known but not yet emitting) from genuinely unknown
  opcodes for diagnostics.
- Behavior unchanged at the user level: a function containing
  any cont.* opcode falls back to the interpreter, which since
  WACS 0.16.3 / 0.16.4 implements
  `cont.new` / `cont.bind` / `suspend` / `resume` end-to-end
  with one-shot semantics.

826 transpiler tests still green.

## WACS 0.16.4 — suspend/resume one-shot dispatch

Lands the runtime catch path for `suspend $tag` and the
`resume $ct handler*` invocation. `resume` installs an active
handler frame, calls the continuation's inner function, and
the interpreter loop's new `SuspensionException` catch arm
walks the handler stack to find a matching tag — on a hit,
the dispatcher unwinds frames back to the installing frame
and branches to the handler's precomputed label with the
suspend's payload and a placeholder continuation pushed.

The reified continuation handed to the handler is one-shot:
its state is set to `Completed` so a guest that tries to
re-resume it traps. True re-resumable continuations need
frame snapshotting that hasn't been built yet.

### New surface

- `Wacs.Core.Runtime.Concurrency.ResumeHandlerFrame` — entry
  on the new `ExecContext.ActiveResumeHandlers` stack.
  Carries the handler array, precomputed branch targets,
  install frame depth, and the continuation reference.
- `Wacs.Core.Instructions.SuspensionDispatcher.TryHandle` —
  static helper the interpreter loop calls to consume a
  `SuspensionException` if any active handler matches.
- `InstResume.HandlerTargets` populated at Link time via
  `InstBranch.PrecomputeStack`.

### Behavior

- `resume`: pops the continuation + remaining args, pushes
  the cont's bound prefix args (from `cont.bind`), installs
  a `ResumeHandlerFrame`, invokes the inner function. On
  normal completion, `FunctionReturn` prunes the handler
  frame. The cont's state transitions
  Fresh → Running → Completed.
- `suspend`: throws `SuspensionException(tag, payload)`. If a
  matching `resume` handler is in scope, control unwinds to
  it and branches; otherwise the exception surfaces as a
  trap.
- `resume_throw` / `switch`: still `NotImplementedException`.

### Tests

- 17 round-trip tests still pass. The
  `Execute_throws_NotImplemented` theory shrinks to two cases
  (resume_throw / switch) — `InstResume.Execute` is exercised
  now.
- 507 Wacs.Core + 380 ComponentModel green overall.

### Async dispatch migration — design intent

The existing `IsAsync` / `ExecuteAsync` path (host functions
returning `Task<T>`) coexists with the new substrate
unchanged in this release. The unified model — host
`await Task` and wasm `suspend` routed through the same
`Continuation` + `SuspensionDispatcher` primitive — lands
with the Component Model async dispatcher; that's the work
that makes CLR `Task<T>` the canonical host async type.

## WACS 0.16.3 — Continuation runtime: data structures + cont.new / cont.bind / suspend

First runtime slice of the Stack Switching proposal. Introduces
the `Wacs.Core.Runtime.Concurrency` namespace, allocates
continuations on the Store, and wires execution for three of the
six opcodes. The remaining three (`resume`, `resume_throw`,
`switch`) still throw `NotImplementedException` until the
BlockTarget handler-frame integration lands.

### New runtime types

- `Wacs.Core.Runtime.Concurrency.ContInstance` — `IGcRef`-shaped
  record of a continuation (state machine: Fresh / Running /
  Suspended / Completed; carries the inner FuncAddr + bound
  prefix args + the continuation type index).
- `Wacs.Core.Runtime.Concurrency.SuspensionException` —
  `WasmRuntimeException` subclass that `suspend` throws to unwind
  the interpreter stack to the matching `resume` handler frame.
- `ContIdx` (`RefIdx`) — Store-side identifier; `Value`'s
  `(refType, IGcRef)` constructor now recognizes it alongside the
  existing struct / array / exn cases.
- `Store.AllocateContinuation(typeIdx, funcAddr)` allocator and
  `Store.GetContinuation(idx)` lookup.

### Opcode runtime

- `cont.new $ct` — pops a function reference, allocates a fresh
  `ContInstance` tied to that function, pushes a non-nullable
  `ContRef` value.
- `cont.bind $ct1 $ct2` — pops a fresh continuation and `bindCount`
  prefix args (computed from `ft1.params - ft2.params`),
  allocates a new continuation with prefix args prepended, and
  marks the source as `Completed` so it can't be reused.
- `suspend $tag` — pops the tag's parameter values and throws a
  `SuspensionException(tag, payload)`. Without a `resume` handler
  frame to catch it the exception currently surfaces as an
  unhandled `WasmRuntimeException`; the catch path lands with
  the BlockTarget integration in the next slice.

### Tests

- 17 round-trip + factory tests in `StackSwitchingInstructionTests`
  still pass; the `Execute_throws_NotImplemented` theory shrinks
  from six cases to three (resume / resume_throw / switch) now
  that the other three opcodes execute.
- 508 Wacs.Core + 380 Wacs.ComponentModel tests green overall.

## WACS 0.16.2 — Scrub PM annotations from Stack Switching code

Comment-only cleanup pass over the files added in 0.16.0 / 0.16.1:
removes phase/version labels and verification-status reminders
from production and test code. The deferred-validation comments
in `cont.bind` / `resume` / `switch` are rephrased to describe
what the validator does and does not check (delegating arity vs.
full structural typecheck) without naming a future implementation
slot. `NotImplementedException` messages on the six Execute
methods drop the implementation-roadmap pointer.

No behavior changes; 511 Wacs.Core + 380 ComponentModel tests
remain green.

## WACS 0.16.1 — Stack Switching: parser + validator

WASIp3 Phase 1.2 — wires binary parse / render / validation for
the six Stack Switching opcodes reserved in 0.16.0. Execute
throws `NotImplementedException` until Phase 1.3 lands the
Continuation runtime.

- New `Wacs.Core/Instructions/StackSwitching.cs` with
  `InstContNew` / `InstContBind` / `InstSuspend` / `InstResume`
  / `InstResumeThrow` / `InstSwitch`.
- `SpecFactory` dispatches `OpCode.ContNew`–`OpCode.Switch` to
  the new classes.
- `ByteCode` constants for the six opcodes.
- Validators check the static typing rules per the proposal
  (cont-type resolution, tag arity, handler labels/tags exist).
- New `StackSwitchHandler` struct models the `0x00 $tag $label`
  and `0x01 $tag $label` on-tag handler immediates inside
  `resume` / `resume_throw`.
- 20 new round-trip + dispatch tests in
  `Wacs.Core.Test/StackSwitchingInstructionTests.cs`.
- 511 Wacs.Core + 380 Wacs.ComponentModel tests green.

## WACS 0.16.0 — Stack Switching: type-system scaffolding

WASIp3 Phase 1.1 — first slice of the WebAssembly Stack
Switching proposal (https://github.com/WebAssembly/stack-switching).
Type-system and opcode reservations only; no behavior wired
yet. Byte assignments need re-verification against current
spec submodule HEAD before final ship.

- `HeapType.Cont` (0x68, -0x18) and `HeapType.NoCont` (0x75,
  -0x0b) added.
- `ValType.ContRef` / `NoCont` / `ContRefNN` / `NoContNN` added
  with full `IsSubType` / `TopHeapType` / `GetHeapType` /
  `Validate` coverage.
- New `ContType` (composite-type subclass wrapping a function-
  type index) parses via `CompType.ContCt = 0x5D`.
- Opcodes 0xE0–0xE5 reserved: `cont.new`, `cont.bind`,
  `suspend`, `resume`, `resume_throw`, `switch`. Instruction
  parsing / validation / execution land in subsequent Phase 1
  slices.

## WACS 0.15.24 — field-names and tag-names subsections

Adds parser and writer support for two more `name` custom-section
subsections that were previously unrecognized (and would have
thrown `FormatException` on any binary carrying them):

- **Field names** (id 10, from the GC proposal) — indirect map
  keyed by typeidx, then fieldidx. Exposed as
  `Module.NameSection.FieldNames` (`IndirectNameMap`).
- **Tag names** (id 11, from the exception-handling proposal) —
  flat name map keyed by tagidx. Exposed as
  `Module.NameSection.TagNames` (`NameMap`).

Names are preserved on the `NameSection` object only; this
change does not stamp `TagType.Id` or `FieldType.Id` (those
types don't carry id strings today). Round-trip tests added
against hand-built binaries.

## WACS 0.15.23 — extended-name-section label-names wire shape

Fixes the binary parser and writer for the label-names subsection
(id 3) of the `name` custom section. Per the extended-name-section
proposal, label names use an **indirect** name map
(funcidx → labelidx → name), not the flat name map WACS was
emitting and parsing. Any binary actually carrying label names
would round-trip incorrectly before this fix.

### What changed

- `Module.NameSubsection.LabelNameSubsection.Names` is now an
  `IndirectNameMap` (was `NameMap`). External consumers that read
  the property directly need to switch from `.NameAssocMap` to
  the indirect form (`labels[funcIdx].NameAssocMap[labelIdx]`).
- `BinaryModuleWriter` emits the subsection as an indirect map.
- New round-trip test covers the wire shape against a hand-built
  binary.

## WACS.ComponentModel.Harness.Runtime 0.7.0 / WACS.ComponentModel.Harness.Lib 0.27.0 — separate host handle space + Borrowed&lt;T&gt;

Layers an independent host-side handle space over the existing
WASM-side `ResourceHandleTable` (which stays rep-as-handle for
wit-bindgen 0.41 Rust guest compatibility). Adds a type-distinct
`Borrowed<T>` companion to the emitted Resource class so borrow
mishandling is caught at compile time, not at runtime.

### What changed

- **`WACS.ComponentModel.Harness.Runtime` 0.7.0** — new public
  `HostHandleTable` (auto-increment counter + freelist,
  `NewOwn`/`Rep`/`DropOwn`) and `readonly struct Borrowed<T>`
  (wraps the wasm rep, no `IDisposable`). Both AOT-clean.
- **`WACS.ComponentModel.Harness.Lib` 0.27.0** — emitted Resource
  class now has `(int hostHandle, int rep, Action<int> dtor,
  Action<int> drop, HostHandleTable hostTable)` constructor;
  `Handle` returns the host-side identity (decoupled from rep
  reuse) and a new `Rep` property exposes the wasm-side value
  for hand-rolled `Borrowed<R>` construction. `borrow<R>` in WIT
  maps to `Borrowed<TR>` in C# at all call boundaries (params
  and returns).

### Architecture

Two handle spaces, layered. The runtime keeps a wasm-side
`ResourceHandleTable` per resource (rep-as-handle, bound by
`CanonResourceBinder` as the `[resource-new]` / `[resource-drop]`
adapter target). The harness adds a `HostHandleTable` per
resource type on top — fresh handles allocated from an
auto-increment counter, recycled through a freelist on
`DropOwn`. User code sees only the host handle via
`Resource.Handle`; lower IL uses `Resource.Rep` to talk to wasm.

### Why two layers

- wit-bindgen 0.41 Rust guests assume `handle == rep` for
  exported resources (they don't import `[resource-rep]`, they
  dereference `handle` directly). Keeping the wasm side
  rep-as-handle preserves compatibility without forking the
  toolchain.
- Decoupling host-side identity gives `Resource.Handle` stable
  semantics independent of how the guest reuses rep values
  (which can be the same allocator address across drop/new
  pairs). Two `new()` calls that happen to receive the same rep
  still yield distinct host handles.
- Type-level borrow safety: `Borrowed<T>` has no `Dispose`, so
  user code that took a borrow can't accidentally release the
  lender's resource. Soundness-tightening on the host side —
  the wasm-side dtor invocation still goes through the canon
  `[resource-drop]` adapter unchanged.

### Deferred (scoped for follow-up)

- **Call-scope borrow invalidation** — refusing to use a
  `Borrowed<T>` after the lending call returned. Needs an
  `ExecContext` hook the runtime doesn't expose yet.
- **Cross-instance handle namespacing** — handing handles
  between composed component instances. Static composition via
  `wasm-tools compose` already works; dynamic composition would
  build on this v2 foundation.

### Tests

- 11 new `HostHandleTableTests` (fresh handles, freelist reuse,
  double-drop guard).
- 4 new `BorrowHarnessEmitTests` (Borrowed signature on params
  + returns, no Dispose, Handle+Rep both surface).
- All 380 `Wacs.ComponentModel.Test` tests green.
- All 3 resource fixtures green: `canon-resource-roundtrip`
  (now reports host handles 1, 2 with freelist reuse instead
  of rep heap addresses), `wit-harness-spike-resource-methods`
  (own params via `get_Rep`), `wit-harness-spike-resource-basic`
  (idempotent `Dispose` still no-op).

### Misc

- `Wacs.ComponentModel.Test.csproj` bumped to `net9.0` to
  reference `Wacs.ComponentModel.Harness.Lib`.
- `Spec.Test.csproj` excludes `canon-resource-roundtrip/**/*.cs`
  from its default glob (fixture ships its own `Generated.Validate`
  subproject — pre-existing oversight from 0.6.0).

## WACS.ComponentModel.Harness.Lib 0.26.1 — diagnostics for unimplemented niches (Item 3 close-out)

Tightens the error messages on two niches the harness emitter
doesn't yet implement, and documents the status:

- **Multi-return / named results** — verified that the WIT spec
  has dropped this in favor of `func() -> tuple<...>` /
  `func() -> record { ... }`; `wit-bindgen` 0.41 rejects the
  old `(a: T, b: U)` syntax at WIT parse time. The
  `BuildFunctionExport` throw is now annotated as a defensive
  guard rather than a TODO.
- **MAX_FLAT_PARAMS overflow** — beyond the BCL `Func<…>` /
  `Action<…>` arity ceiling, the canonical-ABI prescribes an
  indirect param area (single i32 ptr to a memory area
  holding the lowered values at canonical offsets). The
  harness emitter doesn't yet emit that mode; the diagnostic
  in `MakeInvokerDelegateType` now calls this out explicitly.
  Practical impact: real-world WIT exports rarely flatten past
  16 slots; the BCL `Func<,,,,,,,,,,,,,,,,>` arity-17 ceiling
  is well above what wit-bindgen produces in practice.

Item 5c (alt string encodings — UTF-16, Latin1+UTF-16) is
similarly deferred: `wit-bindgen` doesn't expose the
`string-encoding` canon option, so we can't build a test
fixture without going around the standard toolchain.
`StringCoding.LiftUtf8` / `LowerUtf8` are isolated per the
`feedback_js_string_externref` memory, so a future slice can
add the encoding switch as a new `HarnessOptions` knob plus
matching helper methods without touching the lift / lower IL
sites.

## WACS.ComponentModel.Harness.Lib 0.26.0 — variant/result mismatched-width join (Item 5a)

`result<T, E>` and `variant` lower now handle mismatched flat
slot widths between cases via the canonical-ABI join algorithm.

```wit
export upgrade: func(input: result<u32, u64>) -> u64;
```

`result<u32, u64>` flattens to `[i32 disc, i64]`. The Ok branch
lowers its u32 payload and the wrapper IL widens it to i64
(`Conv_U8`) before the invoker call; the Err branch already
matches.

### How it works

- `ComputeVariantJoinedSlots` no longer throws on mismatched
  per-position types — it calls the new `JoinSlotTypes(a, b)`
  helper which implements the canonical-ABI rule: equal → equal,
  `(i32, f32)` → i32, otherwise i64.
- New `EmitWidenLastSlot(il, caseSlots, joinedSlots)` is called
  after a case's payload lowering pushes its values. v1 widens
  only the trailing slot (the most common single-payload
  pattern); intermediate-slot mismatches throw at emit time.
- New `EmitJoinConvert(from, to)` covers `int → long`
  (`Conv_U8`), `float → double` (`Conv_R8`), `float → long`
  (BitConverter.SingleToInt32Bits + `Conv_U8`), `double → long`
  (BitConverter.DoubleToInt64Bits). Other combinations throw
  until they're needed.
- `AppendLoweredType`'s `CtResultType` branch now produces the
  joined slot shape rather than picking one side, so the
  invoker delegate type matches what the lower IL pushes.

### What changed

- **`WorldHarnessEmit.cs`**:
  - `ComputeVariantJoinedSlots` uses `JoinSlotTypes` instead of
    strict equality.
  - New `JoinSlotTypes`, `EmitJoinConvert`, `EmitWidenLastSlot`
    helpers.
  - `EmitLowerResultArg` computes the joined shape and emits
    widening after each branch's payload lowering.
  - `EmitLowerVariantArg` per-case body widens after its
    payload lowering.
  - `IsFlatLowerable` for `CtResultType` no longer requires
    `SlotsMatch`.
  - `AppendLoweredType` for `CtResultType` computes the joined
    shape across both sides.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-join-widening/`
  — `upgrade(result<u32, u64>) -> u64`. Ok(7) widens to i64
  and Rust adds 1 → 8. Err(5000) is already i64 and multiplies
  by 2 → 10000.

**31/31 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.33.0` →
`WACS-ComponentModel-v0.34.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.25.1 — multi-byte variant disc (Item 5b)

`EmitVariantLift` no longer refuses variants with more than
256 cases. Discriminator is now read at the canonical-ABI
width (1 / 2 / 4 bytes) via the appropriate
`MemoryHelpers.Read{U8,I16LE,I32LE}` helper, with `Conv_U2`
on the 16-bit path for proper unsigned widening. Lower side
already pushed `Ldc_I4 i` which is correct for any disc size
since wasm widens to i32 at the boundary.

No new fixture — variants with 257+ cases are pathological;
the change just unblocks them if they appear.

## WACS.ComponentModel.Harness.Lib 0.25.0 — lift list-element symmetry (Item 4)

`list<T>` lift now covers every flat-lowerable element type, not
just primitives + named records / variants. Closes the asymmetry
where lower handled `list<option>` / `list<tuple>` but lift threw.

```wit
export sparse-values: func() -> list<option<u32>>;
export pairs:         func() -> list<tuple<u32, string>>;
export signals:       func() -> list<signal>;          // signal is an enum
```

### How it works

`EmitLiftElementAt` gains cases for `CtEnumType`, `CtFlagsType`,
`CtListType` (nested), `CtOptionType`, `CtResultType`,
`CtTupleType`. For enum / flags the integer is read directly via
a new `EmitReadIntegerAtElement` helper. For nested lists the
element's `(ptr, count)` pair is read and the inner lift goes
through `EmitLiftListFromBase`. For option / result / tuple the
element address is stashed into a local and dispatched to a
new general-purpose `EmitLiftFromBase` walker.

`EmitLiftFromBase` mirrors `EmitLiftField` but takes the
memory + base ptr via locals instead of the fixed arg-slot
contract, so it can chain offsets from any starting address.

### What changed

- **`LiftEmit.cs`**:
  - `EmitLiftElementAt` adds the five missing element-type
    cases.
  - New `EmitReadIntegerAtElement`,
    `EmitElementPtrOffset` element-context helpers.
  - New `EmitLiftFromBase` parallel walker (memory + basePtr
    + offset addressing) with private helpers
    `EmitLiftPrimitiveFromBase`, `EmitReadIntegerAtBase`,
    `EmitLiftOptionFromBase`, `EmitLiftResultFromBase`,
    `EmitLiftTupleFromBase`.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-list-aggregate-lift/`
  — sparse-values (list&lt;option&lt;u32&gt;&gt;), pairs
  (list&lt;tuple&lt;u32, string&gt;&gt;), signals
  (list&lt;enum&gt;).

**30/30 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.32.0` →
`WACS-ComponentModel-v0.33.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.24.0 — resource methods (Slice E)

Resource methods — constructors, instance methods, static
methods — now emit as harness methods.

```wit
interface counter {
    resource counter {
        constructor(initial: u32);
        increment: func() -> u32;
        get-value: func() -> u32;
        merge: static func(a: u32, b: u32) -> u32;
    }
}
```

```csharp
public sealed class DemoHarness
{
    public Counter WacsResourceMethodsSpikeCounter_NewCounter(uint initial);
    public uint    WacsResourceMethodsSpikeCounter_Counter_Increment(Counter self);
    public uint    WacsResourceMethodsSpikeCounter_Counter_GetValue(Counter self);
    public uint    WacsResourceMethodsSpikeCounter_Counter_Merge(uint a, uint b);
}
```

### How it works

- **Constructor**: patched function-spec sets `Result = the
  resource type`, so the existing resource-return lift path
  (lift int handle → newobj `Bucket(handle, _drop)`) fires.
  PascalName uses `New<Resource>` form (e.g.
  `WacsResourceMethodsSpikeCounter_NewCounter`).
- **Instance method**: a synthetic `self: <ResourceType>` param
  is prepended to the function spec. The lower path extracts
  `self.Handle` (via the public getter — `_handle` field is
  private to the resource class so cross-class IL can't
  `Ldfld` it) and pushes it as the first lowered i32. Wasm-side
  name: `<iface>#[method]<resource>.<method>`.
- **Static method**: emits like a regular function on the
  harness; no `self` injection. Wasm-side name:
  `<iface>#[static]<resource>.<method>`.

### Layout note

For v1, resource methods land flat on the harness (taking the
resource as first arg for instance methods). The agreed nested
layout (`bucket.Read(len)` instead of `harness.Bucket_Read(b,
len)`) requires a back-ref pattern from the resource class to
the harness, which would force a multi-phase emission
restructure. Deferred to a future refactor; the wasm-side
plumbing is identical so the surface change is purely user-
facing.

### What changed

- **`WorldHarnessEmit.cs`**:
  - `BuildInterfaceExports` walks `iface.Types`' resources and
    for each `CtResourceMethod` builds a `FunctionExport` with
    the appropriate wasm name (`[constructor]<name>` /
    `[method]<name>.<m>` / `[static]<name>.<m>`) and PascalName.
  - Constructor patches `Result = res` so the resource-return
    lift fires after the invoker call.
  - Instance methods get `self: <res>` prepended via new
    `PrependSelfParam` helper.
  - Lower path for resource args switched from private
    `_handle` Ldfld to public `Handle` Callvirt — `_handle`
    isn't visible to cross-class wrapper IL.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-resource-methods/`
  — `counter` resource with constructor, two instance methods
  (increment, get-value), and a static method (merge).
  Validator builds a counter, increments three times,
  reads value, calls merge, disposes.

**29/29 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.31.0` →
`WACS-ComponentModel-v0.32.0` (minor — capability shift on
Harness.Lib closing the v1 interface-export arc).

## WACS.ComponentModel.Harness.Lib 0.23.0 — resource scaffolding (Slice D)

Resources can now flow through the harness emit-side. Each
WIT `resource <name>` declaration emits a sealed CLR class
in the interface's namespace:

```wit
interface store {
    resource bucket;
    open: func(name: string) -> bucket;
}
```

```csharp
namespace WitHarnessSpike.ResourceBasic.Generated.WacsResourceBasicSpikeStore
{
    public sealed class Bucket : IDisposable
    {
        public int Handle { get; }
        public void Dispose();
    }
}

// In the harness:
public Bucket WacsResourceBasicSpikeStore_Open(string name) => …;
```

### How it works

- **Resource class** emits with `_handle` (int) + `_drop`
  (Action&lt;int&gt;) fields, internal `(handle, drop)` ctor,
  public `Handle` getter, and `Dispose()` that calls drop
  once and zeros the handle (subsequent Dispose calls no-op).
- **Per-resource drop field** on the harness: `_drop_<slug>`
  holds the wasm-side drop invoker. LoadFrom resolves
  `<iface>#[dtor]<name>` (the guest's destructor) and binds
  it. Wrapper IL that lifts a resource return pushes this
  field before `newobj`-ing the resource class.
- **Resource lift / lower**:
  - Lift: invoker returns an int (handle), wrapper IL
    constructs `new Bucket(handle, _drop_bucket)`.
  - Lower: extract `_handle` from the resource instance and
    push as int (Slice E will exercise this with method params).
- **`MapPrimitiveToClrType`** / `IsFlatLowerable` /
  `AppendLoweredType` treat `CtResourceType` / `CtOwnType` /
  `CtBorrowType` as i32 handles at the wasm boundary.
- New `TryGetResource(t)` helper drills through `own<R>` /
  `borrow<R>` / bare resource refs to the underlying
  `CtResourceType` so lift/lower can dispatch.

### Runtime caveat

WACS's component runtime doesn't yet implement the
canonical-ABI exported-resource handle table — `canon
resource.new` / `resource.rep` are parsed (for index-space
accounting) but not constructed as runtime adapters. The
harness wires its drop call to the core `<iface>#[dtor]<name>`
export directly, skipping the table lookup.

For the Slice D fixture to instantiate, the validator's
`bindImports` callback stubs the two component-level
`[export]<iface>.[resource-new|drop]<name>` host imports
using a rep-as-handle 1:1 mapping (handle == rep, so the
dtor receives a real rep pointer when invoked). When WACS
implements proper resource handle tables, these stubs go
away and the harness can call `[resource-drop]<name>` (the
canonical adapter that handles the table lookup) instead.

### What changed

- **`WitTypeEmit.cs`** —
  - `TypeRegistry` gains `Resources`, `ResourceCtors`,
    `ResourceHandleFields`, `HarnessDropFields` dictionaries.
  - `EmitWorldTypes` registers `CtResourceType` shells in
    Pass 1 alongside records/variants/enums/flags.
  - New `PopulateResource(tb, res, registry)` emits the
    sealed class: `_handle` + `_drop` fields, internal
    (handle, drop) ctor, public `Dispose()` with idempotent
    drop-and-zero, public `Handle` getter.
  - `MapClrType` resolves `CtResourceType` / `CtOwnType` /
    `CtBorrowType` to the resource's `TypeBuilder`.
- **`CanonicalAbi.cs`** — `Layout` treats resource handle
  types as `(4, 4)` (a single i32).
- **`WorldHarnessEmit.cs`** —
  - `BuildInterfaceExports` drops the resource refusal.
  - New `ResourceDrop` struct tracks per-resource drop
    metadata.
  - Per-export field allocation now also walks
    interface-export resources and creates `_drop_*` fields.
  - `EmitConstructor` + `EmitLoadFrom` thread the drop
    invokers through.
  - `MapPrimitiveToClrType` / `IsFlatLowerable` /
    `AppendLoweredType` accept resource-like types.
  - `EmitFlattenedArg` extracts `_handle` for resource
    params (lower path).
  - `EmitFlatLowered` direct-return branch lifts a resource
    int into a CLR class instance with the drop field.
  - New `TryGetResource(t)` helper.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-resource-basic/`
  — `store` interface declares an opaque `bucket` resource
  + `open` free function. Validator opens, asserts non-zero
  handle, disposes (handle zeros, no exception), and runs
  through a `using` block.

**28/28 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.30.0` →
`WACS-ComponentModel-v0.31.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.22.0 — interface-level types (Slice C)

Records / variants / enums / flags declared inside an exported
interface emit into the interface's own C# sub-namespace,
keeping interface-local names from colliding with world-level
names (or other interfaces' same-named types).

```wit
interface geometry {
    record point { x: u32, y: u32 }
    enum quadrant { ne, nw, sw, se }
    variant region {
        empty,
        point-only(point),
        labeled(string),
    }
    classify: func(p: point) -> quadrant;
    describe: func(r: region) -> string;
}

world cartographer { export geometry; }
```

Emits:

```
WitHarnessSpike.InterfaceTypes.Generated
├── CartographerHarness
│     ├── WacsInterfaceTypesSpikeGeometry_Classify(Point) -> Quadrant
│     └── WacsInterfaceTypesSpikeGeometry_Describe(Region) -> string
└── WacsInterfaceTypesSpikeGeometry
      ├── Point
      ├── Quadrant
      └── Region (with nested Empty / PointOnly / Labeled case classes)
```

### Structural refactor

`TypeRegistry` flipped from string-keyed to **structural-type-
reference-keyed** for all dictionaries (`Records`, `Variants`,
`Enums`, `Flags`, `RecordCtors`, `RecordGetters`, `VariantCases`,
`VariantCaseCtors`). Two interfaces both declaring `error` no
longer collide because the CtRecordType / CtVariantType
references are unique per WIT declaration.

Same refactor for `liftMethods` dictionary in `LiftEmit` — now
`Dictionary<CtValType, MethodBuilder>` keyed by the structural
type. Lift method names use the TypeBuilder's `FullName` (with
`.` → `_`) to keep them unique even when type short names match
across interfaces.

`EmitWorldTypes` now walks an `EnumerateAllTypes(world, opts)`
sequence that yields `(CtNamedType, csharpNamespace)` pairs —
world types get `opts.Namespace`, interface-export types get
`opts.Namespace + "." + HarnessNaming.InterfaceSegment(iface)`.

### What changed

- **`WitTypeEmit.cs`**:
  - `TypeRegistry` keys flipped to structural-type references.
  - `EmitWorldTypes` walks `EnumerateAllTypes` so interface
    types are emitted into their own sub-namespace.
  - New `EnumerateAllTypes(world, opts)` helper.
  - Dead `PrimitiveAliases` write site removed.
- **`LiftEmit.cs`** — lift methods keyed by structural type
  reference; unique method-name synthesis via
  `UniqueLiftMethodName(tb)`.
- **`WorldHarnessEmit.cs`**:
  - All `registry.X[name]` sites changed to `registry.X[type]`.
  - `BuildInterfaceExports` drops the interface-types refusal
    (now handled by Slice C); resource refusal remains until
    Slice D.
  - `MapPrimitiveToClrType` widened to accept `CtEnumType` /
    `CtFlagsType` (lowered to `int`).
  - `EmitFlatLowered` direct-return branch handles enum / flags
    (invoker's int is stack-compatible with the wrapper's
    enum-typed Ret slot).
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-interface-types/`
  — `geometry` interface with `point` (record), `quadrant`
  (enum direct return), `region` (variant with payload + unit
  cases). Validator asserts all three types live in the
  expected sub-namespace and round-trip through both exports.

**27/27 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.29.0` →
`WACS-ComponentModel-v0.30.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.21.0 — function-only interface exports (Slices A + B)

Interface exports now flow through to the harness. Function-only
interfaces (no resources, no own type declarations) surface as
flat methods on the harness class, prefixed with the interface's
PascalCase segment. Resource + interface-level-type support lands
in Slices C–E.

```wit
package wacs:interface-export-spike;

interface ops {
    add: func(a: u32, b: u32) -> u32;
    swap: func(a: u32, b: u32) -> tuple<u32, u32>;
}

world calculator {
    export ops;                       // function-only interface
    export bake: func() -> u32;       // free function
}
```

Emits a harness with three flat methods:

```csharp
public sealed class CalculatorHarness : ICalculator
{
    public uint Bake() => …;
    public uint WacsInterfaceExportSpikeOps_Add(uint a, uint b) => …;
    public ValueTuple<uint, uint> WacsInterfaceExportSpikeOps_Swap(uint a, uint b) => …;
}
```

Per-interface namespace segments (`WasiCliRun`, etc.) collapse the
multi-part package + interface name into one Pascal token — this
contrasts with the transpiler-side `NameMangler.InterfaceNamespace`
which uses full dotted namespaces. Resource-bearing interfaces
will get nested `Exports` classes in a later slice; that's where
the per-interface segment also becomes the C# namespace for
interface-declared types.

### How it works

- **`HarnessNaming.InterfaceSegment(iface)`** — produces
  `WacsInterfaceExportSpikeOps` from `wacs:interface-export-spike/ops`.
- **`HarnessNaming.InterfaceFunctionPascal(iface, fnKebab)`** —
  joins segment + `_` + Pascal(fn) for the C# method name.
- **`HarnessNaming.InterfaceFunctionSlug(iface, fnKebab)`** —
  C#-safe field slug (replaces `-`/`/`/`:`/`@`/`#` with `_`),
  used for `_invoke_*` / `_post_*` field names.
- **`FunctionExport.WasmName`** (new field) — the exact wasm-side
  export string (`<iface-base>#<fn-kebab>` for interface
  functions, plain witName for free functions). The
  `RequireFunctionExport` and `cabi_post_*` lookup sites use
  this instead of `Name`, which now carries only the C#-safe
  slug.

### What changed

- **`Wacs.ComponentModel.Harness.Lib/HarnessNaming.cs`** — new
  helper module.
- **`WorldHarnessEmit.cs`**:
  - `FunctionExport` gains `WasmName` (separates wasm-side
    name from C#-safe slug).
  - `BuildFunctionExport` accepts optional `wasmName`,
    `pascalName`, `slug` for interface callers.
  - New `BuildInterfaceExports` walks an interface's functions
    and builds one `FunctionExport` per. Refuses interfaces
    that declare their own types (Slice C) or resources
    (Slice D).
  - Exports loop dispatches on `CtExternFunc` /
    `CtExternInterfaceRef` / `CtExternInlineInterface`.
  - `EmitWorldInterface` mirrors the dispatch so the `IWorld`
    C# interface includes interface-export functions.
  - Wasm-side export lookups (`RequireFunctionExport`,
    `cabi_post_*`) read `fe.WasmName`.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-interface-export/`
  — `calculator` world with an `ops` interface (`add`, `swap`)
  plus free function `bake`.

**26/26 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.28.0` →
`WACS-ComponentModel-v0.29.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.20.0 — option/tuple of aggregate-inner direct PARAMS

option / tuple direct params with aggregate inner types
(list, record, nested tuple) now round-trip through the lower
path:

```wit
record line { text: string, weight: u32 }

export sum-or-default: func(maybe-values: option<list<u32>>, fallback: u32) -> u32;
export format-line:    func(maybe-line: option<line>)                      -> string;
export weighted-sum:   func(p: tuple<u32, list<u32>>)                      -> u32;
```

### How it works

- **`EmitLowerInnerFromLocal`** (used by option / result / variant
  payload lower) gains aggregate dispatch: `CtListType` →
  `EmitLowerListFromLocal`, `CtRecordType` → walk the
  record's getters via `EmitFlattenSubRecordField`,
  `CtTupleType` → `EmitFlattenLocal`. The registry param is
  now threaded through (was nullable before — now passed by
  every caller).
- **Top-level `EmitFlattenedArg` tuple branch** simplified: stash
  the arg into a typed `ValueTuple<…>` local and dispatch
  through `EmitFlattenLocal`, which now handles every element
  type — primitives, strings, enums, flags, lists, nested
  records, nested tuples. Removes the duplicated element-by-
  element lower IL from the tuple arg branch.
- **`EmitFlattenLocal` tuple branch** extended to handle list /
  record / nested-tuple elements: extract the element via the
  WitTupleAccess accessor into a typed local and recurse via
  `EmitFlattenLocal`.

### What changed

- **`WorldHarnessEmit.cs`**:
  - `EmitLowerInnerFromLocal` dispatches list / record / tuple
    inner types when the registry is supplied. All callers
    (option / result / variant payload lower) thread it through.
  - Top-level tuple-arg lower in `EmitFlattenedArg` replaced
    with stash-to-local + `EmitFlattenLocal` dispatch.
  - `EmitFlattenLocal` tuple branch handles non-primitive
    element types by recursing.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-aggregate-inner-params/`
  — `sum-or-default(option<list<u32>>, u32)`,
  `format-line(option<line>)`, `weighted-sum(tuple<u32,
  list<u32>>)`. Five distinct cases including Some / None and
  list-in-tuple.

**25/25 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.27.0` →
`WACS-ComponentModel-v0.28.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.19.0 — `list<tuple>` + `list<option>` as direct PARAMS

Two more list-element shapes round-trip through the lower path:

```wit
export tuple-list-sum:    func(pairs: list<tuple<u32, u32>>)     -> u32;
export tuple-list-format: func(items: list<tuple<u32, string>>)  -> string;
export option-list-sum:   func(values: list<option<u32>>)        -> u32;
```

### How it works

`EmitLowerListElement` gains two new branches:

- **`CtTupleType` (list element):** writes each tuple element to
  the per-element slot at its in-tuple offset (computed via
  `CanonicalAbi.TupleElementOffsets`). Per-element-type
  dispatch picks the matching `MemoryHelpers.Write*` helper;
  string elements lower through `LowerUtf8` first and the
  `(ptr, len)` pair is written into the slot's offset and
  offset+4.
- **`CtOptionType` (list element):** writes 1-byte disc at slot
  offset 0, then for Some writes the inner value at
  `align_up(1, T_align)` via the new `EmitWriteSimpleAt`
  helper. None just writes disc=0 — the payload area stays as
  the realloc'd zero-bytes.

The new `EmitLdelemForType` helper picks the right `Ldelem_*`
opcode based on element CLR type (covers struct elements like
`Nullable<T>` and `ValueTuple<...>` via the generic `Ldelem,
elemType` form).

### What changed

- **`WorldHarnessEmit.cs`**:
  - `EmitLowerListElement` adds `CtTupleType` and `CtOptionType`
    cases.
  - New helpers: `EmitLowerTupleElement`,
    `EmitWriteTupleElementPrim`, `EmitLowerOptionElement`,
    `EmitWriteSimpleAt`, `EmitLdelemForType`.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-list-aggregate-params/`
  — three exports covering `list<tuple<u32, u32>>` (pure
  numeric tuple), `list<tuple<u32, string>>` (mixed-element
  tuple with realloc per element), and `list<option<u32>>`
  (mixed None / Some).

**24/24 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.26.0` →
`WACS-ComponentModel-v0.27.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.18.0 — `variant` + `result<T,E>` as direct PARAMS

Variants and results can now flow as direct params per the
canonical-ABI flat shape `(i32 disc, …joined-payload)`. This
closes out the most architecturally complex remaining gap on
the lower path.

```wit
variant signal { silence, ping, message(string) }

export describe-signal: func(s: signal) -> string;
export prefer-ok:       func(input: result<u32, u32>)       -> u32;
export render:          func(input: result<string, string>) -> string;
export note:            func(input: result)                 -> u32;
```

### How it works

- **Result lower** (`EmitLowerResultArg`):
  reads `WitResult<TOk, TErr>.IsOk` via `Call` on the getter,
  branches; the Ok branch pushes `disc=0` then runs the inner
  lower for the present side (or zeros for elided); same shape
  for Err on `disc=1`. v1 requires matching flat shape between
  Ok and Err sides (or one elided) — full join-algorithm
  widening is deferred.
- **Variant lower** (`EmitLowerVariantArg`):
  per-case `isinst` dispatch on the variant base reference;
  each matched case pushes its ordinal disc, then loads
  `Value` from the case subclass (if payload-bearing), stashes
  it to a local, and runs the per-type lower. Trailing joined
  slots the case doesn't fill get zero-padded via
  `EmitDefaultForSlot`.
- **`ComputeVariantJoinedSlots`** — strict join algorithm: at
  each slot position, every case that has a slot at that
  position must contribute the same CLR slot type. Throws
  `NotSupportedException` on mismatched cases (the IsFlatLowerable
  check catches this early so it surfaces at harness-emit
  time, not at JIT).

### What changed

- **`WorldHarnessEmit.cs`**:
  - `IsFlatLowerable` accepts `CtResultType` (matching flat
    shapes / one elided) and `CtVariantType` (via the join
    check).
  - `AppendLoweredType` flattens result + variant per the
    above rules.
  - New helpers: `EmitLowerResultArg`, `EmitLowerVariantArg`,
    `ComputeVariantJoinedSlots`, `SlotsMatch`.
- **Fixtures**:
  - `Spec.Test/components/fixtures/wit-harness-spike-result-params/`
    — `prefer-ok(result<u32, u32>)`, `render(result<string,
    string>)`, `note(result)` covering matching u32 widths,
    matching string widths, and both-elided.
  - `Spec.Test/components/fixtures/wit-harness-spike-variant-params/`
    — `describe-signal(signal)` where signal has two unit
    cases and one string-payload case; exercises the zero-pad
    path for unit cases.

**23/23 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.25.0` →
`WACS-ComponentModel-v0.26.0` (minor — capability shift on
Harness.Lib closing the variant/result lower gap).

## WACS.ComponentModel.Harness.Lib 0.17.0 — `list<record>` as direct PARAM

Per-element canonical layout writes — `list<record>` can now flow
as a direct param, with each element's record laid out in linear
memory per the canonical-ABI offsets and strings inside the record
recursively lowered via `cabi_realloc`.

```wit
record item { sku: string, qty: u32 }
export inventory-value: func(items: list<item>, unit-price: u32) -> u32;
export inventory-summary: func(items: list<item>) -> string;
```

### How it works

`EmitLowerListElement` gained a `CtRecordType` branch that pulls
the per-index element via `Ldelem_Ref`, stashes to a typed local,
then walks each record field. For each primitive field, it writes
via the matching `MemoryHelpers.Write*` helper at the field's
canonical offset within the per-element slot. String fields lower
through `LowerUtf8` first and the produced `(ptr, len)` pair is
written into the slot's offset and offset+4.

### What changed

- **`WorldHarnessEmit.cs`**:
  - `EmitLowerListElement` adds a `CtRecordType` case calling
    new `EmitLowerRecordElement` + per-field
    `EmitLowerRecordFieldToMemory`. Signature widened to thread
    `TypeRegistry` through (needed for the record's CLR-type
    lookup and getters).
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-list-record-param/`
  — `item { sku: string, qty: u32 }`; `inventory-value` returns
  sum of qty * unit-price, `inventory-summary` formats each
  element. Tests non-empty + empty list paths.

**21/21 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.24.0` →
`WACS-ComponentModel-v0.25.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.16.0 — `option<T>` as direct PARAM

`option<T>` is now a valid direct param shape. The canonical-ABI
flat lowering for `option<T>` is `(i32 disc, T_flat…)`:

```wit
export double-or: func(value: option<u32>, fallback: u32) -> u32;
export greet:     func(name: option<string>) -> string;
```

### How it works

- `IsFlatLowerable` accepts options whose inner type is itself
  flat-lowerable.
- `AppendLoweredType` flattens an option to `[int (disc), ...T_flat]`.
- `EmitLowerOptionArg` branches on the arg's presence
  (`Nullable<T>.HasValue` for value types, `!= null` for
  reference types). The Some path pushes `disc=1` then runs
  the inner lower (re-using LowerUtf8 for strings, direct
  `Ldloc` for primitives/enums/flags). The None path pushes
  `disc=0` followed by zero-defaults for each inner flat slot
  (via the new `EmitDefaultForSlot` helper covering int / long
  / float / double).
- New `EmitLdarga` helper for loading struct arg addresses
  (needed for `Nullable<T>.HasValue` and `.Value`).

### What changed

- **`WorldHarnessEmit.cs`**:
  - `IsFlatLowerable` + `AppendLoweredType` extend to
    `CtOptionType`.
  - New `EmitLowerOptionArg`, `EmitLowerInnerFromLocal`,
    `EmitDefaultForSlot`, `EmitLdarga` helpers.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-option-params/`
  — `double-or(option<u32>, u32) -> u32` (value-type inner)
  and `greet(option<string>) -> string` (reference-type inner
  with string lower in the Some path).

**20/20 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.23.0` →
`WACS-ComponentModel-v0.24.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.15.0 — records-of-aggregates as PARAMS + full Func/Action arity range

The flat-lowered record param path now handles fields of any
supported flat-lowerable type — strings, lists, enums, flags,
tuples, and nested records. Previously each field had to be a
primitive.

```wit
record dimensions { width: u32, height: u32 }
record parcel {
    name: string,
    tags: list<string>,
    size: dimensions,
}
export describe: func(p: parcel) -> string;
```

### How it works

- `EmitFlattenedArg`'s record branch now dispatches each field
  through new `EmitFlattenRecordField`, which: for primitives /
  enums / flags just calls the getter and pushes the result;
  for strings calls the getter then runs `LowerUtf8`; for lists
  / nested records / tuples stashes the getter's result into a
  typed local and re-dispatches via `EmitFlattenLocal`. The
  local-based dispatch mirrors the arg-slot-based one but reads
  from a fresh local — needed so we can repeatedly access the
  same nested record's fields via the same instance, rather
  than calling the outer getter multiple times.
- `EmitLowerListFromLocal` mirrors `EmitLowerListArg` but takes
  a local for the array source rather than an arg index. Used
  when a list comes out of a record field or tuple element.
- `MakeInvokerDelegateType` widened to cover every `Func<…>` /
  `Action<…>` arity the BCL exposes (Action arity 0..16, Func
  arity 1..17 type args). Records-with-strings + lists quickly
  blow past the old 4-param ceiling — a parcel with name +
  tags + size lowers to 6 i32s of params + 1 i32 of retArea.

### What changed

- **`WorldHarnessEmit.cs`**:
  - `EmitFlattenedArg` record branch routes each field through
    new `EmitFlattenRecordField`.
  - New helpers: `EmitFlattenRecordField`, `EmitFlattenLocal`,
    `EmitFlattenSubRecordField`, `EmitLowerListFromLocal`.
  - `MakeInvokerDelegateType` replaced its 4-param ceiling with
    `OpenActions` / `OpenFuncs` lookup tables covering the full
    BCL Action / Func arity range.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-record-params/`
  — `describe(parcel)` where parcel = (string, list&lt;string&gt;,
  nested-record). Lowers to 6 i32 slots; returns a string
  formatted from all fields.

**19/19 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.22.0` →
`WACS-ComponentModel-v0.23.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.14.0 / WACS.ComponentModel.Harness.Runtime 0.6.0 — enum + flags + tuple direct PARAMS

Direct params can now be `enum`, `flags`, or `tuple<...>` — they
flatten to one or more i32 slots on the invoker stack.

```wit
world classifier {
    enum priority { low, normal, high }
    flags channels { email, sms, push, webhook }

    export rank: func(p: priority, ch: channels) -> u32;
    export render-point: func(p: tuple<u32, u32, string>) -> string;
}
```

- **Enum / flags** flatten to one i32 — `Ldarg` pushes the
  CLR enum value (whose stack repr is the underlying int).
- **Tuple** flattens to the concatenation of its elements'
  flat lowerings. For primitive / enum / flag / string
  elements, the wrapper IL reads each item via a closed
  generic helper on `WitTupleAccess` (e.g.
  `WitTupleAccess.Item3<uint, uint, string>`) — calling a
  generic static method side-steps a PersistedAssemblyBuilder
  bug where `Ldfld` against a closed runtime ValueTuple field
  serializes the open generic's token, producing
  `MissingFieldException` at JIT time.

### What changed

- **`Wacs.ComponentModel.Harness.Runtime`** — new
  `WitTupleAccess` static class with `Item1..Item7` generic
  accessors for `ValueTuple<...>` arities 1..7.
- **`WorldHarnessEmit.cs`**:
  - `IsFlatLowerable` accepts enum / flags / tuple-of-flat.
  - `AppendLoweredType` flattens enum / flags to one i32, tuple
    to recursed-per-element.
  - `EmitFlattenedArg` dispatches enum / flags to `Ldarg`,
    tuples to per-item `WitTupleAccess.ItemN` accessor calls
    (with inline `LowerUtf8` per string element).
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-aggregate-params/`
  — `rank(priority, channels)` exercises enum + flags as
  separate params, `render-point(tuple<u32, u32, string>)`
  exercises tuple-of-mixed (two primitives + a string) as a
  single param.

**18/18 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.21.0` →
`WACS-ComponentModel-v0.22.0` (minor — capability shift on
Harness.Lib + new Harness.Runtime public API).

## WACS.ComponentModel.Harness.Lib 0.13.0 — direct `option<T>` / `result<T,E>` / `tuple<...>` returns

Anonymous aggregate types are now valid as the direct return of
an export — no record wrapper required.

```wit
world directs {
    export find-positive:    func(value: s32)    -> option<u32>;
    export ensure-non-empty: func(value: string) -> option<string>;
    export parse-int:        func(text: string)  -> result<u32, string>;
    export coord-named:      func(x: u32, y: u32, label: string) -> tuple<u32, u32, string>;
}
```

### How it works

The named-record / named-variant return path lifts via the
per-type `Lift{Name}` static method registered during
`LiftEmit.EmitLifts`. Anonymous aggregates have no name to
register under, so we emit a synthetic per-export
`Lift__ret_<exportName>` static method that calls
`LiftEmit.EmitLiftField` at offset 0 over the retArea pointer.
The wrapper then takes the same `EmitLiftReturnViaRetArea`
path the named-type returns use — including the
`NeedsPostReturn` cabi_post cleanup when the type transitively
carries strings or lists.

### What changed

- **`WorldHarnessEmit.cs`**:
  - `BuildFunctionExport` widens its indirect-aggregate-return
    case to also accept `CtOptionType` / `CtResultType` /
    `CtTupleType` — they get `LoweredReturn = int` and the
    transitive `NeedsPostReturn` check.
  - New per-export emission step: for each export whose return
    derefs to option / result / tuple, define
    `Lift__ret_<name>(MemoryInstance, int)` private static
    method that calls `LiftEmit.EmitLiftField(ret, 0, …)` and
    returns.
  - `EmitFlatLowered` dispatches option/result/tuple returns to
    `EmitLiftReturnViaRetArea` using the new
    `fe.ReturnLiftMethod`.
  - `FunctionExport` carries the optional `ReturnLiftMethod`
    reference.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-direct-returns/`
  — four exports covering direct `option<u32>` (numeric),
  `option<string>` (reference + string-bearing cabi_post),
  `result<u32, string>` (Ok + Err with realloc'd error string),
  and `tuple<u32, u32, string>` (mixed value+ref tuple).

**17/17 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.20.0` →
`WACS-ComponentModel-v0.21.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.12.0 — strings + lists in PARAMS (lower path)

Harness emitter now lowers `string` and `list<T>` arguments on the
way IN to a wasm export. The existing `EmitStringInStringOut`
special path handled exactly one shape (`func(s: string) -> string`);
this slice generalises the lower side so the same lift/lower
plumbing covers strings + lists in arbitrary param positions.

```wit
world repeater {
    export shout: func(name: string, count: u32) -> string;
    export length-of: func(text: string) -> u32;
    export sum: func(values: list<u32>) -> u32;
    export total-chars: func(words: list<string>) -> u32;
}
```

### How it works

- **String params** → call `StringCoding.LowerUtf8(memory, str,
  cabi_realloc, out ptr, out len)`, push `(ptr, len)` onto the
  invoker's argument stack. Memory allocated by `cabi_realloc`
  is conceptually owned by the call site — wasm-side allocator
  reclaims it on its own schedule, matching the existing
  string-in-string-out path.
- **List params** (`list<T>`) → call `cabi_realloc(0, 0, elemAlign,
  count * elemSize)` to get a base pointer, walk each element
  writing it into linear memory via the matching `MemoryHelpers.Write*`
  helper (`WriteU8`/`WriteI16LE`/`WriteI32LE`/`WriteI64LE`/
  `WriteF32LE`/`WriteF64LE`), push `(basePtr, count)`. For
  `list<string>`, each element recurses through `LowerUtf8` and
  the produced `(innerPtr, innerLen)` pair is written into the
  per-element slot.

### What changed

- **`WorldHarnessEmit.cs`**:
  - `IsFlatLowerable` now accepts strings and lists (recursively
    on the element type).
  - `AppendLoweredType` flattens `list<T>` to two i32s (ptr,
    count); strings already flattened that way.
  - `EmitFlatLowered` threads `reallocField` through to
    `EmitFlattenedArg`.
  - `EmitFlattenedArg` dispatches string params to new
    `EmitLowerStringArg` and list params to new
    `EmitLowerListArg`.
  - `EmitLowerListArg` emits the allocate-loop-push pattern,
    delegating to `EmitLowerListElement` for each WIT element
    width (primitives + string).
  - New `MemoryHelpers_Write*` MethodInfo statics (`WriteU8`,
    `WriteI16LE`, `WriteI32LE`, `WriteI64LE`, `WriteF32LE`,
    `WriteF64LE`).
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-lower-params/`
  — `shout`, `length-of`, `sum`, `total-chars`. Covers
  multi-string params, string + primitive return, `list<u32>`,
  zero-length list lower, and `list<string>` (the most complex
  per-element path).

**16/16 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.19.0` →
`WACS-ComponentModel-v0.20.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.11.0 / WACS.ComponentModel.Harness.Runtime 0.5.0 — `result<T,E>` + `tuple<...>`

Harness emitter handles WIT `result<T, E>` and `tuple<T1, T2, …>`,
both with their lift IL plus CLR-type surface.

### `tuple<…>`

```wit
record entry {
    coord: tuple<u32, u32>,
    labeled: tuple<string, u32>,
}
```

- **CLR mapping**: closed `System.ValueTuple<T1, T2, …>` for
  arities 1..7. The BCL ValueTuple struct gives us positional
  `.Item1`/`.Item2`/… access matching WIT tuple semantics with
  no per-type emission needed.
- **Layout**: identical packing rule to records (positional
  fields with per-element alignment padding); shared via the
  new `TupleElementOffsets` helper alongside the existing
  `RecordFieldOffsets`.
- **Lift**: walk per-element offsets, push each element on the
  stack via `EmitLiftField`, then `newobj
  ValueTuple<…>..ctor(T1, T2, …)`.

### `result<T, E>`

```wit
record outcome {
    from-positive: result<u32, string>,
    from-negative: result<u32, string>,
    empty-check:   result,
}
```

- **CLR mapping**: new `WitResult<TOk, TErr>` struct in
  `Wacs.ComponentModel.Harness.Runtime`. Elided sides
  (`result`, `result<T>`, `result<_, E>`) substitute
  `System.ValueTuple` (the empty struct) for the missing side
  — no separate sentinel type needed.
- **Layout**: 2-case-variant shape (1-byte disc + `max(ok_size,
  err_size)` at the aligned payload offset). Elided sides
  contribute size 0 / align 1.
- **Lift**: read disc, branch on `0` (Ok) / `1` (Err); for each
  present side lift the payload at the aligned offset and call
  `WitResult<…>.Ok(T)` / `Err(E)` static factory; for elided
  sides push `default(ValueTuple)`.

### What changed

- **`Wacs.ComponentModel.Harness.Runtime`** — new
  `WitResult<TOk, TErr>` public struct (`IsOk`, `OkValue`,
  `ErrValue`, static `Ok`/`Err` factories,
  `ToString → Ok(...)/Err(...)`).
- **`CanonicalAbi.cs`** — `Layout` handles `CtTupleType` (via
  new `LayoutTuple` helper, mirroring `LayoutRecord`) and
  `CtResultType` (2-case variant shape). Adds
  `TupleElementOffsets` public helper.
- **`WitTypeEmit.cs`** — `MapClrType` returns the closed
  `ValueTuple<…>` for tuples and the closed `WitResult<TOk,
  TErr>` for results.
- **`LiftEmit.cs`** — `EmitLiftField` dispatches `CtTupleType`
  to new `EmitLiftTuple` (per-element lift +
  `newobj ValueTuple<…>..ctor`) and `CtResultType` to new
  `EmitLiftResult` (disc branch + factory calls + elided-side
  `default(ValueTuple)` via new `EmitDefaultValueTuple`).
- **`WorldHarnessEmit.cs`** — `ContainsStringOrList` recurses
  into tuple elements + result Ok/Err sides, so cabi_post
  correctly walks tuples / results carrying strings or lists.
- **Fixtures**:
  - `Spec.Test/components/fixtures/wit-harness-spike-tuple/` —
    `tuple<u32, u32>` + `tuple<string, u32>` inside a record;
    asserts both numeric tuple and string-bearing tuple lift
    plus the CLR types match `ValueTuple<,>`.
  - `Spec.Test/components/fixtures/wit-harness-spike-result/` —
    Ok payload (`result<u32, string>` → `Ok(42)`), Err payload
    (`Err("not a positive integer")`), and the empty-elided
    form (`result` → `WitResult<ValueTuple, ValueTuple>`).

**15/15 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.18.0` →
`WACS-ComponentModel-v0.19.0` (minor — capability shift on
Harness.Lib + new Harness.Runtime public API).

## WACS.ComponentModel.Harness.Lib 0.10.0 — `option<T>`

Harness emitter handles WIT `option<T>` for both value-type and
reference-type inner shapes:

```wit
world picker {
    record snapshot {
        maybe-num: option<u32>,
        maybe-name: option<string>,
    }
    export pick: func(want-num: u32, want-name: u32) -> snapshot;
}
```

CLR mapping:
- **`option<u32>`** → `System.Nullable<uint>` (value-type inner).
  None reads as `null`, Some as the wrapped value via `HasValue`.
- **`option<string>`** → `string` with `null` sentinel
  (reference-type inner — no wrapper needed).

Canonical-ABI layout: 1-byte discriminator at offset 0, then the
payload aligned per the inner type's alignment. Lift walks the
discriminator, branches on `0` (none → `null` / `default`) vs
`1` (some → lifts the inner T, optionally `Nullable.ctor` wraps
for value types). `cabi_post` cleanup walks into a present
option's inner T when it contains strings or lists.

### What changed

- **`CanonicalAbi.cs`** — `Layout` handles `CtOptionType` with
  the disc-then-payload pattern.
- **`WitTypeEmit.cs`** — `MapClrType` returns
  `Nullable<innerClr>` for value-type inner, plain `innerClr`
  for reference-type inner.
- **`LiftEmit.cs`** — `EmitLiftField` adds `CtOptionType` case
  delegating to new `EmitLiftOption` helper (reads disc, two
  branches, lifts inner, conditionally wraps in `Nullable<T>`
  via `.ctor(T)`).
- **`WorldHarnessEmit.cs`** — `ContainsStringOrList` recurses
  into `CtOptionType.Inner`, so cabi_post correctly walks
  options containing strings/lists.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-option/` —
  Rust returns conditional `Some`/`None` based on input flags.
  Test asserts `pick(1,1) → (42, "hi")`, `pick(0,0) → (null,
  null)`, and mixed `pick(1,0) → (42, null)`.

**13/13 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.17.0` →
`WACS-ComponentModel-v0.18.0` (minor — capability shift on
Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.9.0 — `enum` + `flags`

Harness emitter handles WIT `enum` and `flags` declarations:

```wit
world security {
    enum severity { info, warning, critical }
    flags permissions { read, write, execute, delete }
    record status { sev: severity, perms: permissions }
    export get-status: func() -> status;
}
```

Both shapes emit as native CLR enums:
- **`Severity`**: byte underlying, no `[Flags]` — three literals
  at sequential ordinals (`Info=0`, `Warning=1`, `Critical=2`).
- **`Permissions`**: byte underlying, `[Flags]` attribute — bit
  literals (`Read=1`, `Write=2`, `Execute=4`, `Delete=8`).
  Combined values render naturally via `ToString()`:
  `Read|Write` → `"Read, Write"`.

Backing-width selection per canonical-ABI:
- Enum: 1 byte if `≤ 256` cases, 2 bytes if `≤ 65536`, else 4.
  Same width rule as variant discriminator.
- Flags: 1 byte if `≤ 8` flags, 2 bytes if `≤ 16`, 4 if `≤ 32`.

### What changed

- **`CanonicalAbi.cs`** — adds `CtEnumType` + `CtFlagsType` layout
  cases; new `FlagsByteWidth` helper.
- **`WitTypeEmit.cs`** — `EmitWorldTypes` Pass-1 now emits enum +
  flags eagerly as complete `Type` instances (no two-pass needed
  — enum values are constants, no forward refs). New
  `EmitEnumType` and `EmitFlagsType` helpers using
  `ModuleBuilder.DefineEnum` + `DefineLiteral`. Flags get
  `FlagsAttribute` applied. `MapClrType` looks up enum / flags
  types from the registry.
- **`LiftEmit.cs`** — `EmitLiftField` adds `CtEnumType` +
  `CtFlagsType` cases delegating to new `EmitReadIntegerWidth`
  helper (reads 1 / 2 / 4 bytes by backing width; the resulting
  integer is stelem / stfld-compatible with the enum-typed slot
  directly, no explicit boxing/conversion needed).
- **`TypeRegistry`** — gains `Enums` and `Flags` dictionaries.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-enum-flags/`
  — Rust returns `severity=warning` + `perms=Read|Write`. Test
  asserts both numeric values + ToString rendering.

**12/12 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.16.0` → `WACS-ComponentModel-v0.17.0`
(minor — capability shift on Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.8.0 / WACS.ComponentModel.Harness.Runtime 0.4.0 — full primitive width matrix + list&lt;string&gt; / list&lt;record&gt;

Harness lift IL now covers every WIT primitive (bool, s8, u8, s16,
u16, s32, u32, s64, u64, f32, f64, char, string). Previously only
s32/u32/string went through correctly in records; other widths
threw at emit. Plus two more list-element shapes (string, record)
got fixture coverage via the existing infrastructure.

**Three new fixtures** validate the broader coverage:

| Fixture | Shape | Notes |
|---|---|---|
| `list-string` | `list<string>` return | string elements via existing EmitLiftElementAt |
| `list-record-of` | `list<record>` return | record elements via existing Lift{Name} delegation |
| `primitives` | record of all 10 primitives | exercises new width support — bool / s8 / u8 / s16 / u16 / s64 / u64 / f32 / f64 / char |

The primitives fixture's Rust returns:
```rust
Sample {
    flag: true, small_s: -7, small_u: 200,
    med_s: -1000, med_u: 50000,
    big_s: -9_000_000_000, big_u: 18_000_000_000_000_000_000,
    single: 3.14, double: 2.718281828, letter: 'Z',
}
```
All 10 fields round-trip exactly through the emitted Lift method.

### What changed

- **`WACS.ComponentModel.Harness.Runtime` 0.3.0 → 0.4.0** (minor —
  new public API surface): `MemoryHelpers` gains `ReadI16LE` /
  `WriteI16LE`, `ReadI64LE` / `WriteI64LE`, `ReadF32LE` /
  `WriteF32LE`, `ReadF64LE` / `WriteF64LE`. The F32/F64 helpers
  use `BitConverter.{SingleToInt32Bits, DoubleToInt64Bits}` to
  reuse the integer LE path.
- **`WACS.ComponentModel.Harness.Lib` 0.7.0 → 0.8.0** (minor —
  full primitive-width lift): `LiftEmit.EmitLiftPrimitive` +
  `EmitLiftElementAt` extended for bool / s8 / u8 / s16 / u16 /
  s64 / u64 / f32 / f64 / char. Signed narrow widths get
  `Conv_I1` after `ReadU8`; unsigned ushort/char get `Conv_U2`
  after `ReadI16LE`. F32/F64 go through dedicated helpers.

**11/11 fixtures pass.**

Family tag: `WACS-ComponentModel-v0.15.0` → `WACS-ComponentModel-v0.16.0`
(minor — capability shift on both Harness.Runtime + Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.7.0 — small wins batch (bool params, direct string return, string-in-variant)

Three smaller follow-ups bundled in one slice. The harness
emitter's "real-world WIT" coverage is meaningfully wider:

### 1. bool params + missing small-integer primitives

`MapPrimitiveToClrType` (the LOWERED-shape mapper used by
`AppendLoweredType`) was missing many primitive kinds — only
S32/U32/S64/U64/F32/F64. Now covers:
- `bool` → `int` (lowers as i32 0/1; CLR bool on the stack is
  already i4-sized so no explicit conv emit needed at the
  param-pushing site)
- `s8` / `u8` / `s16` / `u16` / `char` → `int` (all lower to i32)
- The wider numeric types (s64/u64/f32/f64) unchanged.

User-facing type (`MapClrType` in `WitTypeEmit`) already returned
the natural CLR primitive for each — `typeof(bool)`, `typeof(byte)`,
etc. The fix lets the lowered-type bookkeeping not throw.

### 2. Direct string return (`func() -> string`)

Previously fell into `EmitFlatLowered`'s `CtPrimType` branch
which returned the int retArea pointer as if it were the result.
Now special-cases `CtPrim.String`:

```csharp
if (retPrim.Kind == CtPrim.String) {
    EmitLiftStringReturn(il, fe, memoryField);
    return;
}
```

New `EmitLiftStringReturn` mirrors the string-out tail of the
existing `EmitStringInStringOut` special path: stash retArea,
read (ptr, len), `StringCoding.LiftUtf8`, call cabi_post, return.

### 3. Strings in variant payloads

No code changes — fixture-only coverage. The existing
infrastructure (variant lift + `EmitLiftField` string case)
already composed correctly.

### What changed

- **`WACS.ComponentModel.Harness.Lib` 0.6.0 → 0.7.0** (minor —
  new primitive-type coverage + direct string return):
  `MapPrimitiveToClrType` extended; `EmitFlatLowered` adds the
  string-return branch; new `EmitLiftStringReturn` helper.
- **Fixtures**:
  - `wit-harness-spike-string-variant/` — string in variant
    payload (`variant message { hello(string), silence }`).
  - `wit-harness-spike-bool-string-return/` — bool param + direct
    string return (`greet(use-comma: bool) -> string`).

**All 8 fixtures pass**: hello, richer, string-record, list-record,
list-return, list-variant, string-variant, bool-string-return.

Family tag: `WACS-ComponentModel-v0.14.0` → `WACS-ComponentModel-v0.15.0`
(minor — capability shift on Harness.Lib).

## Fixture coverage: list&lt;T&gt; in variant payload

New regression fixture
(`Spec.Test/components/fixtures/wit-harness-spike-list-variant/`)
validates that lists nested inside variant payloads work
end-to-end via the existing infrastructure (variant lift +
field-level list lift). No Harness.Lib code changes required:
the prior slices (records + variants + lists in record fields)
composed correctly to cover this shape.

```wit
world streams {
    variant payload {
        numbers(list<u32>),
        empty,
    }
    export get-payload: func(want-numbers: u32) -> payload;
}
```

Rust impl returns `Numbers(vec![7,14,21,28])` or `Empty` based on
the flag. The emitted harness round-trips both cases — variant
discriminator dispatch + `EmitLiftField` recursing into
`EmitLiftList` for the payload + `cabi_post_get-payload` to free
the element-array body.

## WACS.ComponentModel.Harness.Lib 0.6.0 — direct `list<T>` return value

Harness emitter handles `list<T>` as a direct export return (not
nested in a record). Closes a real-world WIT shape:
`export get-numbers: func() -> list<u32>`,
`export get-titles: func() -> list<string>`, etc.

**End-to-end verified on a new fixture**
(`Spec.Test/components/fixtures/wit-harness-spike-list-return/`):

```wit
world numbers {
    export get-numbers: func() -> list<u32>;
}
```

Rust returns `vec![100, 200, 300, 400]`. Emitted
`NumbersHarness.GetNumbers()` returns `uint[] { 100, 200, 300, 400 }`
and calls `cabi_post_get-numbers` to free the element-array body.

### Refactor: `EmitLiftListFromBase` parameterized over memory local

The field-level list lift (called from a static `Lift{Name}`
method where `arg.0 = MemoryInstance`) and the wrapper-instance
list lift (called from the typed wrapper method where
`arg.0 = this`) need the SAME element-walking IL but DIFFERENT
sources for the `MemoryInstance` reference. `EmitLiftListFromBase`
+ `EmitLiftElementAt` now take a `memoryLocal` parameter; each
call site sets it up from its own source:
- Static Lift methods: `memoryLocal = arg.0`
- Instance wrappers: `memoryLocal = this._memory`

Caught a subtle bug in the v0.5.0 emission where I'd assumed
`Ldarg_0 = memory` universally; the instance wrapper for direct
list return tried to call `MemoryHelpers.ReadI32LE(harness, ptr)`
and read random memory addresses. The new param explicitly
threads memory through every path.

### What changed

- **`WACS.ComponentModel.Harness.Lib` 0.5.0 → 0.6.0** (minor —
  direct list return + memory-local refactor):
  - `WorldHarnessEmit.BuildFunctionExport` recognizes `CtListType`
    return as indirect (retArea ptr, `NeedsPostReturn = true`).
  - `WorldHarnessEmit.EmitFlatLowered` adds `CtListType` return
    branch → calls new `EmitLiftListReturn` helper.
  - `EmitLiftListReturn` stashes retArea, loads memory from the
    instance field, calls `LiftEmit.EmitLiftListFromBase` then
    `cabi_post_<name>`.
  - `LiftEmit.EmitLiftListFromBase` made public, takes a
    `memoryLocal` parameter.
  - `LiftEmit.EmitLiftElementAt` takes a `memoryLocal` parameter
    (previously assumed `arg.0`).
  - `LiftEmit.EmitLiftList` (field-level entry point) sets up a
    `memoryLocal` from `arg.0` to satisfy the new contract.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-list-return/`
  — Rust + WIT + built `.component.wasm` + Generated.Validate
  asserting `get-numbers() == [100, 200, 300, 400]`.
- **Regression-checked**: hello, richer, string-record, list-record
  all 4 existing fixtures still pass.

Family tag: `WACS-ComponentModel-v0.13.0` → `WACS-ComponentModel-v0.14.0`
(minor — capability shift on Harness.Lib).

## WACS.ComponentModel.Harness.Lib 0.5.0 — `list<T>` lift in record fields

Harness emitter extends to handle `list<T>` fields inside records
on the LIFT path. Unblocks WIT shapes like
`record bag { values: list<u32>, count: u32 }`,
`record event-log { entries: list<string> }`,
`record query-result { rows: list<row> }`.

**End-to-end verified on a new fixture**
(`Spec.Test/components/fixtures/wit-harness-spike-list-record/`):

```wit
world numbers {
    record bag {
        values: list<u32>,
        count: u32,
    }
    export get-bag: func() -> bag;
}
```

Rust impl returns `Bag { values: vec![10, 20, 30, 40, 50], count: 5 }`.
Emitted `NumbersHarness.GetBag()` returns
`Bag(Values=[10,20,30,40,50], Count=5)` and calls `cabi_post_get-bag`
to free the retArea + element-array body.

### What changed

- **`CanonicalAbi`** — adds `CtListType` layout (8 bytes, 4-align,
  matching the (ptr, count) pair shape).
- **`WitTypeEmit.MapClrType`** — maps `list<T>` to `T[]` (chose
  arrays over `IReadOnlyList<T>` so the lift loop can use
  `newarr` + `stelem` without an extra wrapper).
- **`LiftEmit.EmitLiftField`** — adds `CtListType` case
  delegating to new `EmitLiftList`.
- **`LiftEmit.EmitLiftList`** — reads ptr + count from the field
  offset, allocates a `T[]` of length count, loops `0..count`
  lifting each element from `(listPtr + i * elemSize)`.
- **`LiftEmit.EmitLiftElementAt`** — element-level lift
  parameterized over a runtime (listPtr, indexLocal) pair rather
  than the static (arg.1, offset) pair the field-level lift uses.
  Covers primitive / string / record / variant element types.
- **`LiftEmit.EmitStelem`** — picks the right `Stelem_*` opcode
  for primitive widths + falls back to `Stelem` for reference /
  struct elements.

### What still throws (next slices of #65)

- Lists as direct return / param values (not nested inside a
  record). Same lift logic applies — needs `CtListType` handling
  in `BuildFunctionExport`'s return-type branch + an outer
  list-lift helper.
- Lists in variant payloads. Same `EmitLiftField` extension,
  just needs the variant payload case to call into it.
- Strings + lists in record/variant PARAMS (lower path). The
  indirect-ptr emission is a bigger structural change — defer.
- Nested lists (`list<list<T>>`). The recursive shape probably
  works but isn't exercised by current fixtures.

Family tag: `WACS-ComponentModel-v0.12.0` → `WACS-ComponentModel-v0.13.0`
(minor — capability shift on Harness.Lib).

### What changed

- **`WACS.ComponentModel.Harness.Lib` 0.4.0 → 0.5.0** (minor —
  list lift in record fields): `CanonicalAbi.cs` adds list layout;
  `WitTypeEmit.cs` adds list CLR mapping;
  `LiftEmit.cs` adds `EmitLiftList` + `EmitLiftElementAt` +
  `EmitStelem` helpers.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-list-record/`
  — Rust + WIT + built `.component.wasm` + Generated.Validate
  asserting `get-bag() == Bag([10,20,30,40,50], 5)`.

## WACS.ComponentModel.Harness.Lib 0.4.0 — strings in record fields (lift)

Harness emitter extends to handle string fields inside records on
the LIFT path (records flowing wasm → host). The most common
real-world WIT shape this unblocks: things like
`record task { id: u32, title: string }`,
`record greeting { message: string, count: u32 }`.

**End-to-end verified on a new fixture**
(`Spec.Test/components/fixtures/wit-harness-spike-string-record/`):

```wit
world greeter {
    record greeting {
        message: string,
        count: u32,
    }
    export greet: func() -> greeting;
}
```

Rust impl returns `Greeting { message: "Hello, World!", count: 42 }`.
The emitted `GreeterHarness.Greet()` returns the typed
`Greeting(Message="Hello, World!", Count=42)` and calls
`cabi_post_greet` to free the retArea + the string body.

### What changed

- **`LiftEmit.EmitLiftPrimitive`** — adds the `CtPrim.String`
  case: reads ptr + len (two i32s at the field's offset / offset+4)
  + calls `StringCoding.LiftUtf8(memory, ptr, len)`. The string
  body's lifetime stays managed by the owning record/variant's
  `cabi_post_<name>` call at the export-method level.
- **`WorldHarnessEmit.EmitFlatLowered`** — gate loosened: removes
  the `!fe.NeedsPostReturn` exclusion so records / variants
  containing strings can take the flat-lowered path. New
  `EmitLiftReturnViaRetArea` helper centralizes the
  "stash retArea → lift → optional cabi_post → return lifted"
  shape that both `CtRecordType` and `CtVariantType` returns now
  share.

### Existing fixtures (regression-checked, both green)

- `wit-harness-spike-hello` — `greet(string) -> string` still
  works via the dedicated string-in-string-out path.
- `wit-harness-spike-richer` — records + variants without strings
  still work (NeedsPostReturn=false → cabi_post call elided).

### What still throws

- Strings in RECORD / VARIANT PARAMS (lower path) — the indirect
  param lowering for pointer-content records needs a different
  emission shape (allocate via cabi_realloc, write fields,
  including realloc-allocating any nested string bodies, pass
  the resulting ptr). Tracked for follow-up.
- `list<T>` anywhere — layout primitive (8-byte ptr+len) is
  already in CanonicalAbi, but lift IL needs to walk the element
  array + per-element lift. Next slice of task #65.
- `option<T>`, `result<T, E>`, `tuple<...>` as record/variant
  payloads.

Family tag: `WACS-ComponentModel-v0.11.1` → `WACS-ComponentModel-v0.12.0`
(minor — capability shift on Harness.Lib).

### What changed

- **`WACS.ComponentModel.Harness.Lib` 0.3.0 → 0.4.0** (minor —
  records-with-string lift): `LiftEmit.EmitLiftPrimitive` adds
  `CtPrim.String` case; `WorldHarnessEmit.EmitFlatLowered` loses
  the `!NeedsPostReturn` gate + factors the indirect-return tail
  into `EmitLiftReturnViaRetArea`.
- **Fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-string-record/`
  — Rust + WIT + 40 KB `.component.wasm` + Generated.Validate
  console asserting `greet() == Greeting('Hello, World!', 42)`.

## WACS.Transpiler.Lib 0.11.0 / WACS.ComponentModel 0.7.1 — harness implementation emit infrastructure

Plumbing for the symmetric-engines payoff: when the transpiler is
given a harness assembly via the new
`TranspilerOptions.HarnessAssemblyPath`, it loads the harness,
discovers its `I{World}` interface + named-type classes
(`Vec2`, `Outcome`, etc.), and is now wired to emit a
`{World}HarnessImpl` wrapper class that implements `I{World}` by
forwarding to `ComponentExports`'s static methods.

The CLI's existing `--harness <dll>` flag on `wacs build` /
`wacs aot` already populates the new option transparently — no
new flags.

**Architectural shape**:
- **`HarnessAssemblyBinder`** loads the harness `.dll` via
  `AssemblyLoadContext.Default.LoadFromAssemblyPath`, parses the
  embedded `_WitContract` to recover the harness's authored world
  name (the transpiler must use the HARNESS's world name to find
  the `I{World}` interface, not the loaded component's possibly-
  synthesized name).
- **`ComponentExportsEmit.EmitComponentExportsClass`** gains a
  `preRegisteredTypes` parameter that seeds the emitted-types
  cache. When set, signatures use the harness's `Vec2` / `Outcome`
  rather than emitting transpiler-owned duplicates — no
  translation layer needed at the interface boundary.
- **`HarnessImplEmit`** emits the `{World}HarnessImpl` class:
  sealed, public, parameterless ctor, instance methods (Virtual+
  Final+NewSlot) forwarding to the matching `ComponentExports`
  static method. Methods without a match emit a
  `NotImplementedException`-throwing body so the interface
  contract stays structurally complete.

**Known gap (the reason this is "infrastructure" rather than a
working end-to-end):**

`ComponentExportsEmit.IsEmittable` rejects today's spike fixtures:

- **richer** (`add(vec2, vec2) -> vec2`): record params aren't
  in the v0 emittable set (only primitives, list-of-prim, and
  resource-handle params pass).
- **hello** (`greet(string) -> string`): the string-param shape
  isn't in v0 either (the predicate accepts string RETURNS but
  rejects string PARAMS).

Both gating predicates are pre-existing transpiler limitations
that predate the harness work. With them in place,
`ComponentExports` doesn't emit a method matching `IHello.Greet`
or `IRicher.Add`, so `HarnessImpl` has nothing to forward to.
The plumbing is verified to load the harness correctly and gate
on `componentExportsType != null` cleanly — no errors, no spam.

**Next slice**: extend `ComponentExportsEmit.IsEmittable` (+ the
per-export emit body) to handle record + string params. That's a
focused 200-400 LOC extension of an existing module that
automatically unlocks `HarnessImpl` for these fixtures. Tracked
separately from task 64 since it's a transpiler-internals slice
rather than a harness-side one.

### What changed

- **`WACS.Transpiler.Lib` 0.10.4 → 0.11.0** (minor — new public
  API): `TranspilerOptions.HarnessAssemblyPath`,
  `ComponentExportsEmit.EmitComponentExportsClass` gains an
  optional `preRegisteredTypes` parameter, internal
  `HarnessAssemblyBinder` + `HarnessImplEmit`. `BuildHandler`
  threads `--harness` through.
- **`WACS.ComponentModel` 0.7.0 → 0.7.1** (point — metadata):
  adds `InternalsVisibleTo Wacs.Transpiler.Lib` so the
  Transpiler.Lib helpers can reuse `NameMangler` (kebab →
  PascalCase) — same naming must agree with what
  `Harness.Lib` emitted into the harness assembly.

Family tags: `WACS-Transpiler-v0.10.2` → `WACS-Transpiler-v0.11.0`
(minor — capability shift); `WACS-ComponentModel-v0.11.0` →
`WACS-ComponentModel-v0.11.1` (point).

## WACS.ComponentModel 0.7.0 / WACS.Transpiler.Lib 0.10.4 — primary-section decode for canon-lifted components (richer fixture)

v1 follow-up to the primary-section decode work. Extends
`BinaryWitDecoder.DecodeFromComponentBinary` to track the
component function index space across additional sections so
exports routed through canon.lift + sort=Core aliases (the
typical `wasm-tools component new` shape, including the
richer-spike fixture) resolve their type idx and surface in the
diff.

**End-to-end verified on cargo-built richer fixture (NO
`wasm-tools component embed` step):**
```
$ wacs build --wasip2 --harness richer.harness.dll \
    -o out.dll richer.component.wasm
wrote out.dll (21 functions, 220ms)
$ wacs build --wasip2 --harness hello.harness.dll \
    -o sf.dll richer.component.wasm
error: ...
  export 'greet': declared in harness, missing from component.
  export 'add': present in component, not declared in harness.
  export 'normalize-or-fail': present in component, not declared in harness.
```

### New decoder sections

- **Canon section** — `DecodeCanonSection` reads canon.lift entries
  (recording the source type idx in the func index space) +
  canon.lower (consumes wire format, no component-func-space
  contribution — produces core funcs) + resource.new / drop / rep
  intrinsics (consume wire format, no contribution). canonopts
  parsing covers the common opt tags (string-encoding,
  memory/realloc/post-return refs, async, callback,
  always-task-return).
- **Alias section** — `DecodeAliasSection` consumes wire format
  with correct sort-first / target-second ordering; sort=Func
  aliases (interface-export, core-instance-export, outer)
  contribute uint.MaxValue slots so the func index space stays
  aligned, but type isn't recoverable for them without chasing
  the alias chain. Sort=Core / Type / Instance / Component
  aliases skip without contribution.
- **Export-as-rebinding** — primary-component exports of
  sort=Func re-bind the source func at a new component-func
  index (per wasm-tools' `$"#funcN name"` annotations). The
  decoder translates the source idx through the current
  funcTable to capture the type, then appends a new slot —
  matching the binary's index space exactly.

### World-level type-ref binding in WitContractCompare

`WitResolver` only binds CtTypeRefs declared inside interfaces.
The primary-section decoder places named types in `world.Types`,
which WitResolver ignores. Added `BindWorldLevelTypeRefs` to
`WitContractCompare`: walks each world's named types + binds
matching CtTypeRefs in the world's export / import signatures
(recursively through records, variants, lists, options, results,
tuples). Without this, comparisons against primary-decoded
packages would always diff structural-record-vs-CtTypeRef even
when the underlying types matched.

### Still gap: hello-style components with many sort=Func aliases

The hello fixture (Rust + cargo-built wasm32-wasip2 binding to
~13 WASI interfaces) still falls through to the "no custom
section" error — the canon.lift for "greet" lands at a func idx
the export references, but the cascading sort=Func aliases from
the WASI instance imports either alter the index space ordering
in ways the decoder doesn't track or trigger a canonopts shape
v0 doesn't recognize. Investigation deferred; `wasm-tools
component embed` remains the workaround for those.

### What changed

- **`WACS.ComponentModel` 0.6.0 → 0.7.0** (minor — canon /
  alias / export-rebinding logic added to
  `BinaryWitDecoder.DecodeFromComponentBinary`): new private
  helpers `DecodeCanonSection` (with `SkipCanonOpts`),
  `DecodeAliasSection`, `ContributeImportsToFuncTable`,
  `DecodePrimaryExportSection`.
- **`WACS.Transpiler.Lib` 0.10.3 → 0.10.4** (point — validator
  resolves world-level type refs): `WitContractCompare.Diff`
  calls `BindWorldLevelTypeRefs` on both expected + actual
  packages before structural comparison.

Family tags: `WACS-ComponentModel-v0.10.0` → `WACS-ComponentModel-v0.11.0`
(minor — capability shift); `WACS-Transpiler-v0.10.1` →
`WACS-Transpiler-v0.10.2` (point).

## WACS.ComponentModel 0.6.0 / WACS.Transpiler.Lib 0.10.3 — primary-section WIT decode (partial)

New entry point `BinaryWitDecoder.DecodeFromComponentBinary(byte[])`
derives a `CtPackage` from the primary type / import / export
sections of a component binary — fallback for components without
a `component-type:*` custom section (the cargo-built
`wasm32-wasip2` case the previous slice surfaced as a documented
limitation).

`ComponentTranspiler.Parse` now calls this fallback when the
custom-section decode is absent or yields an empty world. The
validator's world-name check additionally treats `"root"`
(the synthesized default the primary decoder uses when no
qualified name is available in the binary) as a wildcard,
delegating the actual validity signal to the export comparison.

**Verified end-to-end on cargo-built hello fixture (no `wasm-tools
component embed` step):**
```
$ wacs build --wasip2 --harness hello.harness.dll \
    -o out.dll hello.component.wasm
wrote out.dll (138 functions, 296ms)
$ wacs build --wasip2 --harness richer.harness.dll \
    -o sf.dll hello.component.wasm
error: ...
  export 'add': declared in harness, missing from component.
  export 'normalize-or-fail': declared in harness, missing from component.
  export 'greet': present in component, not declared in harness.
```

### What still doesn't decode (and why)

Components routing exports through canonical-function aliases
(the typical `wasm-tools component new` output and richer-spike's
shape) trip `BuildWorld`'s assumption that export indices point
directly at type-indexed function types. In primary-section form
those indices reference the function index space — populated by
canon.lift / canon.lower / alias-from-core-instance — which the
v0 decoder doesn't track. Such components surface as an empty
world; the fallback in `Parse` then yields the existing typed
"no custom section" error from validation.

Lifting this needs ~300-500 LOC: track the function index space
across Canon, Alias, and Import sections; map each canon.lift's
function index to its type index; resolve export-of-sort-Func
through that map. Tracked as v1 work.

Other shapes that don't decode in v0:
- Multiple Type/Import section interleaving where the index
  space ordering matters (decoder does preserve file order; this
  works) but Alias sections also contribute to the index space
  (decoder doesn't track those yet).
- Nested-component types in the primary type section.
- Inline interface re-exports.

### What changed

- **`WACS.ComponentModel` 0.5.2 → 0.6.0** (minor — new public API):
  `BinaryWitDecoder.DecodeFromComponentBinary(byte[])`, plus
  internal helpers `DecodeTypeSectionAsInnerDecls`,
  `DecodePrimaryImportExportSection`, `BuildPackageFromPrimary`.
  Reuses the existing `ReadImportOrExport` for import/export
  parsing — single source for the wire format.
- **`WACS.Transpiler.Lib` 0.10.2 → 0.10.3** (point — fallback
  wiring + validator world-name leniency): `ComponentTranspiler.Parse`
  buffers the stream so the fallback can re-decode, and falls
  through to `DecodeFromComponentBinary` when the custom-section
  decode is absent or empty. `WitContractCompare.Diff` skips the
  world-name comparison when the actual world is named `"root"`
  (the synthesized default).

Family tags: `WACS-ComponentModel-v0.9.0` → `WACS-ComponentModel-v0.10.0`
(minor — capability shift); `WACS-Transpiler-v0.10.0` →
`WACS-Transpiler-v0.10.1` (point).

## WACS.Transpiler.Lib 0.10.2 — imports validation in WitContractCompare

Extends the contract diff to cover imports. v0 rule (per
`docs/wit-harness-plan.md` §"Validation contract"):

- Every WIT-declared inline-function import (`import name: func(...)`)
  must be present in the component with a matching signature.
  Missing or mismatched imports surface as typed messages.
- The reverse direction — component imports not declared in the
  harness — is intentionally NOT a hard mismatch. wit-bindgen
  auto-bundles WASI imports the harness can't realistically pre-
  declare; embedders supply those via `LoadFrom`'s `bindImports`
  callback.
- Interface-reference imports (`import wasi:io/poll@0.2.0`) on the
  harness side surface as a "not validated in v0" diagnostic so
  the user knows the check isn't covering them silently. Lifting
  this needs WIT-side interface resolution + per-method signature
  walking; tracked as the next-mile follow-up.

`CompareFunctionSignatures` generalizes its diagnostic prefix
from hardcoded "export" to a `kind` parameter ("export" or
"import") so messages read correctly for both directions.

Spike fixtures unaffected — both `hello.wit` and `richer.wit`
declare zero imports, so the new code path has nothing to flag
on existing diff invocations. Transpiler.Test 57/57 still green.

### Two follow-ups investigated + documented this round (deferred)

- **`BinaryWitDecoder` primary-section decode**: the existing
  `BuildPackage` is hardcoded for wit-component's
  nested-wrapper encoding (a wrapper `ComponentType` whose
  inner `ComponentType` describes the world). Cargo-built
  `wasm32-wasip2` components don't ship that wrapper — their
  world is in the primary type/export sections directly.
  Passing the full binary to the existing decoder returns
  null (no wrapper to find). A proper fix is ~200-400 LOC of
  new decoder logic walking the primary component sections.
  Today's workaround: `wasm-tools component embed` adds the
  custom section, then validation works.
- **Transpiler emits `implements I{World}`**: the
  symmetric-engines payoff at the CLR-interface level needs
  either cross-assembly type sharing (transpiler references
  the harness's emitted `Vec2` / `Outcome` types instead of
  emitting its own) or a translation layer at the interface
  boundary. Either path is ~500-1000 LOC and tightly coupled
  to `ComponentExportsEmit`'s 3281-LOC class-emission
  pipeline. Embedders today still get typed call sites via
  the interpreter harness + compile-time WIT-shape validation
  via `--harness` on transpile; CLR-interface-level identity
  across engines is the natural v2 close.

### What changed

- **`WACS.Transpiler.Lib` 0.10.1 → 0.10.2** (point — additional
  validation cases): `WitContractCompare.Diff` adds imports
  comparison; `CompareFunctionSignatures` gains a `kind`
  parameter for export/import diagnostic prefixing.

## WACS.Cli 1.10.0 / WACS.Transpiler.Lib 0.10.1 — `--harness` / `--wit-dir` on aot + build

Both transpile-side CLI verbs (`wacs aot`, `wacs build`) gain
two new flags that resolve the harness contract WIT text and
forward it through `TranspilerOptions.HarnessContractText`:

- **`--harness <path.dll>`** — loads a built harness assembly
  (produced by `wacs harness`), reads its embedded
  `_WitContract` static field via reflection, threads the text
  into the transpile pipeline.
- **`--wit-dir <dir>`** — concatenates every `.wit` under the
  directory (matching the embedding shape `HarnessEmitter` uses).
  In-process equivalent of `--harness`: no `.dll` build step
  required, useful during iteration.

The two flags are mutually exclusive.

**End-to-end verified**:
```
$ wacs harness hello-spike/wit -o hello.harness.dll
$ wasm-tools component embed wit/ hello.component.wasm \
    --world hello -o hello-with-witsection.wasm
$ wacs build --wasip2 --harness hello.harness.dll \
    -o out.dll hello-with-witsection.wasm
wrote out.dll (138 functions, 263ms)
$ wacs build --wasip2 --harness richer.harness.dll \
    -o should-fail.dll hello-with-witsection.wasm
error: component transpilation failed: Component does not match harness WIT contract:
  world name: harness expects 'richer', component declares 'hello'.
  export 'add': declared in harness, missing from component.
  export 'normalize-or-fail': declared in harness, missing from component.
  export 'greet': present in component, not declared in harness.
```

**Caveat surfaced during E2E test, fixed in this slice (Transpiler.Lib
0.10.1 point bump)**: the v0 validator requires the component
binary to carry a `component-type:*` custom section (the
wit-component convention). Rust components built straight to
`wasm32-wasip2` via cargo don't emit one. Previously the
validator silently skipped when the section was missing, giving
a false-positive pass; now it throws with an actionable message
pointing at `wasm-tools component embed`. Deriving the world
from the component's primary type / export sections (instead of
a custom section) is a follow-up — needs `BinaryWitDecoder` to
grow a new entry point.

**Deferred from the outer ring**: the transpiler doesn't yet
emit `implements I{World}` on the transpiled output. That's the
"symmetric engines at the CLR-interface level" piece — requires
weaving the harness's `I{World}` reference into
`ComponentExportsEmit`'s class-definition pipeline (~600 LOC of
coordinated transpiler-internals work). Embedders today get
typed call sites on the interpreter side via the harness +
contract validation on the transpiler side; CLR-interface-level
sharing across engines is the natural v2 close.

### What changed

- **`WACS.Cli` 1.9.0 → 1.10.0** (minor — two new flags on aot +
  build verbs): `AotOptions.Harness` + `AotOptions.WitDir` (and
  the same on `BuildOptions`). `BuildHandler.BuildTranspilerOptions`
  gains `ResolveHarnessContractText(BuildOptions)` — loads
  harness `.dll` via `AssemblyLoadContext.LoadFromAssemblyPath` +
  reflects `_WitContract`, or walks the directory concatenating
  every `*.wit`.
- **`WACS.Transpiler.Lib` 0.10.0 → 0.10.1** (point — validation
  correctness): swap silent-skip for typed throw when
  `HarnessContractText` is set but the component binary carries
  no `component-type:*` custom section.

## WACS.Transpiler.Lib 0.10.0 — compile-time harness contract validation

`TranspilerOptions` gains a `HarnessContractText` property — when
set, the transpiler diffs the supplied WIT text against the loaded
component's WIT custom section before any IL is emitted, and
throws `InvalidOperationException` with a typed report listing
every mismatch.

The expected source for that text is the `_WitContract` static
field every `WACS.ComponentModel.Harness.Lib`-emitted harness
carries (per the previous slice). Threading it both ways binds
the harness and transpiled output to one canonical contract — a
binary shape drift gets caught at build time, not at the typed
call site.

New module: `Wacs.Transpiler.AOT.Component.WitContractCompare`.
v0 scope:
- World name match (kebab-case).
- Export set match (added / dropped exports flagged).
- Per-export signature: param arity, per-param type, return type,
  via deep structural equality on the resolved `CtValType` tree
  (records, variants, options, results, tuples, lists, primitives).
  Records / variants compare on field / case names + types.
- Imports not compared (the harness asserts what embedders invoke,
  not what the component imports — that's a follow-up).
- Diagnostic messages render short WIT-ish forms per mismatch.

The transpiler emits-an-`implements I{World}` step on the
transpiled assembly + CLI wiring follow in the next two
checkpoints.

Family tag: `WACS-Transpiler-v0.9.0` → `WACS-Transpiler-v0.10.0`
(minor — new public API on TranspilerOptions, new public type).

### What changed

- **`WACS.Transpiler.Lib` 0.9.1 → 0.10.0** (minor — new public
  API): adds `TranspilerOptions.HarnessContractText`,
  `WitContractCompare.Diff(string, CtPackage)`,
  `WitContractCompare.TypesEqual` recursion. Wired in
  `ComponentTranspiler.TranspileSingleModule` between `Parse` and
  the composer / single-module branch.
- **Spec.Test**: csproj excludes
  `components/fixtures/wit-harness-spike-*/**/*.cs` from the
  default compile + None globs so the wit-harness fixtures'
  subprojects (Aot.Spike, Generated.Validate) don't pollute
  Spec.Test's assembly.

## WACS.ComponentModel.Harness.Lib 0.3.0 — `_WitContract` + symmetric `I{World}` interface

Harness emitter now produces two pieces of contract scaffolding
on every harness assembly:

1. **`I{World}` interface** — emitted alongside the harness class,
   carries one abstract method per WIT export with the same name
   + signature as the typed wrapper. The harness class implements
   it implicitly (Virtual+Final+NewSlot on the wrapper methods).
   The symmetric counterpart for the transpiler-emitted class is
   the next slice: both engines implement the same `I{World}`,
   so embedder call sites are engine-agnostic per the
   `feedback_symmetric_engines` invariant.

2. **`public static readonly string _WitContract`** — embeds the
   raw WIT source the emit consumed (concatenated from every
   `.wit` under the input directory, recursively). The transpiler's
   future `AddHarnessContract` reads this string at compile time
   to diff against the loaded component's WIT custom section;
   a runtime `LoadFrom`-time validator could do the same via
   reflection.

Surface change on `HarnessEmitter.EmitToStream(packages, ...)`:
new optional `contractText` parameter. Default empty when callers
have no source text to embed. The directory-input overload
threads the WIT source through automatically.

Validation extended on
`Spec.Test/components/fixtures/wit-harness-spike-richer/Generated.Validate/`:
five tests now green — add, two normalize-or-fail cases, IRicher
interface cast + invocation, and `_WitContract` presence /
length / contents (297 chars, contains `"world richer"`). The
hello fixture's validator also still passes — the existing typed
methods on its harness satisfy the synthesized `IHello` interface
without changes.

Family tag: `WACS-ComponentModel-v0.8.0` → `WACS-ComponentModel-v0.9.0`
(minor — capability shift on Harness.Lib).

### What changed

- **`WACS.ComponentModel.Harness.Lib` 0.2.0 → 0.3.0** (minor —
  new emitted shape, new optional API parameter): adds
  `EmitWorldInterface` + `EmitWitContractField` in
  `WorldHarnessEmit.cs`; `HarnessEmitter.EmitToStream(packages, ...)`
  gains an optional `contractText` parameter. Typed-wrapper
  methods now carry `Virtual | Final | NewSlot` so they implement
  the `I{World}` interface implicitly.
- **Spike validators**: richer's `Generated.Validate` adds two
  new assertions (interface cast + invocation, `_WitContract`
  presence + content).

## WACS.Cli 1.9.0 — `wacs harness` verb

New CLI verb:

```
wacs harness <wit-dir> -o <out.dll>
       [--namespace <ns>]
       [--assembly-name <name>]
```

Thin wrapper over `HarnessEmitter.EmitToFile` — takes a directory
of `.wit` files (recurses into `deps/` per the existing
`WitLoader.LoadDirectoryTree` convention) and emits a typed
harness `.dll` consumers can reference directly. Defaults:
namespace `Wacs.ComponentModel.Harness.Generated`, assembly name
derived from the world's PascalCase + `"Harness"`.

The in-memory side of the same emit core (the
`HarnessEmitter.EmitInMemory` shape) lights up future
`wacs run --wit-dir` / `wacs transpile --wit-dir` flows — deferred
to a separate slice, since they need design beyond the verb shape
(what does "run" mean for a pure-export component? validate then
invoke a named export? both?). The persisted verb is the
high-value piece for the v0 distribution flow regardless.

Drive-tested against both spike fixtures:
- `wacs harness hello-spike/wit -o /tmp/hello.harness.dll` →
  3.5 KB `.dll` carrying `HelloHarness` with `LoadFrom` + `Greet`.
- `wacs harness richer-spike/wit -o /tmp/richer.harness.dll` →
  4 KB `.dll` carrying `RicherHarness` + `Vec2` + `Outcome` +
  `Outcome.Success` / `Outcome.Invalid` + `LoadFrom` + `Add` +
  `NormalizeOrFail`.

The persisted .dll uses the same `PersistedAssemblyBuilder` pass
as the in-memory path the `Generated.Validate` consoles exercise;
both surfaces share one emit core, so functional equivalence
follows from the in-memory tests.

### What changed

- **`WACS.Cli` 1.8.1 → 1.9.0** (minor — new verb): adds
  `Verbs/HarnessOptions.cs` + `Verbs/HarnessHandler.cs`, wires
  into `Program.cs`'s ParseArguments+MapResult. Project reference
  added to `Wacs.ComponentModel.Harness.Lib`.

## WACS.ComponentModel.Harness.Lib 0.2.0 / WACS.ComponentModel.Harness.Runtime 0.3.0 — records + variants

Harness emitter expands to handle WIT records + variants
end-to-end, validated against a new richer fixture:
`Spec.Test/components/fixtures/wit-harness-spike-richer/` —
multi-export world with `record vec2 { x: s32, y: s32 }`,
`variant outcome { success(vec2), invalid }`, and two exports:

```wit
export add: func(a: vec2, b: vec2) -> vec2;
export normalize-or-fail: func(v: vec2) -> outcome;
```

The generated `RicherHarness` runs the same three assertions the
hand-shaped reference would: `add(Vec2(1,2), Vec2(3,4)) →
Vec2(4,6)`, `normalize-or-fail(Vec2(0,0)) → Outcome.Invalid`,
`normalize-or-fail(Vec2(7,-3)) → Outcome.Success(Vec2(1,-1))`.

### What the emitter learned

- **`CanonicalAbi.cs`** — layout rules: record + variant size /
  alignment, record field offsets, variant disc width
  (1 / 2 / 4 bytes by case count), variant payload offset
  (padded to max-case-align). Single-source for emitters so layout
  math stays consistent across lift / lower paths.
- **`WitTypeEmit.cs`** — record types as sealed CLR classes with
  positional ctor + readonly properties; variants as abstract
  base + nested sealed subclasses per case (`Outcome.Success`,
  `Outcome.Invalid`). `TypeRegistry` stashes ctor / getter
  references so cross-type IL works without TypeBuilder-after-bake
  reflection.
- **`LiftEmit.cs`** — per-named-type private static `Lift{Name}`
  helpers on the harness. Variant lift dispatches via discriminator
  Beq chain, lifts payload, newobjs the matching subclass; throws
  `InvalidDataException` on unknown discriminator.
- **`WorldHarnessEmit.cs`** — generic flat-lowered wrapper path:
  for each user-facing param, unwrap fields into the lowered
  primitive call args (record's `a` becomes `a.X, a.Y`); for
  record / variant returns, treat the invoker return as an
  indirect ret-area i32 and call the registered `Lift{Name}` to
  produce the typed value. Strings still take the dedicated
  string-in / string-out path.

Aggregate-return cabi_post detection: returns containing strings
or lists transitively get a `cabi_post_*` invoker; pure-primitive
records / variants don't (Rust + wit-bindgen don't emit
`cabi_post_*` for those — `wasm-tools print` on richer.component.wasm
confirms no `cabi_post_add` or `cabi_post_normalize-or-fail`).

### What still throws

The v0.2 emitter intentionally fails loud on:
- Strings, lists, options, results, nested records inside
  records, multi-results.
- Inline-interface exports (`export wasi:foo/iface`).
- Variants with > 256 cases (1-byte discriminator assumed).
- Resource handles, flags, enums.

These light up the next fixture pass; each closes incrementally.

`Harness.Runtime` minor-bumps to 0.3.0 — adds
`MemoryHelpers.ReadU8` / `WriteU8` (variant discriminator
reads). Family tag: `WACS-ComponentModel-v0.7.0` →
`WACS-ComponentModel-v0.8.0` (minor — capability shift on
Harness.Lib + Harness.Runtime).

### What changed

- **`WACS.ComponentModel.Harness.Lib` 0.1.0 → 0.2.0** (minor —
  records + variants): `CanonicalAbi.cs`, `WitTypeEmit.cs`,
  `LiftEmit.cs` (new); `WorldHarnessEmit.cs` substantially expanded
  (~+200 LOC) — generic flat-lowered path + lifted-return dispatch.
- **`WACS.ComponentModel.Harness.Runtime` 0.2.0 → 0.3.0** (minor —
  public API addition): `MemoryHelpers.ReadU8` / `WriteU8`.
- **New fixture**:
  `Spec.Test/components/fixtures/wit-harness-spike-richer/` —
  Rust + WIT + built `.component.wasm` (13.7 KB, no WASI imports)
  + `Generated.Validate` console.

## WACS.ComponentModel.Harness.Lib 0.1.0 / WACS.ComponentModel.Harness.Runtime 0.2.0 / WACS.ComponentModel 0.5.2 — IL-emitting harness generator

`WACS.ComponentModel.Harness.Lib` is the third sibling in the
ComponentModel family this round — the IL emitter that takes a
WIT contract and produces a `.dll` containing a typed harness
class. Built on `PersistedAssemblyBuilder` (.NET 9), targets
net9.0, matches the architectural shape `Wacs.Transpiler.Lib`
already uses for the wasm → IL direction.

Single emission core behind three ergonomic surfaces — the
`wacs harness gen` distribution flow (file/stream output) and the
`wacs run --wit-dir` / `wacs transpile --wit-dir` in-process
flows (memory output) all funnel through the same builder pass:

- `HarnessEmitter.EmitToFile(witDir, outPath)` — saves a `.dll`.
- `HarnessEmitter.EmitToStream(witDir, stream)` — lowest-level.
- `HarnessEmitter.EmitInMemory(witDir) → Assembly` — `Save` to
  a `MemoryStream`, then `AssemblyLoadContext.LoadFromStream` for
  immediate use, mirroring `Wacs.Transpiler.Lib`'s `Bake` shape.

v0 emits the spike's `func(string) -> string` shape end-to-end:
the IL builds a sealed `HelloHarness` class with five private
readonly fields (runtime + memory + cabi_realloc invoker + per-
export invoker + per-export `cabi_post_*` invoker), a private
ctor, a public static `LoadFrom(byte[], Action<WasmRuntime>?)`
factory that funnels through `HarnessLoader`, and a public typed
`Greet(string) -> string` method that calls
`StringCoding.LowerUtf8` / `MemoryHelpers.ReadI32LE` /
`StringCoding.LiftUtf8` from `Harness.Runtime`. Records, variants,
multi-result returns, list types, and inline-interface exports
throw `NotSupportedException` at emit time — loud failure beats
silent mis-emission, and these gaps close as the richer fixture
exercises them.

**Validation**: a new console
`Spec.Test/components/fixtures/wit-harness-spike-hello/Generated.Validate/`
emits `HelloHarness` in-memory from the spike's `wit/world.wit`,
loads the spike's `hello.component.wasm` via reflection-driven
`LoadFrom`, calls `Greet("World")`, and asserts the result equals
`"Hello, World!"` — same output the hand-written spike emits.
The generated harness is functionally equivalent to the
hand-written one in `Aot.Spike/HelloHarness.cs` (post the
HarnessLoader refactor in this same commit).

`Harness.Runtime` minor-bumps to 0.2.0 for the new
`HarnessLoader` + `LoadedComponent` public API surface — the
load + export-resolution boilerplate every emitted `LoadFrom`
calls into. `Wacs.ComponentModel` point-bumps to 0.5.2 for the
new `InternalsVisibleTo Wacs.ComponentModel.Harness.Lib`
attribute (Harness.Lib needs `NameMangler` for kebab → PascalCase
identifier conversion, kept as a single source of truth).

Family tag: `WACS-ComponentModel-v0.6.0` → `WACS-ComponentModel-v0.7.0`
(minor — new sibling package).

### What changed

- **`WACS.ComponentModel.Harness.Lib` 0.1.0** (new package):
  `HarnessEmitter.cs` + `HarnessOptions.cs` + `WorldHarnessEmit.cs`
  (~570 LOC total). Targets net9.0 for `PersistedAssemblyBuilder`.
  Internal-visible to `Wacs.ComponentModel.Harness.Lib.Test`.
- **`WACS.ComponentModel.Harness.Runtime` 0.1.0 → 0.2.0** (minor —
  public API addition): adds `HarnessLoader` (parse + instantiate
  + register), `HarnessLoader.RequireMemoryExport`,
  `HarnessLoader.RequireFunctionExport`, and the
  `LoadedComponent(WasmRuntime, ModuleInstance)` return shape.
  Used by both the refactored hand-written spike and the IL
  emitted by `Harness.Lib`.
- **`WACS.ComponentModel` 0.5.1 → 0.5.2** (point — metadata): adds
  `InternalsVisibleTo Wacs.ComponentModel.Harness.Lib` so the
  emitter can reuse `NameMangler` for kebab → PascalCase /
  CamelCase conversions.
- **Spike**: `Aot.Spike/HelloHarness.cs` collapsed from ~250 →
  ~85 LOC by routing through `Wacs.ComponentModel.Harness.HarnessLoader`.
  Cleared the un-needed `using` lines; AOT-publish + native run
  still emit `"Hello, World!"`.
- **Spike validation**:
  `Spec.Test/components/fixtures/wit-harness-spike-hello/Generated.Validate/`
  (new console fixture, net9.0) — emits HelloHarness via
  `HarnessEmitter.EmitInMemory`, drives via reflection, asserts
  result against hand-written spike's output.

## WACS.ComponentModel.Harness.Runtime 0.1.0 — canonical-ABI primitives

New sibling package in the ComponentModel family. Carries the
AOT-safe canonical-ABI runtime primitives that emitted harness IL
calls at component-load time. v0 surface (kept minimal —
intentionally what the wit-harness-spike-hello fixture exercises;
grows as the richer fixture surfaces what's missing):

- **`MemoryHelpers`** — little-endian `ReadI32LE` / `WriteI32LE`
  over `MemoryInstance.Data`. The canonical ABI mandates LE on
  every numeric width; one call site per access in emitted IL.
- **`StringCoding`** — `LiftUtf8(memory, ptr, byteLen) -> string`
  for strings flowing wasm → host, and `LowerUtf8(memory, value,
  cabiRealloc, out ptr, out byteLen)` for strings flowing host →
  wasm. UTF-16 + Latin1 canonical-ABI encodings deferred to v0.2
  when the richer fixture surfaces them.

Validation: the spike's hand-written `HelloHarness` refactored to
call into `MemoryHelpers.ReadI32LE` / `StringCoding.LiftUtf8` /
`StringCoding.LowerUtf8` instead of inline helpers. Both
`dotnet run` and the NativeAOT-published native binary still emit
`"Hello, World!"`.

`WitContract` / `WitContractDiff` / `WitContractMismatchException`
are deliberately deferred to the next slice — they'll land
together with the Harness.Lib emitter that populates them, so the
shape is validated against an actual consumer before shipping.

Multi-target net8.0 + netstandard2.1; `IsAotCompatible` gated to
net8.0.

Family tag: `WACS-ComponentModel-v0.5.0` → `WACS-ComponentModel-v0.6.0`
(minor — second new sibling package this round).

### What changed

- **`WACS.ComponentModel.Harness.Runtime` 0.1.0** (new package):
  `MemoryHelpers.cs` (~30 LOC), `StringCoding.cs` (~70 LOC),
  csproj depending on `Wacs.Core` + `Wacs.ComponentModel.Parser`.
- **Spike** (`Aot.Spike/HelloHarness.cs`): refactored to call into
  `Wacs.ComponentModel.Harness.MemoryHelpers` /
  `Wacs.ComponentModel.Harness.StringCoding`; inline `ReadI32LE`
  + `Encoding.UTF8.*` removed. Csproj gains the new ProjectReference.

## WACS.ComponentModel.Parser 0.1.0 / WACS.ComponentModel 0.5.1 — AOT-safe parser split

`WACS.ComponentModel.Parser` is a new sibling package in the
ComponentModel family. It carries the AOT-safe binary parser
(`Wacs.ComponentModel.Runtime.Parser.*` — ten section readers,
`ComponentBinaryParser`, `ComponentBinaryReader`) plus the typed
output model (`Wacs.ComponentModel.Runtime.ComponentModule`) — pure
byte walkers, no reflection, multi-target net8.0 + netstandard2.1
with `IsAotCompatible` gated to net8.0.

Existing `WACS.ComponentModel` (point bumped to 0.5.1) becomes a
downstream consumer via project reference — the type names,
namespaces, and `using` statements are unchanged for callers who
take both packages transitively. Callers who want *only* the
parser (the wit-harness consumers, per
`docs/wit-harness-plan.md` and the closeout
`docs/wit-harness-unity-spike.md` finding D) can now reference
`WACS.ComponentModel.Parser` standalone and avoid pulling the
reflective `ComponentInstance` / `ComponentBridge` /
`HostInterfaceRuntime` surface (which carries
`[RequiresDynamicCode]` + `[RequiresUnreferencedCode]` and isn't
viable on Unity IL2CPP).

The `WACS.ComponentModel.Parser` package is the first chunk of
`docs/wit-harness-plan.md` productionization — Harness.Runtime
and Harness.Lib will reference it directly.

### What changed

- **`WACS.ComponentModel.Parser` 0.1.0** (new package): split from
  `Wacs.ComponentModel`. Contains
  `Wacs.ComponentModel/Runtime/Parser/*.cs` (10 files, ~1900 LOC)
  + `Wacs.ComponentModel/Runtime/ComponentModule.cs` (~530 LOC).
- **`WACS.ComponentModel` 0.5.0 → 0.5.1** (point — transparent
  refactor): adds a project reference to
  `Wacs.ComponentModel.Parser`; the moved types remain
  transitively visible to consumers. `Wacs.ComponentModel/WIT/BinaryWitDecoder.cs`
  + `Wacs.ComponentModel/Runtime/ComponentInstance.cs` still use
  the parser types unchanged.
- **Spike**: `Spec.Test/components/fixtures/wit-harness-spike-hello/Aot.Spike/`
  switches from referencing `WACS.ComponentModel` to
  `WACS.ComponentModel.Parser` — drops the
  `[RequiresDynamicCode]` surface from its AOT call graph and
  resolves the prior `NETSDK1210` warning the
  unconditional `IsAotCompatible` in `Wacs.ComponentModel` produced.

Family tag: `WACS-ComponentModel-v0.4.0` → `WACS-ComponentModel-v0.5.0`
(minor — new sibling package counts as a capability shift for the
family).

## WACS 0.15.22 — `CreateInvokerFunc<…,TResult>` unboxes via `Value`

`WasmRuntime.CreateInvokerFunc<…,TResult>` (every arity, 0–9 args)
wrapped the wasm return in `Value` (per `Delegates.AnonymousFunctionFromType`),
boxed it via `DynamicInvoke`, and then attempted `(TResult)boxed`. For
primitive `TResult` (e.g. `int`), that's an unbox-to-primitive against
a boxed `Value` struct — `InvalidCastException` at every call.

Added a `UnboxReturn<TResult>` helper that drops the `Value` wrapper
by reading `Value.Scalar` — the existing discriminator-driven switch
that already returns the typed field for `I32` / `I64` / `F32` /
`F64` / `V128` / refs. The standard `(TResult)object` unbox then
matches whatever primitive `Scalar` returned. When `TResult == Value`
(the shape existing `BindingTests` use — bind to `Func<…, Value>`
and lift via Value's implicit operators at the call site, e.g.
`(int)invoker(1)`), the helper returns the boxed `Value` straight
through. When the boxed object is *not* a `Value` (host-function
returns flow through as primitives), the helper falls through to a
direct `(TResult)boxed` unbox. Every `CreateInvokerFunc` arity
(0–9 args) now routes through it.

Surfaced by the wit-harness AOT spike (Package 3 of
`docs/wit-harness-plan.md`): the spike's canonical-ABI call into the
component's `cabi_realloc` / `greet` / `cabi_post_greet` exports
needed typed `Func<int, int, int, int, int>` etc. delegates, which
hit the bug on the first invocation. Hand-written harness now runs
end-to-end on both `dotnet run` and a NativeAOT-published native
binary, returning the expected `"Hello, World!"`.

`CreateInvokerAction` is unaffected — the void-return path doesn't
unbox.

### What changed

- **`WACS` 0.15.21 → 0.15.22** (point — bug fix):
  - `WasmRuntimeExecution.cs` adds `UnboxReturn<TResult>` and routes
    every `CreateInvokerFunc` overload through it.

## Warning hygiene pass — WACS 0.15.21 / WACS.Cli 1.8.1 / WACS.ComponentModel 0.5.0 / WACS.ComponentModel.Bindgen.Lib 0.1.2 / WACS.HostBindings.SourceGen 0.1.1 / WACS.WASI.NN.OpenVino 0.2.2 / WACS.WASI.Preview1 0.13.1 / WACS.Transpiler.Lib 0.9.1

Solution-wide warning count: **390 → 0** (100% reduction).
Coordinated cleanup across nullable reference types (CS86xx), AOT
trim safety (IL2xxx / IL3050), dead code (CS0162 / CS0219 /
CS0169 / CS8321), equality / inheritance hygiene (CS0660 /
CS0661 / CS0108 / CS0465), platform compat (CA1416), package
restore (NU1701), analyzer release tracking (RS2008), source-gen
WIT-style identifiers (CS8981), async-without-await (CS1998),
analyzer version mismatch (CS9057), and several smaller
categories.

**Tests verified**: 1369+ pass across affected suites
(`ComponentModel.Test` 354/354, `Transpiler.Test` 826/826,
`WASI.Preview2.Test` 189/189, `WASI.NN.Test` 21/21,
`WASI.GFX.Test` 16/16, `WASI.GFX.Webgpu.Test` 66/66,
`WASI.GFX.Silk.Test` 3/3). Two `CallIndirect_dispatches_via_funcref_table`
failures in `Wacs.Compilation.Test` are pre-existing on `main`,
unaffected by this work.

### What changed

- **`WACS` 0.15.20 → 0.15.21** (point — internal correctness):
  - Bulk `!` (null-forgiving) annotations across ~24 hot-loop
    files. Per `feedback_core_perf.md`: `!` is the right tool
    in the dispatch loop — compile-time-only assertion, zero
    runtime IL impact.
  - `TypeIdx` gains `Equals(object?)` + `GetHashCode()` to
    match its existing `==` / `!=` operators (CS0660 / CS0661
    closed by structural fix).
  - `FunctionType.Validator` `new`-keyword on the legitimate
    hide of `CompositeType.Validator` (CS0108).
  - `MemAddrs.Finalize()` carries an explicit
    `#pragma warning disable CS0465` with comment — the method
    name overlaps `Object.Finalize` but `Object.Finalize` is
    `protected` so no actual collision occurs; preserved for
    API compat.
  - `InstFusedLocalSet.cs` drops the unused `type` field.
  - `ExecContext.ResetStats` carries
    `[UnconditionalSuppressMessage("AOT", "IL3050")]` —
    `Enum.GetValues(typeof(OpCode))` is safe because the
    opcode enums are statically referenced by the dispatcher.
  - `Runtime/Delegates.ValidateFunctionTypeCompatibility`,
    `Runtime/Types/HostFunction` constructor +
    `CreateConversionHelper` carry the standard
    `[UnconditionalSuppressMessage]` set with documented
    rationale.
  - `Attributes/WatTokenAttribute.ToWat<T>`,
    `OpCodes/OpCodeExtensions.LookUp<T>`,
    `Text/Mnemonics.AddEnum<T>` — `[UnconditionalSuppressMessage]`
    for generic-T reflection over enum field metadata.
  - `Runtime/Equals(object?)` mismatch fixed on `Value`,
    `OpCodes/ByteCode`, `Types/GlobalType`,
    `Compilation/BytecodeCompiler.RefEq<T>` — `object obj`
    parameters → `object? obj`, `(T,T)` → `(T?,T?)` to match
    the base / interface (CS8765 / CS8767).
  - `Instructions/InstructionBase.ExecuteAsync` +
    `Instructions/TailCall.ExecuteAsync` (3 overrides) drop
    the `async` modifier on methods that never await
    (CS1998). Method-signature change in metadata
    (`async ValueTask` → `ValueTask`); callers see identical
    wait-semantics.
  - `<NoWarn>CS9057</NoWarn>` — the .NET 8 SDK ships
    analyzers built against a newer Roslyn than
    `LangVersion 9` resolves. They don't fire on our code;
    suppress the version-mismatch noise.
  - New `Compatibility/AotAttributePolyfills.cs`: internal
    polyfill for `UnconditionalSuppressMessage` /
    `DynamicallyAccessedMembers` /
    `DynamicallyAccessedMemberTypes` /
    `RequiresUnreferencedCode` / `RequiresDynamicCode` when
    targeting netstandard2.1 (where the BCL doesn't have
    them). Wrapped in `#if !NET5_0_OR_GREATER` so the BCL
    versions take precedence on net8.0+.
- **`WACS.Cli` 1.8.0 → 1.8.1** (point — internal cleanup):
  CA1416 fix in `AotHandler.SetUnixFileMode` (wrap with
  `!OperatingSystem.IsWindows()` so the analyzer sees the
  guard); dead null-check removal in
  `InspectHandler.InspectComponent`.
- **`WACS.ComponentModel` 0.4.0 → 0.5.0** (minor — new public
  API annotations):
  - `ComponentInstance.Instantiate(byte[]/Stream/ComponentModule)`
    + `ComponentBridge.AsTypedInterface<T>` +
    `AsTypedInterface(Type)` + `AsHostBundle` carry
    `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]`.
    These paths use `MakeGenericType` /
    `MethodInfo.MakeGenericMethod` /
    `Activator.CreateInstance(Type)` at every WIT boundary —
    fundamentally incompatible with NativeAOT / Unity
    IL2CPP. The annotations make AOT consumers see clean
    compile-time errors pointing at
    `docs/wit-harness-plan.md` (the forward AOT path)
    rather than runtime `ExecutionEngineException`.
  - `WitContract.FromAssembly` +
    `HostInterfaceRuntime.InvokeStaticFactoryReflective`
    carry `[RequiresUnreferencedCode]` — same rationale,
    different trim category.
  - File-level `#pragma warning disable IL2026, IL2067, …`
    over the interior of these four files; the public-entry
    annotations carry the actual contract.
  - Internal `AotAttributePolyfills.cs` synced from
    `Wacs.Core`.
- **`WACS.ComponentModel.Bindgen.Lib` 0.1.1 → 0.1.2** (point —
  internal suppressions): `WitReverse.ExtractWitBytes` overloads
  carry `[UnconditionalSuppressMessage]` for the
  `Assembly.LoadFrom` + `GetTypes` + `GetField` chain that
  reads transpiler-emitted `ComponentMetadata.EmbeddedWitBytes`.
- **`WACS.HostBindings.SourceGen` 0.1.0 → 0.1.1** (point —
  analyzer-release tracking): added
  `AnalyzerReleases.Shipped.md` (empty) +
  `AnalyzerReleases.Unshipped.md` (lists WACS001..WACS004
  with category / severity). Required for the analyzer
  NuGet to satisfy RS2008.
- **`WACS.WASI.NN.OpenVino` 0.2.1 → 0.2.2** (point —
  `<PackageReference … NoWarn="NU1701" />` on the
  `OpenVINO.runtime.macos-arm64` native-only runtime — the
  RID package legitimately ships no managed surface for the
  target framework, NETFx-moniker restore fallback is
  benign).
- **`WACS.WASI.Preview1` 0.13.0 → 0.13.1** (point —
  `<NoWarn>CS8981</NoWarn>` for the 84 WITX-mandated lower-
  case type names (`ptr`, `fd`, `size`, `timestamp`, …);
  `FsPath.cs:478` unreachable-code fix on the
  netstandard2.1 leg).
- **`WACS.Transpiler.Lib` 0.9.0 → 0.9.1** (point — `!`
  annotations + dead-code drops: unused `offsetSoFar` in
  `DirectLinkedImportEmit`, plus null-forgiving over the
  IImports-walk reflection sites).

Also coordinated (already bumped in earlier commits on this
branch):
- **`WACS.WASI.Preview2.DependencyInjection` 0.3.0** — new
  `[WasiScopeBootstrap]` attribute + `IWasiScopeBootstrap`
  interface; hardcoded NN / GFX paths deleted.
- **`WACS.WASI.NN.DependencyInjection` 0.3.0** — new
  `NNScopeBootstrap` self-registers via the attribute.
- **`WACS.WASI.GFX.DependencyInjection` 0.1.1-preview →
  0.2.0-preview** — new `GfxScopeBootstrap` analogously.
- **`WACS.WASI.NN` 0.4.0 → 0.4.1** — NuGet description +
  packed README refresh (3 backends added since last update,
  WIT version reference corrected, Phase 2 status callout).

### Forward work

`docs/wit-harness-plan.md` captures the design for the next
PR — a build-time WIT-shaped harness SourceGen that makes the
Component Model AOT story real (Unity IL2CPP + NativeAOT both
viable). The current PR's `[RequiresDynamicCode]` annotations
are the honest "this path doesn't work under AOT" signal;
the harness is the AOT-compatible path that will exist as a
separate package surface.

## WACS.WASI.Preview2.DependencyInjection 0.3.0 / WACS.WASI.NN.DependencyInjection 0.3.0 / WACS.WASI.GFX.DependencyInjection 0.2.0-preview — attribute-driven scope discovery

`WasiPreview2RuntimeScope` no longer hardcodes the wasi-nn /
wasi-gfx assembly + type + method names it used to look up at
scope-build time. New subsystem DI packages plug in by shipping
their own `IWasiScopeBootstrap` implementation and an
assembly-level `[WasiScopeBootstrap(typeof(MyBootstrap))]`
attribute; the scope walks every loaded assembly for the
attribute and invokes each pointed-at bootstrap.

This is a coordinated upgrade — Preview2.DI 0.3.0 deletes the
hardcoded NN/GFX paths, so it must be paired with NN.DI 0.3.0
(ships `NNScopeBootstrap` + attribute) and GFX.DI 0.2.0-preview
(ships `GfxScopeBootstrap` + attribute). Consumers using the
in-tree project references upgrade together; NuGet consumers
should bump all three at once.

### What changed

- **`WACS.WASI.Preview2.DependencyInjection` 0.2.2 → 0.3.0**
  (minor — behavior change): `WasiPreview2RuntimeScope` adds
  `IWasiScopeBootstrap` interface + `WasiScopeBootstrapAttribute`
  as public API. Deletes the previous hardcoded
  `ReflectivelyAddWasiNN` / `ReflectivelyAddWasiGfx` /
  `BuildAutoDiscoveredCallback` / `ApplyAllRegistrants` /
  `DiscoverBackendRegistrants` / `TryLoadAssembly` helpers — the
  attribute walk supersedes all of them. ~290 lines deleted.
- **`WACS.WASI.NN.DependencyInjection` 0.2.3 → 0.3.0** (minor —
  new public type): ships `NNScopeBootstrap : IWasiScopeBootstrap`
  with the wasi-nn backend auto-discovery logic that previously
  lived in Preview2.DI. The NN-specific reflection (walking for
  `IWasiNNBackendRegistration` implementations) now lives in the
  NN package, where the interface type can be referenced directly
  via `typeof(IWasiNNBackendRegistration)` instead of a string-
  based `Assembly.GetType` lookup.
- **`WACS.WASI.GFX.DependencyInjection` 0.1.1-preview → 0.2.0-preview**
  (minor — new public type): ships `GfxScopeBootstrap` with the
  AddWasiGfx + AddWasiPreview2GfxBundle registration calls that
  previously lived in Preview2.DI.

### AOT correctness

The new path is AOT-safe: the only reflection is
`Activator.CreateInstance(attr.Type)`, where `attr.Type` comes
from a `typeof(...)` token statically referenced in the sibling
assembly's `[WasiScopeBootstrap(...)]` argument. Trimming
preserves the type and its public parameterless constructor
because they're rooted by static reference. The remaining
`Assembly.GetExportedTypes` walks (composite bundle + backend
registrant discovery) are suppressed with documented
justifications.

### Warning impact

Solution-wide IL warnings: 106 → 76 (−30). The 30 warnings in
`WasiPreview2RuntimeScope` dropped to 0; the new
`NNScopeBootstrap` carries 0 unsuppressed warnings.

## WACS.WASI.NN 0.4.1 — Phase 2 status callout + README backend catch-up

NuGet description-only refresh. No code or API change; existing
0.4.0 consumers can upgrade transparently.

### What changed

- **`WACS.WASI.NN` 0.4.0 → 0.4.1** (point — doc-driven):
  - NuGet `<Description>` now names all six bundled backends
    (ONNX Runtime, ONNX Runtime GenAI, ML.NET, LlamaSharp,
    TorchSharp, OpenVINO), corrects the WIT-version reference
    (`wasi:nn@0.2.0-rc-2024-10-28`, matches the pinned wit),
    and adds a Phase 2 status sentence.
  - README packed with the NuGet: package table now lists the
    three backend siblings added since the last README refresh
    (`OnnxRuntimeGenAI`, `TorchSharp`, `OpenVino`); encoding
    routing table updates `pytorch` → `TorchSharpBackend`,
    `openvino` → `OpenVinoBackend`, ggml lists both
    `LlamaSharpBackend` + `OnnxRuntimeGenAIBackend`. Adds a
    Standardization-status section pointing at the upstream
    WASI Proposals.md Phase 2 listing.

The seven sibling wasi-nn packages (`WACS.WASI.NN.DependencyInjection`,
`.OnnxRuntime`, `.OnnxRuntimeGenAI`, `.MLNet`, `.LlamaSharp`,
`.TorchSharp`, `.OpenVino`) also had a Phase 2 status sentence
appended to their NuGet descriptions; bumps for those land with
their next functional change rather than triggering a fan-out
point release for description-only edits.

## WACS.WASI.GFX.Webgpu 0.1.0-preview / WACS.WASI.GFX 0.2.0-preview / WACS.WASI.GFX.Silk 0.2.0-preview / WACS.WASI.GFX.DependencyInjection 0.1.1-preview / WACS.WASI.Preview2 0.5.0 / WACS.WASI.Preview2.DependencyInjection 0.2.2 / WACS.ComponentModel 0.4.0 / WACS.Transpiler.Lib 0.9.0 / WACS.Cli 1.8.0 / WACS 0.15.20 — wasi-gfx v1 (wasi:webgpu host bindings + Phase 1/2 polish)

The four `WACS.WASI.GFX.*` packages adopt a NuGet `-preview`
suffix to signal that the wasi-gfx proposal is at WASI Phase 2
(not yet standardized). Consumers need `dotnet add package … --prerelease`
to install. The wasi-nn family (also tracking a Phase 2 proposal)
adds the same status callout in its NuGet `<Description>` but keeps
its stable version trajectory — those packages already have shipped
consumers.

v1 release for the wasi-gfx family. The fourth wasi-gfx WIT
package — `wasi:webgpu@0.0.1`, ~35 KB of WIT mirroring the
browser WebGPU API — gets host bindings in a new sibling
package `WACS.WASI.GFX.Webgpu`. The Silk.NET/SDL backend
extends to wrap Silk.NET.WebGPU / wgpu-native so one Silk
dependency serves both CPU (frame-buffer / surface) and GPU
(webgpu) paths through a single `--wasi-gfx`/`--bind` flag.
Phase 1 (implementor-DX architectural fixes) and Phase 2
(window-close graceful shutdown) ship alongside.

Architecture mirrors v0: a contract package owns the SPI +
canonical-ABI bindings, backends ship as sibling packages, and
a graphics-context bridge connects the CPU and GPU paths
through polymorphic `IAbstractBuffer` marker sub-interfaces.

Host-side coverage for the wasi:webgpu surface is wired across
all 38 resources at the lifecycle level (label / set-label /
[resource-drop]) plus the entry-point compute path through
queue.submit. Pipeline-create + descriptor-decoding methods on
gpu-device + the wgpu-native dispatch implementation in
SilkGpuBackend are skeletons targeting a follow-up cut; the
binding gates are registered so guests reach a clear
`PlatformNotSupportedException` rather than "missing handler"
when they call into the unwired arms. The `wasi-webgpu-hello`
parity fixture at `Spec.Test/components/fixtures/` exercises
every wire-form binding the stub backend supports end-to-end.

### What changed

- **`WACS.WASI.GFX.Webgpu` 0.1.0** (new sibling): host
  bindings for `wasi:webgpu@0.0.1`. Source-gen emits 39
  `[WitSource]`-tagged interfaces from
  `webgpu.wit`. `IGpuBackend` SPI exposes `CreateGpu()` +
  `FromAbstractBuffer(IAbstractBuffer)` (the graphics-context
  bridge). `WasiWebgpuHost` owns 31 per-resource tables;
  `WasiWebgpuConfiguration.AbstractBufferResolver` plumbs the
  cross-host abstract-buffer handle lookup. WitBindings.cs
  wires ~70 canonical-ABI host functions covering the entry
  points (`get-gpu`, `[method]gpu.*`), gpu-adapter + gpu-device
  query surface, buffer/shader/pipeline-layout/bind-group
  lifecycle, the full compute path
  (compute-pipeline + command-encoder + compute-pass-encoder +
  command-buffer + queue.submit), render-path resource
  lifecycle (texture / texture-view / sampler / render-pipeline
  / render-bundle), and the `[static]gpu-texture.from-graphics-
  buffer` graphics-context bridge. New helper `BindLabeled` +
  `BindDebugMarkers` collapse the label-and-debug boilerplate
  shared across encoder / pass-encoder resources. Deferred to a
  follow-up cut: descriptor decoding for the create-* methods
  on gpu-device (20+ i32 flat-form param shapes that exceed
  `Func<T1..T16, TResult>` arity), result<_, error> shapes on
  buffer.map-async / unmap / get-mapped-range-*, the option<...>
  param decoding for set-bind-group / write-buffer-with-copy.
- **`WACS.WASI.GFX` 0.1.0 → 0.2.0** (minor — new public
  interfaces): `ICpuAbstractBuffer` / `IGpuAbstractBuffer`
  marker sub-interfaces on `IAbstractBuffer`. The wasi-gfx WIT
  itself doesn't distinguish — one `abstract-buffer` handle
  moves through both worlds at the wire level — but the marker
  pair lets backends produce one kind and consumers downcast
  cleanly without backend-specific knowledge. The frame-buffer
  device's `FromGraphicsBuffer` now downcasts on the marker
  first so a guest accidentally feeding a GPU buffer to the
  CPU path gets "wrong kind" instead of a backend-specific
  cast failure.
- **`WACS.WASI.GFX.Silk` 0.1.0 → 0.2.0** (minor — capability:
  adds GPU backend): `SilkGpuBackend : IGpuBackend` skeleton
  with `Silk.NET.WebGPU 2.23.0` + `Silk.NET.WebGPU.Native.WGPU
  2.23.0` package references. `SilkGpu.GetPreferredCanvasFormat`
  reports `Bgra8UnormSrgb`; `RequestAdapter` /
  `FromAbstractBuffer` throw `PlatformNotSupportedException`
  pending the wgpu-native dispatcher follow-up.
  `WasiGfxSilkBindable` constructs the GPU pair alongside the
  CPU pair and wires the cross-host
  `AbstractBufferResolver` automatically.
  `SilkGfxBackend` Phase 2 changes: `QuitRequested` event +
  sticky `IsQuitRequested` property; `EventType.Quit` and the
  last-window `WindowEventID.Close` both signal it.
  `SilkAbstractBuffer` now implements `ICpuAbstractBuffer`.
- **`WACS.WASI.GFX.DependencyInjection` 0.1.0 → 0.1.1**
  (point): `WasiPreview2GfxBundle` carries
  `[WacsCompositeBundle("wasi-gfx", Priority = 10)]` for
  the attribute-driven discovery rewrite.
- **`WACS.WASI.Preview2` 0.4.1 → 0.5.0** (minor — behavior
  change): `IoBindings` now registers
  `wasi:io/error` + `wasi:io/poll` handlers at every
  shape-stable point release (`0.2.0` through `0.2.8`). Guests
  built against an older io version bind without a host-side
  WIT bump; the wasi-gfx vendored WIT in
  `Wacs.WASI.GFX/wit/deps/io/` reverts to upstream-verbatim
  `wasi:io@0.2.0`. The `Strict_canon_abi_drift_is_bounded`
  test learns to ignore the expected ExtraBinding entries for
  `wasi:io@0.2.0..0.2.7` since they're intentional
  compatibility shims rather than real drift.
- **`WACS.WASI.Preview2.DependencyInjection` 0.2.1 → 0.2.2**
  (point — attribute-driven discovery): `WasiPreview2NNBundle`
  + `WasiPreview2GfxBundle` consumers walk
  `[WacsCompositeBundle]` attribute-tagged composites with
  Priority desc / Family asc ordering instead of the hardcoded
  qualified-name cascade. `[WacsDependencyInjectionSibling]`
  on contract assemblies drives the sibling DI auto-load
  (deletes ~30 lines of belt-and-suspenders
  `Assembly.Load` + CLI sibling-listing).
- **`WACS.ComponentModel` 0.3.6 → 0.4.0** (minor — new public
  types):
  - `WacsCompositeBundleAttribute` (Family + Priority) — drives
    the attribute-driven composite-bundle discovery.
  - `WacsDependencyInjectionSiblingAttribute` (AssemblyName) —
    declares DI siblings on contract assemblies so
    `HostPackageResolver` + `WasiPreview2RuntimeScope`
    auto-load them.
  - WIT parser relaxation: `ConsumeIdent` now accepts
    Keyword-class tokens positionally. The wasi:webgpu
    `vertex-format` enum has cases named `float32`, `sint32`,
    etc. — bare primitive-type lexemes that the spec allows as
    identifiers in non-type position but the previous strict-
    Ident parser rejected.
- **`WACS.Transpiler.Lib` 0.8.16 → 0.9.0** (minor — new public
  diagnostics + emit changes):
  - `TranspilerDiagnostics` env-var-gated trace surface
    (`WACS_TRANSPILER_DEBUG=1`): logs direct-link emit
    rejections (gate name + reason) and `IImports` stub
    lenient-default serves. Closes v0's "wasm hangs at 100%
    CPU with no output" debugging hole.
  - First-class IL emit for `[static]X.foo` resource methods:
    bypasses `HostInterfaceRuntime.InvokeStaticFactoryReflective`
    when the resolver locates an impl class with `{Name}Static`.
  - Attribute-driven composite-bundle discovery
    (`FindBestComposite` / `SelectBestComposite` sort helper),
    DI-sibling auto-discovery (`LoadDeclaredSiblings`).
  - 44-row canonical-ABI shape coverage matrix
    (`CanonicalAbiCoverageTests`) locks every CLR↔wasm shape
    `DirectLinkedImportEmit.CanEmitDirect` currently accepts.
- **`WACS.Cli` 1.7.8 → 1.8.0** (minor — behavior change for
  `--windowed`):
  - New `--trace-imports` flag (point-level addition by
    itself) toggles `TranspilerDiagnostics.Enabled` for the
    run.
  - Phase 2 quit-shutdown: `ExecuteWindowed` subscribes to the
    SDL backend's `QuitRequested` event (Cmd-Q / window close
    / Alt-F4 / SIGTERM) and cancels the wasm task's
    `CancellationTokenSource` on signal. If the wasm guest
    doesn't observe cancellation within 100ms (typical for
    `poll()`-wedged guests), the host process-exits with the
    last wasm-produced exit code instead of hanging until
    Ctrl-C.
  - `ResolveHostPackages` collapses: `--wasip2` / `--wasi-nn`
    / `--wasi-gfx` name only the contract assemblies now; the
    DI siblings auto-load via the attribute.
- **`WACS` 0.15.19 → 0.15.20** (point): WIT parser carries the
  ConsumeIdent relaxation (lives in `Wacs.Core` via the
  `Wacs.ComponentModel` package's source tree).

### Family tag

`WACS-WASI-GFX-v0.2.0` — the family-wide minor bump anchors the
new `WACS.WASI.GFX.Webgpu` sibling at its 0.1.0 baseline plus
the capability-changing `WACS.WASI.GFX` + `WACS.WASI.GFX.Silk`
minor bumps. No previous `WACS-WASI-GFX-v*` tag existed; the v0
ship in commit `81b07e1c` predated the family-tag convention.

### Parity fixture

`Spec.Test/components/fixtures/wasi-webgpu-hello/` — minimal
Rust component built against the `wasi:webgpu@0.0.1` wire
surface. Calls `get-gpu`, `gpu.get-preferred-canvas-format`,
`gpu.wgsl-language-features`, `wgsl-language-features.has`, and
the matching `[resource-drop]`s. Traps via `unreachable!()` on
expectation mismatch — successful `start()` return signals all
wire-form decoding landed correctly. Pre-built
`wasm/hello.component.wasm` (16 KB) checked in.
`hello_compute` / `skybox` full-pipeline parity fixtures land
alongside the wgpu-native dispatcher follow-up.

## WACS.WASI.GFX 0.1.0 / WACS.WASI.GFX.Silk 0.1.0 / WACS.WASI.GFX.DependencyInjection 0.1.0 / WACS.ComponentModel 0.3.6 / WACS.Transpiler.Lib 0.8.16 / WACS.WASI.Preview2.DependencyInjection 0.2.1 / WACS.Cli 1.7.8 — wasi-gfx host bindings (v0)

Initial release of host bindings for the
[wasi-gfx](https://github.com/WebAssembly/wasi-gfx) proposal
(Phase 2). v0 covers three of the four wasi-gfx WIT packages —
`wasi:graphics-context@0.0.1`, `wasi:frame-buffer@0.0.1`,
`wasi:surface@0.0.1` — the CPU rendering path with windowing
and input. `wasi:webgpu@0.0.1` (35 KB of WIT mirroring the
browser WebGPU API) is a v1 target; the RayLib backend the
original plan called out as a parallel sibling is deferred.

Architecture mirrors `WACS.WASI.NN`: a contract package owns
the SPI + canonical-ABI bindings, backends ship as sibling
packages, and a DependencyInjection package provides the
transpiler-direct-link bundle surface with per-resource
concrete classes. Both the interpreter component path and the
`--wasip2` transpiler-direct-link path render the parity
fixture end-to-end on macOS / Linux / Windows (SDL targets).
The Silk.NET/SDL backend (`WACS.WASI.GFX.Silk`) is bundled
with the CLI so `wacs run --wasi-gfx` resolves it via
`Assembly.Load`. The new `--windowed` flag moves the guest to
a worker thread and reserves the calling thread for the SDL
event pump — required on macOS where AppKit pins windowing
to the main thread.

The release includes infrastructure improvements to
`WACS.ComponentModel`, `WACS.Transpiler.Lib`, and
`WACS.WASI.Preview2.DependencyInjection` that were necessary
for the transpiler-direct-link path to work end-to-end —
they're additive and benefit any future sibling family on the
same shape.

Parity reference: fixtures at
`Spec.Test/components/fixtures/wasi-gfx-rectangle/` and
`wasi-gfx-triangle/` mirror
`wasi-gfx/wasi-gfx-runtime`'s `rectangle_frame_buffer` /
`triangle` examples on a v0-compatible world (no webgpu
inclusion). End-to-end bring-up is documented in the fixture
READMEs.

### What changed

- **`WACS.WASI.GFX` 0.1.0** (new family baseline):
  `WasiGfxConfiguration`, `WasiGfxHost` (`IBindable`),
  `WasiGfxAmbient` static-backend holder, `IBackend` SPI +
  per-resource interfaces, vendored WIT from
  `WebAssembly/wasi-gfx@03c3e95493` with the `wasi:io@0.2.0`
  reference bumped to `0.2.8` (shape-identical, aligns with
  Preview2's existing bindings), source-gen `[WitSource]`
  interfaces under `Wacs.WASI.GFX.{GraphicsContext,
  FrameBuffer, Surface}`, hand-written canonical-ABI
  `WitBindings.cs` covering all three v0 WIT packages for
  the interpreter component path. Cross-package
  `wasi:io/poll.pollable` references resolve to
  `Wacs.WASI.Preview2.Io.IPollable` via the new
  `WitHostPackageNamespaceMap` source-gen property.
- **`WACS.WASI.GFX.Silk` 0.1.0** (new sibling): Silk.NET/SDL
  backend with main-thread marshaling for window creation +
  blit, per-event-type pollables for resize/frame/pointer/
  key events backed by Preview2's `ResourceContext`, SDL
  scancode → uievents-code mapping. `SilkGfxBackend.
  InitializeOnMainThread` runs on the CLI's main thread
  pre-Task.Run to close a race that would otherwise let
  SDL_CreateWindow land on the worker. `WasiGfxSilkBindable`
  auto-wires Preview2 alongside wasi-gfx so a single
  `--wasi-gfx` flag satisfies every import the surface emits.
- **`WACS.WASI.GFX.DependencyInjection` 0.1.0** (new
  sibling): `Microsoft.Extensions.DependencyInjection`
  extensions plus per-resource direct-link impls (`Context`,
  `AbstractBuffer`, `Surface`, `Device`, `Buffer`) following
  the SourceGen-resource convention (parameterless ctor +
  `Create()`). Delegates to the `IBackend` SPI via
  `WasiGfxAmbient`. `WasiPreview2GfxBundle` composite
  exposes both Preview2 and wasi-gfx `[WitSource]` interfaces
  through one CLR object.
- **`WACS.ComponentModel` 0.3.5 → 0.3.6** (point — additive
  infrastructure): source-gen `WitHostPackageNamespaceMap`
  property lets a downstream package remap cross-package
  type references to an upstream's canonical CLR namespace
  (used by wasi-gfx to point `wasi:io` refs at Preview2's
  emitted types). Source-gen now emits WIT `static func` as
  C# `static` default-interface methods with a body that
  dispatches through the new
  `Wacs.ComponentModel.Runtime.HostInterfaceRuntime.
  InvokeStaticFactoryReflective` helper to the impl class's
  `{Name}Static` factory. Closes the latent same-shape bug
  in Preview2's `Fields.from-list` /
  `OutgoingRequest.respond`. No public API breakages.
- **`WACS.Transpiler.Lib` 0.8.15 → 0.8.16** (point —
  additive canonical-ABI coverage):
  `CanonicalSlotCount` + `EmitLiftForType` now handle
  `IResource[]` parameters (`list<borrow<R>>` /
  `list<own<R>>`) by emitting a loop that walks the packed
  handle array and calls `resources.GetResource` per
  element. This was the blocking gap for
  `wasi:io/poll.poll`; the fix is general and unblocks any
  future WIT importing list-of-resources.
  `HostPackageResolver.FindWasiPreview2Bundle` auto-discovers
  `WasiPreview2GfxBundle` alongside the existing NN
  composite and plain Preview2 fallbacks.
- **`WACS.WASI.Preview2.DependencyInjection` 0.2.0 → 0.2.1**
  (point — additive composite plumbing):
  `WasiPreview2RuntimeScope` now reflectively calls
  `AddWasiGfx` + `AddWasiPreview2GfxBundle` when the gfx
  DI assembly is loaded, mirroring the existing
  `ReflectivelyAddWasiNN` hook. `ResolveBundle` prefers the
  gfx composite when it's registered.
- **`WACS.Cli` 1.7.7 → 1.7.8** (point — flag wiring +
  resolver list):
  `RunOptions` gains `--wasi-gfx` and `--windowed`
  (introduced in 1.7.7, now bundled into the resolver
  hostpackages list so the transpiler can find the wasi-gfx
  impl classes the same way it finds wasi-nn's).
  `ExecuteWindowed` reflectively pre-constructs the Silk
  backend on the main thread before Task.Run, then drives
  `RunMainLoop` on the calling thread. No CLI surface
  changes beyond the new flags.

### Known v1 follow-ups (not blocking v0)

- `wasi:webgpu` host bindings.
- `SDL_QUIT` (window close button) + macOS Quit-menu should
  cancel the wasm guest cleanly; v0 requires Ctrl-C.
- The `RayLib` backend.

Family tag baseline: `WACS-WASI-GFX-v0.1.0` (no prior tags
for this family — verified via `git tag --sort=-creatordate
| grep WACS-WASI-GFX` returning empty before the release).

## WACS.WASI.NN.OpenVino 0.2.1 / WACS.Cli 1.7.6 — fix compile_model empty-Properties throw

`OpenVINO.CSharp.API` 2025.4.0's `Core.compile_model(model, device,
Dictionary)` dispatches on `properties.Count` with branches for 0,
1, 2, 3 — but the `Count == 0` arm is unreachable: only a `null`
`Dictionary` hits the wrapper's no-properties native call, while
an empty dict falls through the if-else chain into a throw:
`"Only supports parameter quantities of 0, 1, 2, and 3."`

`OpenVinoBackend.LoadGraph` constructed `_options.Properties` as
an empty `Dictionary<string,string>` and passed it through, so
every guest that loaded an OpenVINO model without setting
`WACS_WASINN_OPENVINO_PROPERTIES` blew up at compile time.
Surfaced now on macOS arm64 because the 2026.1.0 runtime ships
brought `read_model` past the IR-version-skew gate that had been
masking it — Linux and Windows guests would hit the same throw.

Fix: coerce empty `Properties` to `null` at the two
`compile_model` call sites (primary + CPU fallback) before
dispatching to the wrapper. Reported in
`wasi-nn/WACS-GAPS.md` gap 34.

### What changed

- **`WACS.WASI.NN.OpenVino` 0.2.0 → 0.2.1** (point — bug fix):
  one-line guard in `OpenVinoBackend.cs` to substitute `null` for
  the default empty `Properties` dict.
- **`WACS.Cli` 1.7.5 → 1.7.6** (point — bundle re-pack against
  the fixed backend): no CLI surface changes.

## WACS.WASI.NN.OpenVino 0.2.0 / WACS.Cli 1.7.5 — bundle OpenVINO 2026.1.0 macOS arm64 runtime

Upstream `OpenVINO-CSharp-API` shipped 2026.1.0 native packs after
the maintainer merged the auto-update PR. `OpenVINO.runtime.macos-arm64`
is now at 2026.1.0 on NuGet (up from a stale 2024.4.0.1). Wire it
back into the backend csproj as a transitive dependency — a stock
`dotnet add package WACS.WASI.NN.OpenVino` on Apple Silicon now
restores the natives automatically, and the bundled `wacs` CLI
ships them at `tools/.../runtimes/osx-arm64/native/` so global-
tool installs get a working OpenVINO setup with zero manual
staging.

The 2026.1 native reads OpenVINO IR exported by `pip install
openvino==2024.x | 2025.x | 2026.x` cleanly, closing the
forward-incompatibility gap that PR #154's `fetch-openvino-native.sh`
script was created to bridge. With the upstream NuGet packs now
tracking releases, the BYO-runtime fetch script is no longer
needed and has been removed from the package.

The wrapper itself stays at `OpenVINO.CSharp.API` 2025.4.0; the C
ABI is stable within an OpenVINO major series so 2025.4 wrapper
P/Invokes resolve cleanly against the 2026.1 native runtime.

### What changed

- **`WACS.WASI.NN.OpenVino` 0.1.2 → 0.2.0** (minor — adds a
  ~170 MB transitive runtime dep on macOS arm64; out-of-box install
  behavior on that RID shifts from "stage natives yourself" to
  "just works"): adds `OpenVINO.runtime.macos-arm64` 2026.1.0 as
  a hard `PackageReference` with `ExcludeAssets="compile"`. README
  rewritten to demote the version-skew workaround to a section
  scoped to the still-stale non-bundled RIDs (`macos-x86_64`,
  `ubuntu.*-x86_64`).
- **`WACS.Cli` 1.7.4 → 1.7.5** (point — bundle picks up the new
  backend transitive dep): CLI nupkg grows from ~135 MB to ~181 MB,
  comfortably under the 250 MB NuGet upload limit. No CLI surface
  changes.

### Verified

- `dotnet pack Wacs.Console` produces `WACS.Cli.1.7.5.nupkg` at
  ~181 MB; `unzip -l` confirms the OpenVINO 2026.1 dylibs land at
  `tools/net9.0/any/runtimes/osx-arm64/native/`.
- `wacs run /tmp/empty.wasm --bind WACS.WASI.NN.OpenVino --verbose`
  reports `bind WACS.WASI.NN.OpenVino -> 1 binding(s)` against the
  new native runtime.

## WACS.WASI.NN.OpenVino 0.1.2 — BYO-runtime script for the 2025.x+ IR skew

The NuGet `OpenVINO.runtime.macos-arm64` (and the other non-Windows
RIDs) is **stuck at 2024.4.0.1**. IR exported by
`pip install openvino==2025.x` or 2026.x trips
`Incorrect weights in bin file!` at `Core.read_model` against
the older bundled runtime. OpenVINO IR is backward-compatible
within a major version but not forward-compatible.

Ships a `tools/fetch-openvino-native.sh` script in the
`WACS.WASI.NN.OpenVino` package that:

- Resolves Intel's official tarball URL for a given version
  (default 2025.4.1) + RID (default `osx-arm64`) via
  `storage.openvinotoolkit.org`'s `filetree.json` index.
- Downloads, extracts, and stages the dylibs into the wacs
  install location's `runtimes/<rid>/native/` directory.
- Auto-detects the wacs install path via `command -v wacs` +
  the standard `dotnet tool list -g` layout. `--dest <path>`
  override for embedders who installed wacs in a non-standard
  location.

The C# wrapper layer P/Invokes against `libopenvino_c` by
soname, so Intel's newer native is binary-compatible at the
wrapper boundary without bumping `OpenVINO.CSharp.API`. Live-
verified that 2025.4.1 dylibs swap cleanly: `wacs run --bind
WACS.WASI.NN.OpenVino` constructs `new Core()` against the
newer native and reports `1 binding(s)`.

Currently supports `osx-arm64` only — Linux + Windows users
typically have a workable NuGet runtime already (Linux 2024.4
covers most exports + Windows is at 2026.0.0 on NuGet). Open
an issue if you need the script extended.

Includes a version-skew note in the package README pointing
at the script + Intel's release archive.

## WACS family release — auto-discovery for wasi-nn backends

Replaces the hand-written per-backend `BuildXxxConfigureCallback`
chain in `WasiPreview2RuntimeScope` (~620 LOC across five
hardcoded backends) with a single auto-discovery loop. Adding a
new wasi-nn backend no longer requires editing the DI scope —
the new package implements `IWasiNNBackendRegistration` on its
bindable and gets picked up the next time the bundle scope is
built. Same shape this PR's refactor would have demanded for any
seventh, eighth, … backend.

### What changed

- **`WACS.WASI.NN` 0.3.5 → 0.4.0** (minor — new public API):
  added `IWasiNNBackendRegistration` interface with one method,
  `ConfigureConfiguration(WasiNNConfiguration config)`.
- **All six backend packages — `WACS.WASI.NN.OnnxRuntime` 0.3.1
  → 0.3.2, `LlamaSharp` 0.2.3 → 0.2.4, `TorchSharp` 0.1.2 →
  0.1.3, `OnnxRuntimeGenAI` 0.1.4 → 0.1.5, `OpenVino` 0.1.0 →
  0.1.1, `MLNet` 0.2.3 → 0.2.4** (point — additive interface
  implementation): each `WasiNN<Backend>Bindable` adds
  `IWasiNNBackendRegistration` and refactors its ctor body into
  the new method. Env-driven model-registry scanning
  (`WACS_WASINN_GGUF_DIR` / `_TORCH_DIR` / `_GENAI_DIR`) lives
  in the bindable as before — the DI scope no longer
  duplicates the scan logic.
- **`WACS.WASI.Preview2.DependencyInjection` 0.1.9 → 0.2.0**
  (minor — behavior shift): `BuildOnnxConfigureCallback`,
  `BuildLlamaSharpConfigureCallback`,
  `BuildTorchSharpConfigureCallback`,
  `BuildOnnxGenAIConfigureCallback`,
  `BuildOpenVinoConfigureCallback`,
  `BuildGenAIRegistryFromEnv`,
  `BuildTorchScriptRegistryFromEnv`,
  `BuildGgufRegistryFromEnv` and `CombineCallbacks` all gone;
  one `BuildAutoDiscoveredCallback` walks the AppDomain for
  `Wacs.WASI.NN.*` assemblies, finds public types implementing
  `IWasiNNBackendRegistration`, and chains their
  `ConfigureConfiguration` calls into the AddWasiNN configure
  delegate. The DI scope dropped from ~945 lines to ~480.
- **`WACS.WASI.NN.MLNet`** picks up auto-wire under `--wasip2`
  for free (it was previously missing from the hardcoded
  callback list — a latent gap nobody had hit).
- **`WACS.Cli` 1.7.3 → 1.7.4** (point — rebuilt deps).

### Cost analysis (the trade-offs we picked up)

| | Before | After |
|---|---|---|
| LOC in `WasiPreview2RuntimeScope.cs` | 945 | 480 |
| Adding a new backend touches | DI scope + new builder method | Implement interface on bindable only |
| Env-scan code locations per backend | 2 (bindable ctor + DI builder) | 1 (bindable's `ConfigureConfiguration`) |
| MLNet auto-wire under `--wasip2` | Missing | Works |
| New surface area to maintain | Per-backend builders | One interface, one discovery loop |

### Tests + smoke

- `Wacs.Core.Test` suite: 488/488 (no change — this is DI wiring).
- All six backend csproj's build clean.
- Live: `wacs run --wasip2 --wasi-nn` still binds OnnxRuntime
  (the shorthand path); `wacs run --wasip2 --bind
  WACS.WASI.NN.OpenVino` binds via the new discovery — both
  report `1 binding(s)` and proceed to instantiation without
  the gap-33 `InvalidEncoding` error.

## WACS.WASI.Preview2.DependencyInjection 0.1.9 / WACS.Cli 1.7.3 — OpenVINO auto-wire under `--wasip2`

Closes the gap reported in `wasi-nn/WACS-GAPS.md` §33: under
`--wasip2 --bind Wacs.WASI.NN.OpenVino.dll`, guests calling
`graph.load(encoding=openvino)` saw `InvalidEncoding: No backend
registered for encoding OpenVINO`. Root cause was the same shape
that bit OnnxRuntimeGenAI in an earlier round: the IBindable's
`BindHostFunction` registrations get silently shadowed under
`--wasip2` by the direct-link path, the WitBindings handlers
drop, `GraphFuncsImpl.Load` reads the DI-bundle's
`WasiNNConfiguration.Backends` dict, and without an auto-wire
callback `Backends[OpenVINO]` stays empty.

Added `BuildOpenVinoConfigureCallback` to
`WasiPreview2RuntimeScope.ReflectivelyAddWasiNN` —
mirrors `BuildOnnxConfigureCallback` (the cleanest analog; both
register via the encoding-keyed `Backends` dict, no
`LoadByNameBackend` plumbing). The callback joins the existing
four-backend combine chain and registers an
`OpenVinoBackend()` against `GraphEncoding.OpenVINO`. Failure
modes (assembly not loadable / type not found / `Activator`
throws) report on stderr instead of silently leaving the
`Backends` dict empty — same pattern as the four siblings.

WACS.Cli 1.7.3 picks up the rebuilt DI assembly. No user-facing
flag change.

## WACS.Cli 1.7.2 — drop OpenVINO native runtimes from bundle

The 1.7.1 nupkg failed to publish to NuGet (HTTP 413 — package
exceeds the 250 MB upload limit). Each `OpenVINO.runtime.<rid>`
pack is 150-200 MB decompressed; pinning all four primary RIDs
(`macos-arm64` / `macos-x86_64` / `win` / `ubuntu.22-x86_64`)
plus the existing ORT natives pushed the wacs nupkg past the
NuGet ceiling.

Removed all four `OpenVINO.runtime.*` package references from
`Wacs.Console.csproj`. The OpenVINO backend DLL still ships in
the wacs bundle, but the matching native runtime is the user's
responsibility — install
`OpenVINO.runtime.<rid>` for your RID separately and drop the
unpacked `runtimes/<rid>/native/` tree into the wacs install
location. Full instructions in
[`Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.OpenVino/README.md`](Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.OpenVino/README.md).

The architecture matches how ML.NET and TorchSharp backends
already ship — the WACS-native backend DLL is small and
bundled, the heavy ML framework's natives are a separate
install.

## WACS.WASI.NN.OpenVino 0.1.0 / WACS.Cli 1.7.1 — OpenVINO wasi-nn backend

New backend package for `graph-encoding.openvino`, plus a CLI
bundle update that ships the OpenVINO native runtimes alongside
the existing ONNX Runtime packs. A guest that calls
`wasi:nn/graph.load` with the `openvino` encoding now Just Works
end-to-end on the four primary RIDs (macOS arm64 / macOS x64 /
Windows x64 / Ubuntu 22 x64).

### What's new

- **`WACS.WASI.NN.OpenVino`** — backend package implementing
  `IBackend` against [`OpenVINO.CSharp.API`](https://www.nuget.org/packages/OpenVINO.CSharp.API)
  2025.4.0. Handles the two-builder shape (XML + bin) that the
  wasi-nn spec was originally designed around. Element-type
  coverage: `fp16`, `fp32`, `fp64`, `bf16`, `u8`, `i32`, `i64` —
  every primitive in the WIT enum.
- **`OpenVinoBackendOptions` / `OpenVinoDevice`** — typed knobs
  for device pinning (`CPU` / `GPU` / `NPU` / `AUTO`), a
  property-bag forwarded into OpenVINO's `compile_model`, and
  CPU-fallback gating. Env-var aliases:
  `WACS_WASINN_OPENVINO_DEVICE`,
  `WACS_WASINN_OPENVINO_PROPERTIES`,
  `WACS_WASINN_OPENVINO_FALLBACK_CPU`.
- **`WasiNNOpenVinoBindable`** — parameterless `IBindable`
  adapter for `--bind WACS.WASI.NN.OpenVino` (matches the
  per-backend convention already in use by the ONNX / MLNet /
  LlamaSharp / TorchSharp / OnnxRuntimeGenAI siblings).
- **`Wacs.Console`** bundles `WACS.WASI.NN.OpenVino` plus
  `OpenVINO.runtime.<rid>` native packs for macOS arm64
  (2024.4.0.1), macOS x86_64 (2024.4.0.1), Windows
  (2026.0.0), and Ubuntu 22 x86_64 (2024.4.0.1). NuGet's
  RID-aware restore drops the matching native binaries into
  `runtimes/<rid>/native/` in the published bundle.

### Usage

```bash
# CLI — backend ships bundled in WACS.Cli; pick it via --bind.
wacs run my.component.wasm --wasip2 --bind WACS.WASI.NN.OpenVino

# Pin a device (default AUTO):
WACS_WASINN_OPENVINO_DEVICE=cpu wacs run my.wasm ...
WACS_WASINN_OPENVINO_DEVICE=gpu wacs run my.wasm ...

# Forward arbitrary compile_model properties:
WACS_WASINN_OPENVINO_PROPERTIES=PERFORMANCE_HINT=LATENCY,INFERENCE_PRECISION_HINT=f16 \
    wacs run my.wasm ...
```

```csharp
// Embedder
using Wacs.Core.Runtime;
using Wacs.WASI.NN;
using Wacs.WASI.NN.OpenVino;
using Wacs.WASI.NN.Types;

var runtime = new WasmRuntime();
runtime.UseWasiNN(b => b.AddBackend(
    GraphEncoding.OpenVINO, new OpenVinoBackend()));
```

### Multi-builder `graph.load` shape

OpenVINO is the wasi-nn encoding the spec multi-builder shape
was originally designed around: `builders[0]` is the IR XML and
`builders[1]` is the weights `.bin`. The new backend reads them
directly into `Core.read_model(xmlBytes, weightsTensor)` with no
intermediate file I/O. Single-builder calls fall through with
empty weights — supports IRs that inline all constants into the
XML.

### Note: no separate `--wasi-nn-openvino` flag

The pattern matches the existing MLNet / LlamaSharp / TorchSharp
backends: a backend is selected via
`--bind WACS.WASI.NN.<Backend>` from the bundled assembly, not
via a flag. The shorthand `--wasi-nn` still maps to the ONNX
Runtime backend (its job for years); a single-encoding tool
keeps the routing unambiguous. Guests that need multiple
encodings simultaneously belong on the `WasiNNHost`-with-
multiple-backends programmatic path, which all packages
support.

## WACS.Cli 1.7.0 — `wacs wast2json` verb

New verb that converts a `.wast` spec-test script into the
canonical wast2json bundle: one `.json` file listing commands +
one side-car `.wasm` per referenced module. Mirrors `wabt`'s
`wast2json` output shape, so the resulting directory is
consumable by any tooling that reads the spec-test format —
notably WACS's own `Spec.Test` runner, which previously needed
the upstream tool installed.

### Usage

```bash
wacs wast2json forward.wast -o out/
# wrote out/forward.json (5 commands, 1 side-car modules)

ls out/
# forward.0.wasm  forward.json

wacs wast2json i32.wast -o out/
# wrote out/i32.json (460 commands, 86 side-car modules)
```

### Output shape

Top-level: `{ "source_filename": "...", "commands": [...] }`. Each
command carries `"line"` for traceability back to the source
position. Module-bearing commands point at a `<basename>.<n>.wasm`
side-car; assertion-with-module forms also carry `"text"` and
`"module_type"`. Action arguments and expected values use the
typed-value shape (`{"type": "i32", "value": "..."}`); floats
serialize as their unsigned IEEE-754 bit-pattern decimal so the
runner reconstructs exact NaN payloads.

### Coverage

All spec-test commands the WAST parser produces:

| .wast form | JSON output |
|---|---|
| `(module …)` | `type: "module"`, side-car `.wasm` |
| `(module binary "…")` | `type: "module"`, raw bytes |
| `(module quote "…")` | `type: "module"`, `.wat` side-car |
| `(module instance $a $b)` | `type: "module_instance"` |
| `(register "name" $id?)` | `type: "register"` |
| `(invoke …)` / `(get …)` | `type: "action"` |
| `(assert_return …)` | `type: "assert_return"` |
| `(assert_trap (invoke …) …)` | `type: "assert_trap"` |
| `(assert_trap (module …) …)` | `type: "assert_uninstantiable"` |
| `(assert_exhaustion …)` | `type: "assert_exhaustion"` |
| `(assert_invalid …)` | `type: "assert_invalid"` |
| `(assert_malformed …)` | `type: "assert_malformed"` |
| `(assert_unlinkable …)` | `type: "assert_unlinkable"` |
| `(assert_exception …)` | `type: "assert_exception"` |

## WACS 0.15.19 / WACS.Cli 1.6.7 — `wacs.trivia` custom section

Optimistic in-band carrier for WAT comments (`;;` line + `(;…;)`
block) and `(@name …)` annotations so round-tripping WAT → wasm
→ WAT keeps human-authored trivia. WACS-specific, ignored by
other engines; no spec.

### What changed

- `BinaryModuleWriter` emits a `wacs.trivia` custom section
  when `Module.Comments` or `Module.Annotations` is populated.
- `BinaryModuleParser` parses the section when
  `BinaryModuleParser.ParseTrivia = true` (off by default, like
  the other opt-in custom sections).
- `wacs inspect` flips both `ParseCustomNames` and
  `ParseTrivia` on so `--dump-wat` from a binary input recovers
  comments and names, and `--dump-wasm` from WAT serializes
  them into the section.

### Wire format

```
custom section "wacs.trivia":
  comments:    vec(entry<vec(comment)>)
  annotations: vec(entry<vec(annotation)>)
entry<T> ::= kind:u8  index:s32  instructionIndex:s32  payload:T
comment    ::= trivia_kind:u8  is_trailing:u8  text:utf8-str
annotation ::= name:utf8-str  payload:utf8-str
```

### Live behavior

```bash
$ cat /tmp/commented.wat
;; top-level header comment
(module
  ;; the boom function does the deed
  (func $boom
    i32.const 1
    drop
    unreachable))

$ wacs inspect --dump-wasm /tmp/commented.wat --output-dir /tmp/d
wrote /tmp/d/commented.wasm (135 bytes)
$ wacs inspect --dump-wat /tmp/d/commented.wasm
;; top-level header comment
(module
  (type (func))
  ;; the boom function does the deed
  (func (type 0)
    i32.const 1
    drop
    unreachable
  )
)
```

### Lossy by design

- Source line/col aren't serialized (the binary form has no
  source positions; trivia is keyed by `ModuleElementRef`
  only).
- Comment positions on re-parse are best-effort — comments
  attach to the same `ModuleElementRef` they came from, but
  the renderer doesn't try to preserve "this comment was on
  line 7 column 12" inside the WAT body.
- Trailing-vs-leading distinction is preserved (the
  `IsTrailing` flag rides along).

### Tests

`TriviaSectionRoundTripTests` (4 cases): top-level line
comment, module-level annotation, opt-in gating, and zero-
emit-when-empty. Full suite 488/488.

## WACS 0.15.18 — uninternify parse-time instruction singletons

Pairs with 0.15.17. Removes the ten process-wide singletons that
the binary / text parsers used to hand out as shared instances,
making every parsed occurrence its own `InstructionBase`.
Resolves the first-match ambiguity that 0.15.17's
`ByteOffsetWalker` was unable to disambiguate.

### What changed

- `InstI32Const` and `InstLocalGet`: deleted the static
  `ConcurrentDictionary<int, T> LookupCache` fields and the
  `.Inst` statics. `Immediate(N)` now always allocates a fresh
  instance; `Parse(reader)` does the same directly.
- Removed the `.Inst` static fields from `InstUnreachable`,
  `InstNop`, `InstReturn`, `InstDrop`, `InstRefIsNull`,
  `InstRefEq`, `InstRefAsNonNull`. Removed
  `InstSelect.InstWithoutTypes`. Constructors for
  `InstUnreachable`/`InstNop` flipped from private to public so
  the factory can allocate them. `SpecFactory` calls `new InstX()`
  for each opcode.
- Test sites updated to use `new InstX().Immediate(N)` or
  `new InstReturn()` instead of `InstX.Inst.…`.

### What didn't change

ALU / relop / test ops (`InstI32BinOp.I32Add`,
`InstI32RelOp.I32Eq`, `InstI32TestOp.I32Eqz`, …) are still
per-opcode statics referenced directly in `SpecFactory`. They
have no per-occurrence state today, so sharing is harmless. The
`StackAnalysis` queue-per-instance trick is kept in place to
give those still-shared singletons per-site info. If anyone
later attaches per-site state to ALU ops, those classes must
also be uninterned.

### Memory / runtime impact

For a typical wasm module, a few extra MB of `InstructionBase`
subclass instances at parse time. Dispatch is virtual-by-
reference and the CLR JIT de-virtualizes the same way regardless
of instance count, so no measurable runtime cost. Memory is
rooted by the `Module` and lives in Gen2 for the program
lifetime — no GC pressure.

### Tests

484/484 stable across 10 parallel runs (0.15.17 was also
stable; this confirms the uninterning didn't regress
anything). The two pre-existing `CallIndirect_dispatches_via_funcref_table`
failures in `Wacs.Compilation.Test` are unrelated and pre-date
this branch.

## WACS 0.15.17 — on-demand byte-offset walker

Replaces the parse-time `ByteOffsetInFunc` stamping (binary
parser + Pass F annotator) with a single on-demand
`Wacs.Core.Bin.ByteOffsetWalker`. The `ByteOffsetInFunc` field
on `InstructionBase` is gone; consumers (stack-trace formatter,
branch-hint join, LineMap writer) call `ByteOffsetWalker.Find`
or `Walk` when they need an offset.

### Why

The deleted field was a mutable per-occurrence value living on
instances that the rest of WACS interns process-wide
(`InstI32Const`, `InstLocalGet`, `InstDrop`, `InstNop`, …). The
last parse to stamp the singleton won, so:

- Two functions with the same `drop` got the same offset
  (whichever parsed last).
- Concurrent parses (xUnit parallel collections, embedder
  loading multiple modules) clobbered each other, producing
  flaky test failures in Pass F/G assertions.

Pass F made the surface area worse by stamping every WAT-parsed
instruction. Rather than chase down where the race manifests,
we deleted the storage altogether and compute offsets on demand
through the existing `BinaryModuleWriter` + `CountingStream`
machinery.

### What changed

- New `Wacs.Core.Bin.ByteOffsetWalker` (`Walk` for visitor-
  style traversal, `Find` for first-match lookup).
- Deleted `InstructionBase.ByteOffsetInFunc`, the parser
  stamping at `Module.ParseInstruction`, the `BinaryParseContext`
  thread-static and the `FunctionBodyStart` plumbing in
  `CodeSection`, and `ByteOffsetAnnotator` (with its
  `TextModuleParser.FinalizeModule` hook).
- `WasmStackTrace.AppendFrame` now walks the trap function's
  body to resolve the `@+0xXX` display and the LineMap lookup
  key.
- `BranchHintSection.JoinByInstruction` walks once per function
  to map offset-keyed parse data to the instance-keyed lookup.
- `TextModuleWriter.WriteFunc` (LineMap path) pre-walks the
  body once to build a `Dictionary<InstructionBase, uint>` of
  first-match offsets; `RecordInstructionSpan` looks up by
  reference.

### Remaining limitation

Interned singletons (`drop`, `i32.const N`, `local.get N`, …)
still ambiguate among occurrences — `ByteOffsetWalker.Find`
returns the first reference match. A trap at the second `drop`
in a function reports the first `drop`'s offset. Same
ambiguity as 0.15.16; resolving it needs a per-occurrence
handle in `WasmStackFrame` (body-index or linker-PC). Deferred.

### Tests

Removed `ByteOffsetAnnotatorTests` (now redundant — both ends
of the walker would tautologically agree). Updated
`BranchHintParsingTests.InstructionsCarryFuncRelativeByteOffset`
and `NoBranchHintsWhenSectionAbsent` to call
`ByteOffsetWalker.Find` instead of reading
`inst.ByteOffsetInFunc`. Full suite: 484/484, stable across 10
parallel runs (no flake).

## WACS 0.15.16 / WACS.Cli 1.6.6 — binary-source line bridge (Pass G)

Binary-parsed modules now resolve to source coordinates in trap
traces via a canonical re-render, closing the last Pass-C gap.
The CLI lazily builds a `LineMap` on first trap and surfaces
`(line:col)` against the re-rendered WAT, matching what WAT
direct-parse already produced via `Module.SourcePositions`.

### What changed

- `TextModuleWriter.WriteWithLineMap` now records a
  `ModuleElementKind.Instruction`-kinded `ModuleElementRef` for
  every emitted instruction, keyed by
  `(absolute funcIdx, ByteOffsetInFunc)`. Plumbed through
  `WriteInstructionSeq` / `WriteInstruction` / `WriteBlockForm` /
  `WriteIfForm`. The previous overloads (without a `LineMap`)
  remain available for the non-tracking callers.
- `WasmStackTrace.TryResolveSourceCoord` consults the LineMap
  when `Module.SourcePositions` is absent — falls back to the
  recorded span's start line / column. Resolves the
  frame's `FuncAddr` through the `Store` to its absolute
  `FuncIdx` (via `FunctionInstance.Index`) so the lookup key
  matches what the writer recorded.
- `RunHandler.BuildLineMapIfNeeded` in WACS.Cli lazily computes
  the LineMap on demand (only when `SourcePositions` is null);
  WAT-parsed modules skip the re-render. Wired into
  `PrintTrap` / `PrintUnhandledWasm` / `PrintRuntimeException`.

### Live behavior

```bash
# Binary input (no source) — Pass G surfaces (line:col) from
# a canonical re-render.
$ wacs run trap.wasm --invoke _start
WASM stack trace:
  at $_.boom|0 (unreachable @+0x3) (6:1)
  at $_._start

# Same WAT direct — resolves via Module.SourcePositions
# (unchanged behavior).
$ wacs run trap.wat --invoke _start
WASM stack trace:
  at $_.$boom (unreachable @+0x3) (5:5)
  at $_._start
```

### Known limitation

`ByteOffsetInFunc` is a mutable field on `InstructionBase`, but
several instruction classes (`InstI32Const`, `InstLocalGet`,
`InstDrop`, `InstNop`, …) intern process-wide. The latest parse
to stamp the singleton wins, so the displayed `@+0xXX` can be
the wrong function's offset when the same opcode/value shows up
in multiple functions or modules. The mechanism is sound in
serial production use; tests for Pass F/G use looser assertions
that survive the race. The architectural fix (per-occurrence
offset side-table + body-index in `WasmStackFrame`) is deferred.

### Tests

`BinarySourceLineBridgeTests` (4 cases) covering the LineMap
recording for binary-parsed modules and WAT round-trips, plus
`WasmStackTraceTests.FormatVerbose_BinaryParsedModule_ResolvesViaLineMap`
exercising the full trap → re-render → format path. Full suite:
487/487.

## WACS 0.15.15 — WAT byte-offset annotation (Pass F)

WAT-parsed modules now carry `InstructionBase.ByteOffsetInFunc`
stamps that match what the binary parser records natively, so
stack-trace formatting surfaces `@+0xXX` coordinates regardless
of which parser produced the module.

### What changed

- New `Wacs.Core.Bin.ByteOffsetAnnotator` walks every defined
  function body through a counting `Stream` wrapper that
  swallows writes and only tracks position, then stamps each
  instruction's body-relative byte offset using the existing
  `BinaryModuleWriter` encoder machinery. Block-shaped
  instructions (`block`/`loop`/`if`/`try_table`) recurse into
  their inner sequences.
- `TextModuleParser.FinalizeModule` invokes the annotator
  immediately after `SynthesizeNameSection`. One extra body-
  shaped walk per module load — no per-instruction allocations
  — amortized over every subsequent trap-format call.
- Closes the byte-offset half of the binary-vs-text parity gap
  called out in 0.15.14. The source-line bridge for binary
  inputs (Pass G) remains the last item.

### Tests

`ByteOffsetAnnotatorTests` (4 cases): flat-body, WAT/binary
agreement on flat and on nested `block`/`loop` shapes, and
`if`/`else` branch coverage. Full suite: 483/483.

## WACS 0.15.14 / WACS.Cli 1.6.5 — name-section round-trip parity (Gap A closed)

WAT-parsed and binary-parsed modules now produce stack traces
with the same level of function-name detail. Previously, dumping
WAT to binary via `wacs inspect --dump-wasm` and re-running the
binary lost every `$name` — traces fell back to `func@<addr>`.

### What changed

- `TextModuleParser.FinalizeModule` synthesizes
  `Module.Names.FunctionNames` from each `Function.Id` at the end
  of parse. Conventionally the `name` custom section stores names
  without the leading `$` (wabt / wasm-tools convention) so that's
  what we emit. Imports surface their entity name as the label.
  Modules with no `$names` leave `Module.Names` null — lazy-
  allocation invariant preserved.
- `BinaryModuleWriter` already serialized `Module.Names` when
  populated (Wacs.Core 0.14.0); this commit just makes the WAT
  path actually populate it.
- `WACS.Cli` sets `BinaryModuleParser.ParseCustomNames = true`
  in `RunHandler` so binary inputs surface function names in
  traces without the caller needing to flip the flag manually.

### Live behavior

Same module, two parse paths — names survive both:

```
;; WAT direct
WASM stack trace:
  at $_.$inner (unreachable @+0x0) (3:5)
  at $_.$outer (resume@4)
  at $_._start

;; WAT → BinaryModuleWriter → BinaryModuleParser
WASM stack trace:
  at $_.inner|0 (unreachable @+0x0)
  at $_.outer|1 (resume@4)
  at $_._start
```

The binary path's `inner|0` suffix is the existing `PatchNames`
convention (`{name}|{idx}` to keep duplicates unique). Cosmetic
asymmetry with the direct-WAT label format but the same
information is there.

### Remaining gaps

- **Source line numbers for binary-parsed modules** (Gap B):
  binary inputs still don't carry `(line:col)` in the verbose
  trace because there's no source. Bridging via on-demand
  `TextModuleWriter.WriteWithLineMap` is a follow-up.
- **Byte offsets on WAT-parsed modules**: `ByteOffsetInFunc`
  stays zero for WAT inputs. The verbose trace shows `(line:col)`
  so this is less visible, but cheap-form output omits a useful
  coordinate. Could be filled in by a parse-time accounting pass.

### Tests

`NameSectionRoundTripTests` (3 cases): WAT-parse synthesizes the
name map, name-free modules stay lazy, full
WAT → binary → re-parse loop preserves function ids.
479/479 Wacs.Core.Test tests pass.

## WACS 0.15.13 / WACS.Cli 1.6.4 — WasmRuntimeException family + CLI handler (Stack-trace Pass E)

Final pass of the stack-trace arc. Extends the in-dispatch-loop
enrichment to `WasmRuntimeException` (host-API misuse, OpStack
underflow, internal invariants violated during execution) and
wires the CLI to print the WASM trace section for it too.

### Public API additions (Wacs.Core 0.15.13)

- `Wacs.Core.Runtime.Exceptions.WasmRuntimeException.WasmFrames`
  property + `(message, frames)` overload constructor.
- The dispatch loop in `WasmRuntime.ProcessThreadAsync` gains a
  third parallel catch filter (alongside `TrapException` and
  `UnhandledWasmException`):
  ```csharp
  catch (WasmRuntimeException re) when (re.WasmFrames == null)
  {
      re.WasmFrames = ctx.SnapshotCallStack(inst);
      throw;
  }
  ```
  Runtime exceptions thrown from inside an instruction's
  `Execute` are retroactively populated. Exceptions thrown
  *outside* the dispatch loop (instantiation, host binding,
  type-system construction errors) leave `WasmFrames` null —
  no call stack exists at those sites.

### CLI wiring (WACS.Cli 1.6.4)

- New `RunHandler.PrintRuntimeException(exc, module, runtime)`
  helper, mirroring `PrintTrap` / `PrintUnhandledWasm`.
- A fourth catch filter at each of the four invoker sites:
  ```csharp
  catch (System.Exception any) when (TryUnwrap<WasmRuntimeException>(any, out var rexc))
  ```
  Prints the WASM trace section when frames are available;
  falls through to the .NET trace when they aren't.

### Arc summary

Five passes:
- **B** (0.15.7): `Module.SourcePositions` — memoize WAT (line, col, offset) per instruction.
- **A** (0.15.8): `WasmStackFrame` + `ExecContext.SnapshotCallStack` + exception fields.
- **C** (0.15.9 / Cli 1.6.2): `WasmStackTrace.Format` + `FormatVerbose`; CLI wiring.
- **C-followups** (0.15.10–0.15.11 / Cli 1.6.3):
  unwrap `TargetInvocationException`; WAT `$name` → `FunctionInstance`.
- **D** (0.15.12): dispatch-loop auto-enrichment;
  `ComputePointerPath` removal; `ErrorFormattingTests`.
- **E** (0.15.13 / Cli 1.6.4): `WasmRuntimeException` enrichment;
  CLI handler.

End-to-end behavior: any trap, unhandled exception, or runtime
fault thrown from inside an instruction's `Execute` produces a
two-section error output — the .NET stack trace followed by a
"WASM stack trace:" section showing function names, throwing
mnemonic + byte offset, and source `(line:col)` when available.

Toward the debugger-support goal: every runtime fault now
carries enough metadata to identify its source location and
the call chain that led there. Next steps will likely be
runtime-instrumentation oriented (breakpoints, single-step,
local-variable inspection) rather than diagnostic.

476/476 Wacs.Core.Test tests pass.

## WACS 0.15.12 — universal trap enrichment + ComputePointerPath removal (Stack-trace Pass D)

Every trap that escapes the interpreter's dispatch loop now carries
a `WasmFrames` snapshot, regardless of whether the originating
throw site was migrated to the snapshot constructor. This is the
broad-coverage equivalent of migrating each of the ~170 trap
throw sites manually — without touching any of them.

### How it works

`WasmRuntime.ProcessThreadAsync` wraps each `inst.Execute(ctx)` /
`inst.ExecuteAsync(ctx)` dispatch step in a try/catch whose
filter fires only when the escaping exception lacks `WasmFrames`:

```csharp
try { /* execute */ }
catch (TrapException te) when (te.WasmFrames == null)
{
    te.WasmFrames = ctx.SnapshotCallStack(inst);
    throw;
}
```

.NET's "zero-cost exception handling" model means the wrap pays
nothing on the happy path — the hot interpreter loop is
unchanged in steady state. The `inst` reference is exactly the
instruction that was about to execute when the trap fired, so
the top frame's `Instruction` is always the source-level
throwing op (no migration needed at each throw site).

`UnhandledWasmException` gets the same treatment in parallel
catch filters.

### Cleanup

- `ExecContext.ComputePointerPath()` deleted. It was a stub
  returning an empty list (entire implementation commented out),
  used by 8 sites in `WasmRuntimeExecution.cs` that wrapped trap
  messages with an "empty path" suffix — all dead code.
- The 8 callers in `WasmRuntimeExecution.cs` removed. The
  `--calculate-lines` CLI option still parses; source-line
  enrichment now flows through `WasmStackTrace.FormatVerbose`
  (Pass C).
- `TrapException.WasmFrames` and `UnhandledWasmException.WasmFrames`
  setters became `internal` so the dispatch loop can enrich
  in-place. Public API surface unchanged (the getter behavior
  is identical).

### Tests

`ErrorFormattingTests` (6 cases):
- `unreachable` — migrated-constructor path still works.
- `i32.div_s` divide-by-zero — non-migrated throw site,
  auto-enriched by the dispatch loop.
- `i32.load` out-of-bounds memory — same.
- Call chain — three-frame snapshot with the trap site's
  instruction at the top and resume PCs on caller frames.
- `FormatVerbose` source-line resolution.
- Function-label `$name` from the WAT parser.

476/476 Wacs.Core.Test tests pass.

### Live behavior across error kinds

```
;; before Pass D — only migrated sites had frames:
TrapException: Cannot divide by zero
   at Wacs.Core.Instructions.Numeric.InstI32BinOp.ExecuteI32DivS(...)
   …

;; after Pass D — every trap is enriched:
WASM stack trace:
  at $_.div_by_zero (i32.div_s @+0x0) (5:5)
```

### What's still ahead

Pass E: same enrichment treatment for `WasmRuntimeException`
(host-API misuse — typically thrown before the dispatch loop
starts, so a different mechanism applies).

## WACS 0.15.11 — propagate WAT `$name` to FunctionInstance for stack-trace labels

Cosmetic but useful: non-exported functions now appear in WASM
stack traces by their WAT-declared `$name` rather than
`func@<addr>`. Closes the most visible polish gap on the Pass C
output.

### Wiring

- `TextModuleParser.ParseFuncForm` writes the parsed `$id` onto
  `Module.Function.Id` (previously only the parallel
  `ctx.Funcs.Declare(name)` index table got it).
- `WasmRuntimeInstantiation` propagates `func.Id` onto the
  `FunctionInstance.Id` during the allocate-functions step.
  Export names still take precedence (they're written later in
  the instantiation flow) — they're the function's public
  identity, the parsed name is a fallback for non-exported
  helpers.

### Live behavior

```
;; Before this commit:
WASM stack trace:
  at func@47 (unreachable @+0x0) (3:5)
  at func@48 (resume@4)
  at $_._start

;; After:
WASM stack trace:
  at $_.$inner (unreachable @+0x0) (3:5)
  at $_.$outer (resume@4)
  at $_._start
```

### Tests

`TextModuleWriterPartialTests.WriteFunction_StackAnnotated_AddsStackComments`
updated to expect `(;$b;)` (parsed name) instead of `(;0;)`
(numeric index) on the header id comment. 470/470 Wacs.Core.Test
tests pass.

## WACS 0.15.10 / WACS.Cli 1.6.3 — CLI sees the WASM trace (Pass C follow-up)

Two bugs were preventing the freshly-landed `WasmStackTrace`
output from actually reaching the user. Both surfaced when we
ran a trapping WAT through the CLI for the first time.

### Bug 1: trap path swallowed by TargetInvocationException

The CLI's `CreateInvokerAction` path calls into the parsed module
via `Delegate.DynamicInvoke`, which wraps any thrown exception
inside `System.Reflection.TargetInvocationException`. The
existing `catch (TrapException)` blocks in `RunHandler` never
fired — they were dead code. Every trap bubbled up to `Main`'s
last-resort handler and printed only the .NET stack trace.

Fix: `RunHandler.TryUnwrap<T>(exc)` walks the InnerException chain;
the four trap catch sites are now exception-filter-based:
`catch (System.Exception any) when (TryUnwrap<TrapException>(any, out var exc))`.
A parallel filter catches `UnhandledWasmException` and routes
through a new `PrintUnhandledWasm` helper. Existing
`SignalException` handling is unchanged.

### Bug 2: unhandled-throw snapshot taken after the unwind

`InstThrowRef.ExecuteInstruction` searches for a matching catch
clause by walking the control stack and calling
`context.FunctionReturn()` to pop unmatched frames. The previous
code snapshotted the call chain *after* the search loop, when
the stack was already empty.

Fix: snapshot before the search. Plus, thread a `throwingInstruction`
parameter through `InstThrow.Execute` → `InstThrowRef.ExecuteInstruction`
so the top frame's `Instruction` is the source-level `throw`
(not the throw_ref dispatcher).

### Live behavior

A WAT that traps now produces (CLI output):

```
Wacs.Core.Runtime.Types.TrapException: unreachable
   at Wacs.Core.Instructions.InstUnreachable.Execute(...)
   …

WASM stack trace:
  at func@47 (unreachable @+0x0) (3:5)
  at func@48 (resume@4)
  at $_._start
```

A WAT that throws an unhandled exception produces:

```
Wacs.Core.Runtime.Exceptions.UnhandledWasmException: Unhandled exception ExnInstance
   at Wacs.Core.Instructions.InstThrowRef.ExecuteInstruction(...)
   …

WASM stack trace:
  at $_._start (throw @+0x0) (5:5)
```

470/470 Wacs.Core.Test tests pass.

## WACS 0.15.9 / WACS.Cli 1.6.2 — WasmStackTrace formatter + CLI wiring (Stack-trace Pass C)

Third piece of the stack-trace arc. Formats captured
`WasmStackFrame` chains into human-readable traces (cheap and
verbose forms) and wires the CLI's trap-catch sites to surface
them alongside the .NET stack trace.

### Public API additions (Wacs.Core 0.15.9)

- `Wacs.Core.Runtime.Exceptions.WasmStackTrace`:
  - `Format(frames, module, store) → string` — cheap form. Single
    line, `←`-separated frames. Uses `FunctionInstance.Id` for
    function labels, `InstructionBase.ByteOffsetInFunc` + mnemonic
    for the throwing instruction.
  - `FormatVerbose(frames, module, store, lineMap?) → string` —
    multi-line form. Adds `(line:col)` from
    `Module.SourcePositions` when available (Pass B, WAT-parsed).
    Optional `LineMap` parameter is plumbed for future re-render-
    based fallback on binary-parsed modules.

### CLI wiring (WACS.Cli 1.6.2)

- New `RunHandler.PrintTrap(exc, module, runtime)` helper. The
  four `catch (TrapException)` sites in `InvokeInterpreterEntry`
  route through it. Output adds:
  ```
  WASM stack trace:
    at $myfunc (unreachable @+0x4) (3:17)
  ```
  below the existing .NET trace, when the trap carries
  `WasmFrames` (currently `unreachable` and unhandled exceptions —
  Pass D batches the rest).

### Behavior

- Traps that haven't been migrated to the snapshotting constructor
  still print the unchanged .NET-only trace. CLI output is purely
  additive — existing scripts parsing the trap message keep working.

### Tests

`WasmStackTraceTests` (3 cases) covers cheap-form mnemonic + offset
output, verbose-form source-line resolution from
`Module.SourcePositions`, and the empty-frames sentinel.
470/470 Wacs.Core.Test tests pass.

### What's still ahead

Pass D: bulk-migrate the ~100 trap throw sites to use the
snapshotting constructor; delete the dead `ComputePointerPath`;
add `ErrorFormattingTests` asserting on full trace structure.
Pass E: same enrichment for `WasmRuntimeException`.

## WACS 0.15.8 — WasmStackFrame + exception enrichment foundations (Stack-trace Pass A)

Second piece of the stack-trace arc. Adds the data shape and the
ExecContext snapshot API for capturing the WASM-side call stack at
trap / uncaught-exception time. Exception types gain an optional
`WasmFrames` field; two proof-of-concept throw sites are migrated
to populate it. The bulk migration of remaining throw sites
batches with Pass D.

### Public API additions

- `Wacs.Core.Runtime.WasmStackFrame { uint FuncAddr, InstructionBase? Instruction, int ResumeContinuationAddress }`
  — immutable struct, single snapshot frame.
- `ExecContext.SnapshotCallStack(InstructionBase? topInstruction = null)`
  → `WasmStackFrame[]`. Walks the polymorphic `_callStack` top-
  first; the top frame's `Instruction` is the supplied throwing
  instruction; caller frames carry their resume PC for lazy
  resolution later. O(call-depth) and allocation-free beyond the
  returned array.
- `TrapException(message, WasmStackFrame[] wasmFrames)` — overload
  carrying the snapshot. Existing `TrapException(message)`
  constructor unchanged.
- `OutOfBoundsTableAccessException` gains the same overload.
- `UnhandledWasmException` gains the same overload.
- `TrapException.WasmFrames` / `UnhandledWasmException.WasmFrames`
  read-only properties — null when the throw site didn't snapshot
  (most still don't; migration is incremental).

### Behavior

- `unreachable` (`InstUnreachable.Execute`) now throws with the
  snapshot.
- The unhandled-wasm-exception path in `InstThrowRef.ExecuteInstruction`
  snapshots before throwing (call stack has already unwound, so the
  snapshot is empty — that's still useful information: "uncaught at
  the entry point").
- Every other trap site remains on the legacy bare-string
  constructor and leaves `WasmFrames` null. Pass D migrates them in
  bulk along with the formatting tests.

### What's still ahead

- **Pass C**: `WasmStackTrace.Format(module, verbose)` consumes
  `WasmFrames` and emits human-readable traces. Cheap form uses
  `InstructionBase.ByteOffsetInFunc` + `Op.GetMnemonic`. Verbose
  form resolves source coords via `Module.SourcePositions` (Pass B,
  WAT-parsed) or lazy `TextModuleWriter.WriteWithLineMap` (Pass 6,
  binary-parsed).
- **Pass D**: bulk-migrate the remaining ~100 trap throw sites to
  pass `(message, ctx.SnapshotCallStack(this))`. Delete the
  stubbed `ExecContext.ComputePointerPath`. Add `ErrorFormattingTests`
  asserting on full trace structure.
- **Pass E**: same enrichment for `WasmRuntimeException` (host-API
  misuse) and siblings.

### Ultimate target

Debugger support — every runtime fault carries enough metadata to
surface the WAT source line plus the call chain that led there,
on demand and without execution-time bloat.

### Tests

`WasmStackFrameTests` (3 cases) covers live `unreachable` trap
capture, legacy-constructor null-frames invariant, and the empty-
stack snapshot path. 467/467 Wacs.Core.Test tests pass.

## WACS 0.15.7 — memoize WAT source positions per instruction (Stack-trace Pass B)

First substantive piece of the stack-trace plumbing arc. The text
parser now records the originating `(Line, Column, SourceOffset)`
for every instruction it constructs, so later trap / exception
formatting can resolve a runtime frame to a WAT source location
without re-rendering the module. This is the WAT-side counterpart
to the existing `InstructionBase.ByteOffsetInFunc` (binary side).

### Public API additions

- `Wacs.Core.SourcePos { Line, Column, SourceOffset }` — immutable
  struct, 1-based line/column. `SourceOffset` is the byte index into
  the original WAT source string.
- `Module.SourcePositions` — lazy
  `Dictionary<InstructionBase, SourcePos>?`. Stays null on binary-
  parsed modules and on WAT-parsed modules with no function bodies.
- `Module.RecordSourcePosition(inst, pos)` — public append helper.

### Wiring

- `TextModuleParser.ParseInstrList` is the choke point — every
  instruction emitted (flat or folded) gets stamped with the
  outermost form's `(Line, Column, Start)` via the existing
  `SExpr.Token`. Zero extra parse work: the lexer already produced
  the position. For folded forms that emit multiple instructions
  (operands + operator), all share the outer form's position —
  accurate on a single line, a fair approximation otherwise.
- Hot interpreter loop is untouched. Side-table lookup happens only
  at trap-format time.

### What's still ahead

- **Pass A**: `WasmStackFrame` + `TrapException` / `UnhandledWasmException`
  enrichment. Throw sites pass `(this, ctx)`; the exception captures
  a frame chain at throw time.
- **Pass C**: `WasmStackTrace.Format(module, verbose)` — cheap form
  via `ByteOffsetInFunc` + mnemonic; verbose form resolves source
  via `SourcePositions` (WAT) or `LineMap` (binary, lazy re-render).
- **Pass D**: delete the stubbed `ExecContext.ComputePointerPath`;
  add `ErrorFormattingTests`.
- **Pass E**: same enrichment for the `WasmRuntimeException` family.

### Ultimate target

Debugger support — WACS modules carry enough metadata to surface
WAT source lines (or via `LineMap`, generated text source lines)
on every runtime fault, plus the call chain that led there.

### Tests

`SourcePositionTests` (5 cases) covers flat-instruction line
capture, folded-form outer-position propagation, source-offset
accuracy (slices back to "i32.const"), binary-parse null-safety,
and the empty-body laziness invariant. 464/464 Wacs.Core.Test
tests pass.

## WACS 0.15.6 — text round-trip equivalence tests + canonical fixed-point fix (Pass 7)

Final pass of the text-emission consolidation arc. Adds the test
matrix that proves the seven-pass refactor produces the round-trip
equivalence the user asked for, and fixes one canonicalization bug
the new tests surfaced.

### Bug fixed

- `InstF32Const` / `InstF64Const` `.RenderText` always appended a
  diagnostic `(;= 2;)` style block-comment showing the source-form
  value of the hex-formatted constant. That trailing comment
  doubled on each round-trip — the comment-capture path (Pass 3)
  treats it as user trivia and re-emits, while the next render
  produces the same diagnostic again. `RenderInstruction` in
  `TextModuleWriter` now strips the diagnostic tail for canonical-
  mode emission so the round-trip is a stable fixed point.

### Tests

`TextRoundTripFixedPointTests` (6 cases):
- Canonical fixed-point round-trip on three fixture .wat files
  (`engine/binding.wat`, `engine/tailcalls.wat`,
  `Wacs.Bench/fib.wat`) — parse → write → re-parse → re-write
  produces byte-identical output.
- Folded ↔ flat parity: the same module rendered in both styles
  re-parses to a Module with the same section shape.
- Comment survival across a round-trip (line comments at module
  and section level).
- Multi-section structural round-trip (types / funcs / memory /
  table / elem / data all parse-write-parse with matching counts).

459/459 Wacs.Core.Test tests pass.

### Arc summary

`TextModuleWriter` is now the sole text emitter, with three styles
(canonical / stack-annotated / folded), full section coverage
(types incl. GC, imports, funcs, tables, memories, tags, globals,
exports, start, elems, datas), comment + `(@…)` annotation round-
trip, partial-render entry points for tooling, and a bidirectional
line map for IDE / source-map use. `ModuleRenderer` is gone.

Known limitations after Pass 7:
- Recursive folding inside `block` / `loop` / `if` bodies is still
  flat in folded mode (operands inside blocks don't collapse).
- Instruction-level comments / annotations attach to module-level
  rather than to the precise instruction; section-level placement
  is exact. Function-body trivia round-trip lands in a follow-up.

## WACS 0.15.5 — bidirectional LineMap from Write (Pass 6)

Adds a debug / tooling-friendly entry point that returns both the
rendered WAT and a line-and-column map between module elements and
the text they produced.

### Public API additions

- `Wacs.Core.Text.LineMap` — bidirectional map.
  - `LineMap.Span` — immutable struct: `(StartLine, StartCol, EndLine, EndCol)`,
    1-based.
  - `ByElement(ModuleElementRef) → Span?` — direct lookup.
  - `ByLine(int line) → ModuleElementRef?` — finds the innermost
    element whose span brackets the line.
  - `All` — `IReadOnlyDictionary<ModuleElementRef, Span>` of every
    recorded entry.
- `Wacs.Core.Text.LineCountingTextWriter` — `TextWriter` wrapper
  that tracks the running `(Line, Column)` cursor.
- `TextModuleWriter.WriteWithLineMap(module, options?)` —
  returns `(string text, LineMap map)`. The standard
  `Write(module)` path is unchanged and still returns a bare
  `string`.

### Recorded sections

Every top-level section element gets a span: types, imports,
functions, tables, memories, globals, tags, exports, start, elems,
datas. Instruction-level spans inside function bodies are
deferred — a later pass can refine when there's a use case
(source-map generation from WAT to bytecode for debugger support).

### Tests

`LineMapTests` (4 cases) covers per-section span recording,
`ByLine` line→element lookup, backward-compatible `Write(module)`
return type, and `All` enumeration. 453/453 Wacs.Core.Test tests
pass.

## WACS 0.15.4 — folded / S-expression instruction style (Pass 5)

Lights up `TextWriterStyle.Folded`. Function bodies that previously
emitted as flat stack-machine lines now collapse into S-expression
form when the option is enabled — round-trips of WAT originally
written in folded shape can now return to that style.

### Public API additions

- `Wacs.Core.OpCodes.OpcodeArity.TryGet(inst, out consume, out produce)`
  — per-opcode arity table for the "pure" subset (numeric consts,
  unary / binary ops, locals, globals, ref-leaves, loads / stores,
  drop, memory.size / memory.grow). Anything outside this table
  (call, branch, block, etc.) returns false and forces a chain
  break.

### Folder behavior

- Single linear pass with a stack of rendered operand fragments.
  Leaves push; operators pop their operands and wrap as
  `(op (operand1) (operand2) …)`; effectful ops (produce=0) emit
  the folded form as a stand-alone line.
- Chain breakers — branches, returns, calls, throws, block shapes,
  unreachable — flush the pending stack as flat lines and emit
  the instruction flat. The folder cannot safely treat their
  result as a pure operand without per-call signature lookup.
- Block bodies emit flat in this pass. Recursive folding inside
  `block` / `loop` / `if` lands in a follow-up.
- `TextWriterStyle.Canonical` (the default) is unchanged — no
  on-the-wire difference for callers of
  `TextModuleWriter.Write(module)`.

### Tests

`FoldedEmissionTests` (7 cases) covers binary add nesting, local-set
operand pull, two-operand stores, block fallback, call chain-break,
canonical-mode invariance, and folded-output reparse. 449/449
Wacs.Core.Test tests pass.

## WACS 0.15.3 — fill TextModuleWriter structural gaps (Pass 4)

Replaces the Phase-2 "round-trip not supported" placeholders with
real WAT emission for every remaining structural section. After this
pass, every section the parser produces re-emits to text that re-
parses to a structurally-equivalent module — no more silent drops
into comment stubs.

### What's now emitted

- **Tags**: `(tag (type N))` for defined tags. Imported tags surface
  through the existing import-section path.
- **GC types**: full struct / array / sub / rec coverage.
  - Bare single-sub final final types: `(type (struct (field T) …))`,
    `(type (array (mut T)))`, `(type (func …))`.
  - `(sub …)` and `(sub final …)` with super-type indices.
  - `(rec (type …) (type …) …)` wrappers for multi-sub recursion
    groups and non-final / supered subs.
  - Field types render `(mut T)` for mutable + bare `T` for
    immutable; packed storage types (`i8` / `i16`) survive.
- **Element segments**: full canonical form for all eight wire
  shapes:
  - Active default table: `(elem (offset (i32.const 0)) func 0 1)`
    (func-shortcut) or `(elem (offset …) reftype (item …) …)`.
  - Active explicit table: `(elem (table N) (offset …) …)`.
  - Passive: `(elem func 0 1)` / `(elem reftype (item …) …)`.
  - Declarative: `(elem declare func 0)` /
    `(elem declare reftype (item …) …)`.
  - Func-shortcut auto-selected when every initializer is a single
    `ref.func` and the segment type is FuncRef-family.
- **Data segments**: full canonical form:
  - Active default mem 0: `(data (offset (i32.const 0)) "bytes")`.
  - Active explicit: `(data (memory N) (offset …) "bytes")`.
  - Passive: `(data "bytes")`.
  - Byte payloads escape through the existing
    `BytesEncoder.EncodeToWatString` helper, so non-ASCII / control
    bytes round-trip via `\XX`.

### Tests

`StructuralEmissionTests` (9 cases) covers tags, all three data
modes, the func-shortcut and declarative element forms, struct /
array / rec GC types, plus a full-module multi-section round-trip
(parse → write → re-parse with matching section counts).
442/442 Wacs.Core.Test tests pass.

### What's still ahead

Pass 5 lights up the folded / S-expression instruction style (the
per-opcode arity table + folder). Pass 6 returns a bidirectional
`LineMap` from `Write`. Pass 7 is the broader test arc (full
round-trip equivalence across the spec testsuite, folded↔flat
parity, comment / annotation survival inside function bodies).

## WACS 0.15.2 — TextModuleParser captures comments + annotations; writer re-emits (Pass 3)

Closes the round-trip loop for trivia from Pass 2. The parser now
populates `Module.Comments` and `Module.Annotations` as it walks the
module body, and `TextModuleWriter` re-emits each entry at its
attached position.

### Parser changes

- `SExprParser.ParseWithTrivia(source)` — new entry point returning
  the lexer, the s-expression tree, and the side-band trivia list.
- `TextModuleParser.ParseWat` switches to the trivia-aware entry
  point. `ParseModule` gains an overload taking lexer + trivia.
- `TextParseContext` tracks the trivia cursor and exposes
  `DrainTriviaBefore(pos, owner)` / `DrainRemainingTrivia()` so the
  section walk can stream comments to the right owner in O(N).
- Top-of-file comments (before `(module`) attach to module-level;
  comments between section forms attach to the *following* form;
  trailing comments past the last section attach to module-level
  with `IsTrailing = true`.
- `(@name payload…)` annotations at module level are captured onto
  `Module.Annotations` instead of being silently dropped. The
  payload is the raw text between the name atom and the closing
  paren, so the writer can re-emit it verbatim.

### Writer changes

- `TextModuleWriter.WriteTo` now calls `EmitLeading(...)` before
  each section element and `EmitAnnotations(...)` after the
  `(module` opener for module-level annotations.
- Indentation matches the section the comment precedes — a comment
  attached to a function emits at the function's indent.

### Behavior

- Comment-free / annotation-free modules still leave the side-
  tables `null` (Pass 2's lazy-allocation invariant holds).
- Nothing else changes: existing `(module …)` outputs are
  byte-identical for sources without trivia.

### Tests

- `CommentAnnotationRoundTripTests` (4 cases) covers line / block
  comment survival, `(@custom)` annotation capture + re-emit, and
  the no-trivia laziness assertion. 433/433 Wacs.Core.Test tests
  pass.

### What's still ahead

Pass 4 fills the remaining structural gaps in the writer
(element/data segments full, GC struct/array bodies, tags,
name-section idents). Pass 5 lights up the folded /
S-expression style with the per-opcode arity table. Pass 6 returns
a bidirectional `LineMap` from `Write`. Pass 7 is the test arc
exercising parse → write → re-parse → re-write text-identity
across the spec fixtures.

## WACS 0.15.1 — trivia-aware lexer + Module annotation side-tables (Pass 2)

Foundation for round-tripping comments and `(@…)` annotations. Adds
the data shape and the lexer hook; no parser or writer changes yet
(those land in Pass 3).

### Public API additions

- `Wacs.Core.Text.TriviaKind { LineComment, BlockComment }` and
  `Wacs.Core.Text.TriviaToken` (immutable struct: kind, source
  span, line, column).
- `Lexer.TokenizeWithTrivia()` — returns the same tokens as
  `Tokenize()` plus a side-band `List<TriviaToken>` of every
  `;;` and `(;…;)` comment seen, in source order with their
  original delimiters intact.
- `Lexer.SliceTrivia(TriviaToken) → string` — materializes the
  raw comment text.
- `Wacs.Core.ModuleElementRef` (immutable struct: `Kind`, `Index`,
  `InstructionIndex`) + `ModuleElementKind` enum — stable handle
  used to key the new side-tables.
- `Wacs.Core.WatAnnotation` (name + payload + position) and
  `Wacs.Core.WatComment` (kind + text + position + is-trailing).
- `Module.Annotations` and `Module.Comments` —
  `Dictionary<ModuleElementRef, List<…>>?`, lazy-allocated so
  comment-free modules pay no extra memory. `AddAnnotation` /
  `AddComment` helpers append + lazy-init.

### Behavior

- The default `Tokenize()` path is unchanged — `SkipTrivia` still
  drops comments, the existing parser sees the same token stream.
  The new `_capturedTrivia` field is `null` outside
  `TokenizeWithTrivia`, so the hot path stays allocation-free.
- Module instances loaded from a comment-free source leave the
  side-tables null (no allocation). Pass 3 wires the parser to
  populate them.

### Tests

`LexerTriviaTests` (6 cases) covers default-strip, line-comment
capture, block-comment capture, nested-block depth tracking,
EOF-terminated line comments, and lazy-allocation behavior of
the module side-tables. 429/429 Wacs.Core.Test tests pass.

## WACS 0.15.0 / WACS.Cli 1.6.1 — consolidate text emission onto TextModuleWriter; retire ModuleRenderer (Pass 1)

First of seven planned passes to bring `TextModuleWriter` to full
fidelity round-trip and retire the older `ModuleRenderer`. This pass
is the structural consolidation: every text-emission caller now goes
through `TextModuleWriter`, the legacy renderer is gone, and the
debug / stack-annotated rendering it used to do is reachable as an
option on the unified writer.

### Public API changes

- **Deleted:** `Wacs.Core.ModuleRenderer` (entire static class —
  `RenderWatToStream`, `RenderFunctionWat`, `GetFuncIdx`,
  `ChopFunctionId`, `Indent2Space`).
- **Added:** `Wacs.Core.Text.TextWriterOptions` +
  `Wacs.Core.Text.TextWriterStyle { Canonical, StackAnnotated, Folded }`.
- **Added:** `TextModuleWriter.Write(module, options)`,
  `WriteTo(writer, module, options)`, and partial-render
  `WriteFunction(module, funcIdx, indent, options)`.
- **Added:** `TextModuleWriter.Indent2Space` (the two-space module-
  body indent — replaces `ModuleRenderer.Indent2Space`).
- **Added:** `Wacs.Core.Text.TextDiagnostics.GetFuncIdx(path)` /
  `ChopFunctionId(path)` — validation-path parsers split out of the
  old renderer.
- **Moved:** `Module.CalculateLine(...)` (validation-path → WAT-line
  cache) — same method, same partial class, file relocated to
  `Modules/ModuleValidationLines.cs`.

### Behavior

- `TextWriterStyle.Canonical` (the default) emits the same parser-
  friendly flat WAT that `TextModuleWriter.Write(module)` produced
  before — no on-the-wire change for existing callers.
- `TextWriterStyle.StackAnnotated` reproduces what
  `ModuleRenderer.RenderFunctionWat(module, idx, "", true)` did: the
  per-function `(;N;)` id comment, `;; label = @N` block markers,
  and left-margin stack-state side comments. Routes through the
  existing `Function.RenderText` + `StackRenderer` machinery.
- `TextWriterStyle.Folded` is a placeholder for Pass 5; it currently
  falls back to canonical.

### Migrations

- `Wacs.Console/Verbs/RunHandler.cs` (the validation-error reporter)
  now calls `TextModuleWriter.WriteFunction(module, idx, "",
  TextWriterOptions.StackAnnotated)` and
  `TextDiagnostics.{GetFuncIdx,ChopFunctionId}`. The on-disk
  `<funcid>.part.wat` artifact is unchanged.
- `Wacs.Core/Modules/Sections/FunctionSection.cs` references the
  indent constant via `TextModuleWriter.Indent2Space`.

### What's next

Pass 2 adds `Module.Annotations` + `Module.Comments` plus lexer
trivia-token support. Pass 3 wires the parser to attach trivia and
the writer to re-emit it. Pass 4 fills the remaining structural
gaps (element / data / GC types / tags / name-section idents). Pass
5 lights up the folded / S-expression style. Pass 6 returns a
bidirectional LineMap from `Write`. Pass 7 is the test arc.

## WACS 0.14.0 — `Wacs.Core.Bin.BinaryModuleWriter`: round-trip binary serializer

Inverse of `BinaryModuleParser`, symmetric with the existing
`Wacs.Core.Text.TextModuleWriter`. A `Module` parsed from a wasm
binary now writes back to a byte-identical canonical form on the
second write — and after one parse/write/parse cycle the bytes
stabilize, so the writer is the binary inverse of the parser modulo
non-structural details the parser already drops (custom-section
ordering, etc.).

### Design

`InstructionBase` gains a virtual `RenderBinary(BinaryWriter)` that's
the inverse of `Parse(BinaryReader)`. Default is a no-op (covers
opcodes with no immediates like `add` / `drop` / `end`); each
immediate-bearing subclass overrides it to emit the same operands
in the same order it would have read them. The pattern mirrors the
existing `RenderText` virtual that the text writer leans on. Per-
instruction encoding stays next to the instruction class, which
keeps the dispatch open to extension when new opcodes land.

`BinaryModuleWriter` composes those overrides with section-level
encoders for every spec section in the canonical order (type / import
/ function / table / memory / tag / global / export / start / element /
data-count / code / data) plus the structured custom sections (`name`,
`metadata.code.branch_hint`) when their parser-side capture flag was
set and the module carries the parsed structure.

### Coverage

Round-trip stabilizes after one write across 21 fixtures spanning
every wasm proposal WACS supports:

- **Core / multi-value / sign-extensions / saturated-float-to-int**:
  `binding`, `tailcalls`, `fib`, `HelloWorld`
- **Feature exercisers** (Feature.Detect/generated-wasm): `bulk-memory`,
  `extended-const`, `multi-memory`, `multi-value`, `mutable-globals`,
  `reference-types`, `saturated-float-to-int`, `sign-extensions`,
  `SIMD`, `relaxed-SIMD`, `tail-call`, `typed-function-references`,
  `GC`, `exceptions-final`, `threads`, `memory64`, `js-string-builtins`

Every section variant gets exercised end-to-end:

- Type section: function / struct / array composites; rec-group with
  multi-sub forms; sub / sub-final with super-type vectors
- Element section: all 8 flag variants (active / passive / declarative ×
  funcref-elemkind-shortcut / reftype-expr-vector)
- Data section: active-default-mem / active-explicit-mem / passive
- Code section: compressed-locals run-length grouping; block / loop /
  if-else / try-table inner sequences emitted recursively with their
  terminator instructions intact
- Table types: legacy reftype+limits and GC-extended `0x40 0x00`
  init-expr form
- Limits / memarg / global flags: all bit variants including
  shared / thread-local / 64-bit address / multi-memory flag
- Heap types: abstract single-byte tokens and concrete s33 indices
  for `ref.null`, `ref.cast`, `ref.test`, `br_on_cast`, `br_on_cast_fail`

### Files

- `Wacs.Core/Utilities/BinaryWriterExtension.cs` — LEB128 (u32/u64/s32/s64/s33),
  F32/F64, UTF-8 string, vector, and length-prefixed-section helpers
- `Wacs.Core/Types/Defs/ValTypeWriter.cs` — single-byte / `0x63`-`0x64` /
  s33-index dispatch matching `ValTypeParser`
- `Wacs.Core/BinaryFormat/TypeWriters.cs` — `Limits`, `GlobalType`,
  `MemoryType`, `TagType`, `TableType`, `FunctionType`, `ResultType`,
  `FieldType`, `CompositeType`, `SubType`, `RecursiveType`, `Expression`,
  `InstructionSequence`, opcode-prefix encoder
- `Wacs.Core/BinaryFormat/BinaryModuleWriter.cs` — `Write(Module)` /
  `WriteTo(Stream, Module)`; section dispatch
- `Wacs.Core/BinaryFormat/CustomSectionEncoders.cs` — `name` subsections
  (10 kinds) and `metadata.code.branch_hint`, sorted ascending for
  canonical output
- `Wacs.Core.Test/BinaryModuleWriterTests.cs` — round-trip idempotence
  across 21 fixtures
- 22 instruction files + `MemArg.cs` + `InstructionBase.cs` — virtual
  + ~75 overrides

### Folder note

Source files live in `Wacs.Core/BinaryFormat/` rather than `Wacs.Core/Bin/`
because the .NET SDK's default `<DefaultItemExcludes>` excludes `bin/**`
(it's where the build output lands). The C# namespace is `Wacs.Core.Bin`
as designed; only the on-disk folder name differs.

## WACS.Cli 1.6.0 — `wacs inspect --dump-wasm`: binary output

Lights up the inverse direction of `--dump-wat`. The `inspect` verb's
input parser already auto-detects WAT vs binary, so the new flag makes
both round-trips bidirectional from the CLI:

```
wacs inspect foo.wasm --dump-wat  --output-dir out/   # binary → text
wacs inspect foo.wat  --dump-wasm --output-dir out/   # text   → binary
wacs inspect foo.wasm --dump-wasm --output-dir out/   # binary → canonical binary
```

Without `--output-dir`, raw bytes stream through stdout via
`OpenStandardOutput()` — bypassing the console's text-encoding wrapper
so the output stays byte-clean for shell piping (e.g.
`wacs inspect foo.wat --dump-wasm | sha256sum`).

The output uses `Wacs.Core.Bin.BinaryModuleWriter` (Wacs.Core 0.14.0),
so the canonical form is byte-identical across repeat passes. Verified
end-to-end on Feature.Detect fixtures — `multi-value.wasm → wat → wasm`
reproduces the canonical wasm byte-for-byte. Coverage is bounded by
the upstream `TextModuleWriter`: features where the text writer emits
comment placeholders (notably GC type bodies, elem / data segments)
still round-trip on the binary side but lose information through WAT.

## WACS.WASI.NN 0.3.5 — WITX retArea wire format: payload at offset 0, errno via return value

PR #142 (0.3.4) aligned the `set_input` / `compute` arg count + errno
convention to the bytecodealliance `wasi-nn 0.6` ABI, but missed a
deeper retArea-layout mismatch in the four data-returning calls
(`load`, `load_by_name`, `init_execution_context`, `get_output`).

WACS was writing `errno @ retArea+0, payload @ retArea+4` and always
returning `0` from the function. The 0.6 crate's generated FFI shim
expects exactly the opposite: errno is the function's `i32` return
value, and `retArea` is a buffer sized for the payload alone — the
crate reads the payload from `retArea+0` via `ptr::read`.

End-to-end consequence: the model loaded host-side, but the `Graph`
handle returned to the guest was always 0 (the errno value sitting at
retArea+0). Every subsequent call cascaded:

- `load_by_name(name)` → returns `Graph { handle: 0 }`
- `init_execution_context(graph=0)` → `InvalidArgument` (handle 0 is
  the null sentinel in the WACS ResourceTable)
- `set_input(ctx=junk, ...)` → `InvalidArgument` (ctx lookup miss)

Now corrected. All four retArea-using calls write the payload at
`retArea+0` and use the function's `i32` return value as the errno:

| Call | Payload at retArea+0 | Errno |
|---|---|---|
| `load` | graph handle (i32) | return value |
| `load_by_name` | graph handle (i32) | return value |
| `init_execution_context` | graph-execution-context handle (i32) | return value |
| `get_output` | written-byte count (i32) | return value |
| `set_input` | — | return value (PR #142) |
| `compute` | — | return value (PR #142) |

`WriteWitxOk` / `WriteWitxErr` helpers deleted (no callers).

### Verification

Live witx guest (`wasi-nn-llm-witx`, built against `wasi-nn = "0.6"`,
running through `wacs run --bind WACS.WASI.NN.LlamaSharp.dll`) now
produces real LLM output on Qwen2.5 0.5B Instruct Q4_K_M GGUF —
`load_by_name` → `init_execution_context` → `set_input` →
`compute` → `get_output` all return errno=0, generated tokens stream
back through the guest's REPL.

`Wacs.WASI.NN.Test` suite: 21/21 pass.

## WACS.WASI.NN family — cascade bump to advertise the 0.3.4 WITX-ABI fix

Repackages all six WASI.NN sibling backends so their `.nuspec` declares the
fixed `WACS.WASI.NN >= 0.3.4` dependency floor. The previous release tag
(`WACS-WASI-NN-v0.3.5` → `WACS.WASI.NN 0.3.4`) shipped the WITX-ABI fix on
the top-level package but skip-dup'd every sibling, so each sibling's
live manifest still advertised a pre-0.3.4 floor. Consumers were
silently protected by NuGet's range-resolution picking the latest 0.3.4,
but anyone pinning a sibling exactly would have stayed on the stale
floor. Point-bumping each sibling makes the cascade explicit.

| Sibling                            | Before | After |
|------------------------------------|-------:|------:|
| `WACS.WASI.NN.DependencyInjection` |  0.2.2 | 0.2.3 |
| `WACS.WASI.NN.LlamaSharp`          |  0.2.2 | 0.2.3 |
| `WACS.WASI.NN.MLNet`               |  0.2.2 | 0.2.3 |
| `WACS.WASI.NN.OnnxRuntime`         |  0.3.0 | 0.3.1 |
| `WACS.WASI.NN.OnnxRuntimeGenAI`    |  0.1.3 | 0.1.4 |
| `WACS.WASI.NN.TorchSharp`          |  0.1.1 | 0.1.2 |

No code changes — each sibling re-packs with the new
`WACS.WASI.NN 0.3.4` `ProjectReference` already on `main` and inherits
the WITX 0.6 ABI fix transparently. See the prior changelog entry for
the actual `set_input` / `compute` signature change.

## WACS.WASI.NN 0.3.4 — align WITX `set_input` / `compute` with bytecodealliance wasi-nn 0.6 ABI

The legacy `wasi_ephemeral_nn` core-module binding in `WitxBindings.cs`
matched a stale interpretation of the WITX where every result lifted
through a `retArea` pointer. Real bytecodealliance `wasi-nn` 0.6
guests (`wasi-nn` crate 0.6 on crates.io) ship a different wire shape
for the two void-Ok calls: the errno collapses to the function's i32
return value, no retArea pointer.

Two signature changes against the previously-bound WACS shape:

| Function    | Args before                                      | Args now (0.6)                | Errno encoding       |
|-------------|--------------------------------------------------|-------------------------------|----------------------|
| `set_input` | `(ctx, idx, dimsPtr, dimsLen, type, dataPtr, dataLen, retArea) → i32` | `(ctx, idx, tensorPtr) → i32` | i32 return value     |
| `compute`   | `(ctx, retArea) → i32`                           | `(ctx) → i32`                 | i32 return value     |

`set_input`'s `tensorPtr` points at a 20-byte packed record matching
the upstream `wasi-nn` crate's `Tensor<'t>` `#[repr(C)]` layout:

```
offset 0:  dims_ptr   i32  (*const usize — usize = u32 on wasm32)
offset 4:  dims_len   i32  (slice len)
offset 8:  type       u8   (TensorType enum, low byte of i32 cell)
offset 9:  padding    3    (to 4-byte align)
offset 12: data_ptr   i32  (*const u8)
offset 16: data_len   i32  (slice len)
```

The four data-returning calls (`load`, `load_by_name`,
`init_execution_context`, `get_output`) keep their `retArea`
convention — they carry actual payload, so the crate continues to
write `(errno, T)` into the caller-supplied buffer. Only the two
void-Ok calls move to direct-i32-errno.

### Verification

`wasm-objdump` on the compiled `wasi-nn = "0.6"` guest's import section
confirms the wire signatures:

```
- func[0] sig=0 (i32, i32, i32) -> i32   <- wasi_ephemeral_nn.set_input
- func[1] sig=0 (i32, i32, i32) -> i32   <- wasi_ephemeral_nn.load_by_name
- func[2] sig=1 (i32, i32)      -> i32   <- wasi_ephemeral_nn.init_execution_context
- func[3] sig=5 (i32)           -> i32   <- wasi_ephemeral_nn.compute
- func[4] sig=6 (i32, i32, i32, i32, i32) -> i32   <- wasi_ephemeral_nn.get_output
```

All five match the bound signatures after this change. `Wacs.WASI.NN.Test`
suite passes (18/18); the existing `DualAbiBindingsTests` covers
name-level binding registration so the rename-free signature change
doesn't perturb the dual-ABI manifest.

WasmEdge's downstream fork (`wasmedge-wasi-nn` 0.7+) adds a GGML
encoding variant and a 2-arg `compute(ctx, options)` shape —
incompatible with the bytecodealliance ABI we now target. Cross-fork
support is a separate task if/when WasmEdge interop becomes a goal.

## Documentation — unified wasi-nn usage guide

New canonical entry point at [`docs/WASI_NN_USAGE.md`](docs/WASI_NN_USAGE.md):

- Backend matrix with capability + verification status across every shipped sibling
- CLI invocation reference: `--wasi-nn` shorthand, `--bind <path>`, `-d <preopen>`, `--native-memory`, engine choice
- Per-backend environment-variable cheat sheet (one table each for `OnnxRuntime` / `LlamaSharp` / `TorchSharp` / `OnnxRuntimeGenAI` / `MLNet`)
- Programmatic embedding for both engines (interpreter `runtime.UseWasiNN(...)` and transpiler DI-scope path)
- Worked examples for each backend (ONNX SLM, GGUF chat, TorchScript inference, GenAI generative, ONNX + GenAI composed)
- Diagnostics (`WACS_DIAG_MEMORY`) + troubleshooting matrix mapping symptoms to fixes

Per-backend READMEs gain a pointer to the unified guide so embedders landing
on a NuGet page reach the full picture in one click.

No package version bumps — README cross-links ride along with the next
legitimate release of each backend.

## WACS.Cli 1.5.26 / WACS.WASI.NN.OnnxRuntimeGenAI 0.1.3 — opt-in EP selection for the GenAI backend

Mirrors the EP-selection surface that ships on `WACS.WASI.NN.OnnxRuntime`
(0.3.0). After the gaps 32+33 fix, GenAI loaded under `Config.ClearProviders`
and ran CPU-only. This change adds an opt-in path to swap in CoreML / CUDA
/ DirectML / ROCm.

### Empirical: why CPU stays the default

Direct probe through `Microsoft.ML.OnnxRuntimeGenAI` on osx-arm64 against
`gemma-3-270m-it-genai`:

| EP | Load | "What is 2+2?" | "Capital of France?" |
|----|------|----------------|----------------------|
| CPU (cleared) | 927ms | 128ms / 8 tok | 84ms / 8 tok |
| CoreML | 4517ms | 427ms / 8 tok | 270ms / 8 tok |

Both EPs produce identical, correct output. CoreML is **3-5× slower** for
this 270M-param model — kernel-compile + Metal-command-buffer setup
dominates the actual compute. CoreML's win typically kicks in at 1B+
params, so auto-promotion as the default would regress the common SLM
case. Symmetric with `OnnxBackend`'s opt-in posture (the regular ORT
backend also defaults to CPU after PR #132).

### What landed

- **`OnnxGenAIExecutionProvider`** (new enum) — `Auto`, `Cpu`, `CoreML`,
  `Cuda`, `DirectML`, `Rocm`. Parallel to
  `Wacs.WASI.NN.OnnxRuntime.OnnxExecutionProvider`.
- **`OnnxGenAIBackendOptions.ExecutionProvider`** (new property, default
  `Cpu`) — plus per-EP device IDs (`CudaDeviceId`, `DirectMLDeviceId`,
  `RocmDeviceId`) and `FallbackToCpu` (default `true`).
- **`OnnxGenAIBackendOptions.FromEnvironment()`** — reads
  `WACS_WASINN_GENAI_EP` (case-insensitive: `auto` / `cpu` / `coreml` /
  `cuda` / `dml` / `directml` / `rocm`) plus
  `WACS_WASINN_GENAI_{CUDA,DML,ROCM}_DEVICE`.
- **`OnnxGenAIBackend.LoadGraphByName`** — after the existing
  `Config.ClearProviders` call, appends the selected provider via
  `Config.AppendProvider` (with optional device-id via
  `Config.SetProviderOption`). `Auto` resolves at session-construction
  time per OS. On EP-append failure with `FallbackToCpu=true` (the
  default), strips the partial provider state and falls through to CPU.
- **End-to-end verified** against `gemma-3-270m-it-genai`:
  - default (no env var): `>>> 2 + 2 = 4` ✓
  - `WACS_WASINN_GENAI_EP=coreml`: `>>> 2 + 2 = 4` ✓
  - `WACS_WASINN_GENAI_EP=auto`: `>>> 2 + 2 = 4` ✓

### Versions

- `WACS.Cli` 1.5.25 → **1.5.26** (release event)
- `WACS.WASI.NN.OnnxRuntimeGenAI` 0.1.2 → **0.1.3** (new public types:
  `OnnxGenAIExecutionProvider`; new options on
  `OnnxGenAIBackendOptions`)

### Test plan

- `Wacs.WASI.NN.OnnxRuntimeGenAI.Test` 18/18 (was 8/8 — 10 new tests
  covering env-var parsing for every EP value + the default check)
- End-to-end via `scripts/run-llm.sh` against all three modes (default
  CPU, `WACS_WASINN_GENAI_EP=coreml`, `WACS_WASINN_GENAI_EP=auto`)

## WACS.Cli 1.5.25 / WACS.WASI.NN.OnnxRuntimeGenAI 0.1.2 — first end-to-end verified GenAI run

The first empirical end-to-end pass through `WACS.WASI.NN.OnnxRuntimeGenAI`
on osx-arm64 against `gemma-3-270m-it-genai` surfaced two real gaps. Both
closed; the model now generates coherent output through the documented
`--bind` invocation.

```
$ echo -e "What is 2+2?\n/bye" | scripts/run-llm.sh
>>> 2 + 2 = 4
```

### Gap 32 — `genai_config.json` declares an unsupported provider

`Model(dir)` rejects model directories whose `genai_config.json` lists
provider names not compiled into the platform's GenAI dylib. Pre-built
HuggingFace exports often declare `nnapi` (Android NNAPI) by default —
on macOS this trips:

```
Unknown provider name 'nnapi'. Currently supported values are
'DML' / 'QNN' / 'OpenVINO' / 'SNPE' / 'XNNPACK' / 'WEBNN' / 'WebGPU'
/ 'AZURE' / 'JS' / 'VitisAI' / 'CoreML' / 'NvTensorRtRtx' / 'MIGraphX'.
```

**Fix:** `OnnxGenAIBackend.LoadGraphByName` now loads via
`Config(dir)` → `ClearProviders()` → `Model(config)` (instead of the
direct `Model(dir)` ctor). The model-declared provider list is stripped
and GenAI falls back to its platform default — CPU on macOS, matching
the regular `OnnxBackend`'s opt-in posture for hardware acceleration.
The `Config` is held for the `OnnxGenAIGraph`'s lifetime (some 0.x
GenAI builds share native state between `Model` and its source
`Config`). Embedders that want CoreML / CUDA / DirectML opt in
explicitly via a future `OnnxGenAIBackendOptions` provider knob.

### Gap 33 — instruction-tuned models need their chat template

`OnnxGenAIContext.GenerateFromPrompt` was passing the raw UTF-8 prompt
straight into `Tokenizer.Encode`. For instruction-tuned models
(Gemma 3 IT, Llama 3 Instruct, Qwen 2.5 Instruct, Phi-4 Mini Instruct,
…) the prompt needs to be wrapped in the model's turn markers
(`<start_of_turn>user\n...<end_of_turn>\n<start_of_turn>model\n` for
Gemma) for the decode loop to produce coherent text. Without it,
gemma-3-270m-it generated ~500 tokens of `2?2?2?2? just once? essen??…`
gibberish.

**Fix:** `GenerateFromPrompt` now calls
`Tokenizer.ApplyChatTemplate(null, messages, null, add_generation_prompt: true)`
before `Tokenizer.Encode`. Passing `null` for the template string makes
GenAI consume the model's bundled `chat_template.jinja`. `messages` is
a JSON array of `{role, content}` records built via
`System.Text.Json.JsonSerializer.Serialize` (an earlier attempt with
`JsonEncodedText.Encode` produced unquoted content fields → invalid
JSON → silent fallback to raw prompt → gibberish; documented inline so
the regression doesn't recur). Falls back to the raw prompt only if
`ApplyChatTemplate` throws — base (non-instruct) models work fine
without a template and we don't want to break them.

### Versions

- `WACS.Cli` 1.5.24 → **1.5.25** (release event)
- `WACS.WASI.NN.OnnxRuntimeGenAI` 0.1.1 → **0.1.2** (gap 32 +
  gap 33 fixes)

### Verified

- Direct probe through `Microsoft.ML.OnnxRuntimeGenAI` (no WACS layer)
  confirms `Config(dir).ClearProviders()` + `ApplyChatTemplate(null, …)`
  is the right sequence; replicated in `OnnxGenAIBackend.LoadGraphByName`
  and `OnnxGenAIContext.GenerateFromPrompt`.
- `wacs run --wasip2 --bind Wacs.WASI.NN.OnnxRuntimeGenAI.dll` end-to-end
  against `gemma-3-270m-it-genai`:
  - `What is 2+2?` → `2 + 2 = 4`
  - `What is the capital of France?` → `The capital of France is Paris.`
  - `Write a haiku about WebAssembly.` → coherent multi-stanza output
    (small model loops near `max_length` with greedy decoding, expected)
- `Wacs.WASI.NN.OnnxRuntimeGenAI.Test` 8/8

## WACS.Cli 1.5.24 / WACS.WASI.NN.OnnxRuntimeGenAI 0.1.1 / WACS.WASI.Preview2.DependencyInjection 0.1.8 — gap 31: `BuildOnnxGenAIConfigureCallback` auto-wires the GenAI backend via `--bind`

`wasi-nn/WACS-GAPS.md` gap 31: the new `OnnxRuntimeGenAI` backend's
`IBindable` was wiring its backend into a per-host
`WasiNNConfiguration.LoadByNameBackend`, but the DI bundle's shared
`WasiNNConfiguration` — the one `GraphFuncsImpl.LoadByName` consults
under `--engine transpiler --wasip2` — stayed null because
`WasiPreview2RuntimeScope.ReflectivelyAddWasiNN` had no auto-wire
callback for GenAI. Same shape as gap 20 (LlamaSharp's registry-split,
closed at round 14) and its TorchSharp sibling.

Symptom from `run-llm.sh -v`:

```
loading model 'gemma-3-270m-it-genai' via wasi-nn graph.load-by-name…
Error: "wasi-nn ErrorCode::NotFound: no named-model resolver
       configured and no LoadByNameBackend wired"
```

### Fix

- **`OnnxGenAIBackend.FromDirectories(IDictionary<string, string>)`**
  (new static factory) — mirror of `TorchSharpBackend.FromPaths`. Takes
  a name→directory map and constructs a backend with a lookup
  resolver. Lets the scope's reflective auto-wire emit Linq.Expression
  IL without threading a `Func<string, string?>` delegate type across
  the assembly boundary.
- **`WasiPreview2RuntimeScope.BuildOnnxGenAIConfigureCallback`** —
  mirror of `BuildTorchSharpConfigureCallback`. Detects
  `Wacs.WASI.NN.OnnxRuntimeGenAI.OnnxGenAIBackend`, builds an
  env-driven registry from `$WACS_WASINN_GENAI_DIR` (subdirectory
  scan for `genai_config.json`, matching `WasiNNOnnxGenAIBindable`'s
  shape), and wires the backend into `LoadByNameBackend` ONLY —
  leaves `Backends[ONNX]` for the regular `OnnxBackend` so
  `graph.load(bytes)` keeps its byte-tensor I/O path and
  `graph.load-by-name(<dir>)` routes to GenAI's KV-cached decode loop.
- Plumbed into the existing `CombineCallbacks` chain alongside the
  ONNX / LlamaSharp / TorchSharp callbacks.

### Co-existence with the regular OnnxBackend

Both backends register for `GraphEncoding.ONNX`. The GenAI backend
claims `LoadByNameBackend` only, leaving `Backends[ONNX]` for the raw
`OnnxBackend`. With both loaded:

- `graph.load(bytes, ONNX)` → `OnnxBackend` (byte-loaded single-shot
  tensor I/O)
- `graph.load-by-name("model-dir")` → `OnnxGenAIBackend` (KV-cached
  generative decoding)

### Versions

- `WACS.Cli` 1.5.23 → **1.5.24** (release event)
- `WACS.WASI.NN.OnnxRuntimeGenAI` 0.1.0 → **0.1.1** (new
  `FromDirectories` factory)
- `WACS.WASI.Preview2.DependencyInjection` 0.1.7 → **0.1.8** (new
  auto-wire callback)

### Test plan

- `Wacs.WASI.NN.OnnxRuntimeGenAI.Test` 8/8
- `Wacs.WASI.Preview2.Test` 189/189
- **Empirical** (user verification):
  `BACKEND_DLL=Wacs.WASI.NN.OnnxRuntimeGenAI.dll
  MODEL_NAME=gemma-3-270m-it-genai scripts/run-llm.sh` should resolve
  the model directory and route through `GraphFuncsImpl.LoadByName` →
  `OnnxGenAIBackend.LoadGraphByName` instead of returning NotFound.

## WACS.Cli 1.5.23 / WACS.Transpiler.Lib 0.8.15 / WACS 0.13.8 / WACS.WASI.NN 0.3.3 / WACS.WASI.Preview2.DependencyInjection 0.1.7 — fix unbounded leak: `[resource-drop]X` was a silent no-op under `--engine transpiler`

User-reported regression: the wasi-nn SLM REPL grew the host process to
~40 GiB before crashing with `mutex lock failed`. `WACS_DIAG_MEMORY=1`
(added below) showed +12.67 GiB managed-heap growth across 106 token
steps, matching almost exactly the sum of per-call output sizes
(~26 MiB → ~227 MiB FP32 logits per token, autoregressive, no KV cache).

### What was actually leaking

Each `ctx.compute()` returned a `list<(string, own<tensor>)>` that the
transpiler lowered into resource handles allocated through
`WasiPreview2Resources.AllocateResource(typeof(Nn.ITensor), …)`. When
the guest dropped the handles at end-of-turn the host should have
released them, but they piled up forever.

### Two-stage diagnosis (recorded in the diag.log lineage)

**Stage 1 — cross-table mismatch hypothesis.** Initial theory:
the interpreter binding `[resource-drop]tensor` drops from
`WasiNNHost.Tensors` (one resource table), but the transpiler
direct-link path allocates into
`ResourceContext.TableFor(typeof(ITensor))` (a different table). Wired
a cross-binding hook (`WasmRuntime.ExternalResourceDrop` →
`WasiPreview2Resources.FreeResource`) so the interpreter `[resource-
drop]X` handler dropped from both tables. **Result: leak rate roughly
unchanged**. The hook was right; the binding it bridged from was the
wrong layer.

**Stage 2 — the binding was never invoked.** Added split counters
(`drops[interp=X ext=Y]`) to the diag output. Result: `interp=0` across
130 turns. The WitBindings `[resource-drop]X` delegate **never fires**
under `--engine transpiler`. Traced into the transpiler:

```csharp
// ComponentMainHost.cs (before this PR):
var importsStub = ImportDispatcher.Create(importsType,
    new Dictionary<string, Func<object?[], object?>>(),  // EMPTY
    lenient: true);                                       // silent no-op
```

Every `[resource-drop]X` call from the guest hit an empty handler
dictionary, fell through `lenient: true`, and returned `default(void)`
without touching the host. The runtime's entity-binding table — where
WitBindings registered the drop handlers — was bypassed entirely. The
wasm thought drops succeeded; the host never saw them.

### The fix

`ComponentMainHost` now walks the imports interface's
`[WacsImportNames]` assembly metadata and auto-registers a handler for
every `[resource-drop]X` import:

1. For each entry whose name starts with `[resource-drop]`, split the
   module name into `(package, interface)` and the entity name into
   the bare resource name.
2. Resolve the CLR resource interface type by scanning loaded
   assemblies for one whose `[WitSource]` attribute matches
   `(Package, Interface, Item)`.
3. Register a handler that calls
   `WasiPreview2Resources.FreeResource(typeof(IX), handle)` on the
   dropped handle.

Generic across **all** host-imported resources — wasi-nn (tensor,
graph, context, error), wasi:io/streams, wasi:filesystem/types, wasi:io/poll,
and anything else the transpiler emits a stub for. No per-resource code.

The stage-1 hook (`WasmRuntime.ExternalResourceDrop`,
`WasiPreview2Resources.FreeResource`) stays — it's now defense-in-depth
for the rare case where an `IBindable` other than the transpiler
direct-link path allocates into one table and routes drops through
another.

### What the SLM REPL looks like now

193 token-generation steps, no crash. Per-turn output: 14 MiB → 332 MiB
(autoregressive prompt growth is unchanged; that's a guest decode-loop
property, not a leak). Per-turn managed-heap and RSS:

| | Before fix (turn 130) | After fix (turn 193) |
|---|---|---|
| Managed heap | **27.07 GiB** (+24.83 GiB from turn 1) | **1.03 GiB** (−1.21 GiB) |
| RSS | 6.60 GiB | 6.57 GiB |
| Gen2 collections | 12 (stalled — heap was rooted) | 164 (healthy) |
| Outcome | crashed | still running |

Managed heap is now smaller than the turn-1 baseline because Gen2
finally reclaims the LOH allocations once the resource-table roots are
released.

### What also landed in this PR

- **`WACS_DIAG_MEMORY=1` instrumentation** — per-compute stderr snapshot
  (`rss`, `managed`, `gc[g0/g1/g2]`, `in`/`out` bytes, `drops[interp,ext]`,
  duration). The diagnostic surface that found this; useful for any
  future "RSS climbs across a long-running REPL" report. Hooks both the
  interpreter (`WitBindings.compute`, `WitxBindings.compute`) and the
  direct-link path (`GraphExecutionContext.Compute`). Off by default,
  zero overhead in the negative path.
- **Stage-1 cross-table hook** — `WasmRuntime.ExternalResourceDrop` +
  `WasiPreview2Resources.FreeResource` + `WitBindings.[resource-drop]X`
  handlers wired through both tables. Architecturally correct even
  though it didn't fire for the SLM workload.

### Versions

- `WACS.Cli` 1.5.22 → **1.5.23**
- `WACS.Transpiler.Lib` 0.8.14 → **0.8.15** (`ComponentMainHost` auto-
  registers `[resource-drop]X` handlers — the actual fix)
- `WACS` (Wacs.Core) 0.13.7 → **0.13.8** (`WasmRuntime.ExternalResourceDrop`
  cross-binding hook)
- `WACS.WASI.NN` 0.3.2 → **0.3.3** (WitBindings drop handlers call the
  cross-binding hook + `WACS_DIAG_MEMORY` instrumentation)
- `WACS.WASI.Preview2.DependencyInjection` 0.1.6 → **0.1.7**
  (`WasiPreview2Resources.FreeResource` + scope wires the hook)

### Test plan

- `Wacs.WASI.NN.Test` 21/21
- `Wacs.WASI.NN.OnnxRuntime.Test` 10/10
- `Wacs.WASI.Preview2.Test` 189/189
- `Wacs.Transpiler.Test` 775/776 (1 pre-existing skip)
- **Empirical**: SLM REPL ran 193 turns clean; managed heap plateaued
  near 1 GiB instead of climbing past 27 GiB.

## WACS.Cli 1.5.22 / WACS.WASI.NN.OnnxRuntimeGenAI 0.1.0 — new wasi-nn backend: OnnxRuntime-GenAI

A fifth wasi-nn backend, slotting alongside `OnnxRuntime` / `MLNet` /
`LlamaSharp` / `TorchSharp`. Wraps Microsoft's
[OnnxRuntime-GenAI](https://github.com/microsoft/onnxruntime-genai) — the
generative-LLM runtime built on top of ONNX Runtime — and surfaces it through
wasi-nn as a `load-by-name` backend for Gemma 3, Llama 3, Qwen 2.5, Phi 4,
and any other model that `onnxruntime-genai`'s `model_builder.py` can produce.

Where the plain `WACS.WASI.NN.OnnxRuntime` backend serves single-shot
tensor-in / tensor-out inference (image classification, embeddings, encoder-
only models), this backend serves the **generative** workflow: first-class
tokenizer + KV cache + sampling, all inside the host. The osx-arm64 native
dylib links directly against `CoreML.framework`, giving Metal-capable
acceleration where the underlying ORT CoreML EP supports the ops.

### What landed

- **`OnnxGenAIBackend`** — `IBackend` against
  [`Microsoft.ML.OnnxRuntimeGenAI`](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntimeGenAI)
  0.13.2 + `Microsoft.ML.OnnxRuntime` 1.26.0. `LoadGraphByName` resolves
  through an injected name→directory delegate (the bindable wires that to a
  `WACS_WASINN_GENAI_DIR` scan).
- **Two compute shapes, dispatched by named-input convention**:
  - `compute(["prompt" → utf-8 bytes])` → `["response" → utf-8 bytes]` —
    tokenize → KV-cached decode loop → detokenize. Hits GenAI's optimized
    kernels; recommended for new generative-LLM guests.
  - `compute(["input_ids" → int64])` → `["logits" → float32]` — single
    forward pass with a fresh stateless generator. Drop-in replacement for
    existing wasi-nn ONNX guests that drive their own decode loop.
- **`OnnxGenAIBackendOptions`** — `MaxLength`, `DoSample`, `Temperature`,
  `TopP`, `TopK`, `IncludePromptInResponse`. `FromEnvironment()` reads
  `WACS_WASINN_GENAI_{MAX_LENGTH,DO_SAMPLE,TEMPERATURE,TOP_P,TOP_K,INCLUDE_PROMPT}`.
- **`WasiNNOnnxGenAIBindable`** — parameterless `IBindable` for `--bind`.
  Scans `$WACS_WASINN_GENAI_DIR` first-level subdirectories for
  `genai_config.json` and registers each by directory name. Wires through
  `LoadByNameBackend` only — composes alongside the regular `OnnxBackend`
  which keeps the `Backends[ONNX]` slot for byte-loaded `graph.load`.
- **`nuget.yml` matrix** gains the new package under the existing
  `WACS-WASI-NN-v*` tag prefix.

### How model resolution works

Models ship as **directories**, not single ONNX files. Build one with the
upstream `model_builder.py` or pull a pre-built variant from Hugging Face:

```sh
huggingface-cli download onnx-community/gemma-3-270m-it-ONNX \
    --local-dir ./models/gemma-3-270m-it

export WACS_WASINN_GENAI_DIR=./models
wacs run --wasip2 --bind Wacs.WASI.NN.OnnxRuntimeGenAI.dll my.wasm
```

A guest call to `graph.load-by-name("gemma-3-270m-it")` resolves to the
`./models/gemma-3-270m-it/` directory.

### Test plan

- `Wacs.WASI.NN.OnnxRuntimeGenAI.Test` 8/8 — SPI surface, byte-load rejection,
  TPU rejection, NotFound on missing model, InvalidArgument on missing
  `genai_config.json`, options defaults, env-var passthrough,
  bindable parameterless ctor.
- Empirical end-to-end against a real GenAI Gemma 3 (or Qwen / Phi / Llama)
  is a user-driven verification step gated on having a GB-scale GenAI
  model directory in hand.

### Versions

- `WACS.WASI.NN.OnnxRuntimeGenAI` (new) — **0.1.0**
- `WACS.Cli` 1.5.21 → **1.5.22** (release event)


## WACS.Cli 1.5.22 / WACS.WASI.NN.OnnxRuntime 0.3.0 — ONNX hardware acceleration via execution-provider selection (opt-in)

`Microsoft.ML.OnnxRuntime` 1.22.0 already ships the CoreML / CUDA / DirectML / ROCm
managed API surface AND (on macOS-arm64) the CoreML EP symbol baked into the bundled
native dylib — but `OnnxBackend` defaulted to CPU-only and didn't surface any knob to
opt in. This release adds typed configuration + env-var-driven EP selection so wasi-nn
ONNX guests can enable hardware acceleration without source changes.

**Default stays CPU.** Empirically, CoreML's partition-and-fallback for generative-LLM
ops (GroupQueryAttention specifically) produces silently wrong numerical output on
Gemma 3 270M — the SLM REPL stops responding under `WACS_WASINN_ONNX_EP=auto`/`coreml`.
DirectML on Windows has comparable op-coverage uneveness. Until ORT 1.22.0 closes
those gaps, hardware acceleration is **explicit opt-in**: the parameterless
`OnnxBackend()` and the CLI's `--wasi-nn` path default to CPU unless
`WACS_WASINN_ONNX_EP` is set. Pin the EP per-model after you've verified your model
works with it (e.g., `WACS_WASINN_ONNX_EP=coreml` for image-classification / encoder-
only models where CoreML's op coverage is complete).

### What landed

- **`OnnxExecutionProvider`** (new enum) — `Auto`, `Cpu`, `CoreML`, `Cuda`, `DirectML`,
  `Rocm`. `Auto` resolves at session-construction time to the platform-best EP (CoreML
  on macOS, DirectML on Windows, CUDA / ROCm on Linux).
- **`OnnxBackendOptions`** (new typed config) — `ExecutionProvider` (default
  **`Cpu`**), per-EP device IDs, `CoreMLFlags` passthrough, `FallbackToCpu` (default
  `true`). `FromEnvironment()` reads `WACS_WASINN_ONNX_EP` (case-insensitive: `auto` /
  `cpu` / `coreml` / `cuda` / `dml` / `directml` / `rocm`) plus
  `WACS_WASINN_ONNX_{CUDA,ROCM,DML}_DEVICE` for the device index. Unset env var → CPU.
- **`OnnxBackend()`** (parameterless ctor) — now reads `OnnxBackendOptions.FromEnvironment()`.
  CPU when `WACS_WASINN_ONNX_EP` is unset (the common case), the requested EP otherwise.
  Strict mode (`FallbackToCpu = false`) propagates EP-append failures as
  `WasiNNException(ErrorCode.RuntimeError)` at `graph.load` time.
- **`OnnxBackend(OnnxBackendOptions)`** (new ctor) — explicit typed-config path for
  library embedders.
- **`OnnxBackend(Func<SessionOptions>?)`** (preserved) — full escape hatch, wins over
  the typed-options path.
- **`CoreMLFlags` env-var passthrough** — `WACS_WASINN_ONNX_COREML_FLAGS` accepts a
  comma/pipe-separated list of CoreML flag names (`MLProgram`, `UseCpuAndGpu`,
  `CpuOnly`, `ANE`, `Static`, `Subgraph`) so the **MLProgram** model format (CoreML 5+,
  much broader op coverage for transformer ops) is reachable without recompiling.
  Pair with `WACS_WASINN_ONNX_EP=coreml` to enable.
- **`Microsoft.ML.OnnxRuntime` 1.22.0 → 1.26.0** — accumulated kernel improvements on
  osx-arm64: top-level `RMSNorm` op (was contrib-only), `FusedQKRotaryEmbedding`,
  `SplitPackedQKVWithRotaryEmbeddingAndCopyKV`, broader WebGPU EP coverage in the
  underlying op-fusion pipeline. No public-API break for the surface this package
  uses. **Note**: the CoreML EP itself sees iterative improvements but partition-and-
  fallback semantics for generative-LLM ops on macOS are largely unchanged across
  1.22 → 1.26.

### Out-of-box pick

| OS                | Auto resolves to | Notes                                                                       |
|-------------------|------------------|-----------------------------------------------------------------------------|
| macOS (arm64/x64) | CoreML           | Stock `Microsoft.ML.OnnxRuntime` ships the CoreML EP symbol — no NuGet swap |
| Windows           | DirectML         | Add `Microsoft.ML.OnnxRuntime.DirectML` for full DML coverage               |
| Linux             | CUDA → ROCm      | Requires CUDA toolkit / ROCm runtime on host                                |
| Other             | CPU              |                                                                             |

Silent CPU fallback covers the "EP picked, runtime not installed" case — the user gets
inference, not a `DllNotFoundException`. To opt out of acceleration entirely:
`WACS_WASINN_ONNX_EP=cpu`. To make EP misconfigurations loud (strict mode):
`new OnnxBackend(new OnnxBackendOptions { FallbackToCpu = false })`.

### Verified

- `Wacs.WASI.NN.OnnxRuntime.Test` 26/26 (was 10/10 — 16 new tests covering env-var
  parsing, every EP enum value, the typed-options ctor null guard, a real CoreML EP
  round-trip on macOS-arm64 with the bundled native dylib, and strict-mode
  `EntryPointNotFoundException` → `WasiNNException` wrapping for unsupported EPs)
- `Wacs.WASI.NN.Test` 21/21 (orchestrator surface unchanged)
- `Wacs.Transpiler.Test` 775/776 (1 skip, pre-existing)

### Versions

- `WACS.WASI.NN.OnnxRuntime` 0.2.3 → **0.3.0** (new public types:
  `OnnxBackendOptions`, `OnnxExecutionProvider`; new `OnnxBackend(OnnxBackendOptions)`
  ctor)
- `WACS.Cli` 1.5.21 → **1.5.22** (release event)

## WACS.Cli 1.5.21 / WACS.Transpiler.Lib 0.8.14 — gap 30: `BindBackendLoadContext` for transitive-dep DllImports

Round-25 verification (`wasi-nn/WACS-GAPS.md` round 25) found that the gap-28 fix —
`NativeLibrary.SetDllImportResolver(asm, …)` keyed on the `--bind`'d assembly — only
fires for DllImports declared **inside that assembly**. Real-world backends declare
their `[DllImport]`s in a transitive NuGet (TorchSharp.dll, LLamaSharp.dll, …), not in
the bind asm itself. So the per-asm resolver was a no-op for the actual hot-path,
and the round-25 demo (`wacs run --wasip2 --bind <Wacs.WASI.NN.TorchSharp.dll>`) still
required manual native staging into `Wacs.Console`'s `runtimes/<rid>/native/` to
work — the documented one-line UX was broken.

The proper fix is a load-context-level hook: `BindingLoader.LoadAssembly` now
constructs a custom `BindBackendLoadContext : AssemblyLoadContext` whose
`LoadUnmanagedDll(name)` override fires for every P/Invoke from any assembly in the
context — bind asm, upstream NuGet wrappers, deep transitive deps. The override
defers to `AssemblyDependencyResolver.ResolveUnmanagedDllToPath` first (deps.json-
driven RID-aware lookup, the standard .NET 8 plugin pattern), then falls back to a
bind-dir `runtimes/<rid>/native/` probe (with coarser-RID + flat-bin fallbacks).
Empirically verified: `wacs run target/wasm32-wasip2/release/wasi-nn-torch.wasm
--wasip2 --bind <Wacs.WASI.NN.TorchSharp.dll>` runs the XOR MLP end-to-end with no
`DYLD_FALLBACK_LIBRARY_PATH` and no manual `runtimes/` staging.

### What landed

- **`BindingLoader.LoadAssembly`** — file-path branch now memoizes
  `path -> Assembly` through a `ConcurrentDictionary` and uses
  `BindBackendLoadContext` instead of `Assembly.LoadFrom`. Memoization ensures
  the load-then-bind double-pass in `RunHandler.PreloadBindAssemblies` +
  `ApplyBindings` returns the same `Assembly` instance both times — without it,
  a fresh `AssemblyLoadContext` per call would yield distinct `Type` identities
  and break `IBindable` matching against the host's interface.
- **`BindBackendLoadContext.Load`** — defers to the default context for
  any assembly already loaded by the host (host-shared types like `IBindable`,
  `IBackend`, `Wacs.Core` runtime types). Without this short-circuit, the deps.json
  resolver would happily return private paths for those assemblies (since
  `EnableDynamicLoading=true` bundles them) and we'd load duplicates with split
  `Type` identities.
- **`BindBackendLoadContext.LoadUnmanagedDll`** — deps.json-driven resolution
  first (handles the standard "managed library 'TorchSharp' P/Invokes
  'LibTorchSharp', which lives at `runtimes/<rid>/native/libLibTorchSharp.dylib`"
  case), then explicit probes of `<bind-dir>/runtimes/<rid>/native/` plus coarser
  RIDs plus the flat bind dir.
- **Per-asm `SetDllImportResolver`** retained as a complementary hook — still
  useful when the bind asm itself declares direct `[DllImport]`s.

### What this means for the wasi-nn family

| Backend | Encoding | `wacs run --wasip2 --bind <…>` (no env, no manual staging) |
|---|---|---|
| `Wacs.WASI.NN.OnnxRuntime` | `Onnx` | already worked (CLI bundles ORT) |
| `Wacs.WASI.NN.LlamaSharp` | `GGML` | works (LLamaSharp's own `NativeLibrary.Load` walks the LoadFrom dir's `runtimes/`) |
| `Wacs.WASI.NN.TorchSharp` | `PyTorch` | **now works post-gap-30** — same one-line invocation |
| `Wacs.WASI.NN.MLNet` | (TBD) | not exercised |

### Verified

- `Wacs.Transpiler.Test` 775/776 (1 skip)
- `Wacs.WASI.NN.TorchSharp.Test` 8/8
- `Wacs.WASI.NN.LlamaSharp.Test` 8/8 + 2 skip
- `Wacs.WASI.NN.OnnxRuntime.Test` 10/10
- `Wacs.WASI.NN.MLNet.Test` 7/7
- End-to-end XOR MLP under `--bind` produces sigmoid outputs `0.0000 / 1.0000 /
  0.9994 / 0.0000` — numerically identical to the round-24 verification, but with
  no env-var workarounds and no manual staging.

### Versions

- `WACS.Transpiler.Lib` 0.8.13 → **0.8.14** (gap-30 `BindBackendLoadContext`)
- `WACS.Cli` 1.5.20 → **1.5.21** (release event)

## WACS.Cli 1.5.20 / WACS.Transpiler.Lib 0.8.13 / WACS.WASI.NN.TorchSharp 0.1.1 / WACS.WASI.Preview2.DependencyInjection 0.1.6 — new wasi-nn backend: TorchSharp / PyTorch (+ gaps 28/29 native-lib ergonomics)

A fourth wasi-nn backend covering `graph-encoding.pytorch`. Same packaging shape as
`WACS.WASI.NN.LlamaSharp` (load-by-name first-class via env-driven directory scan; byte-
loaded fallback for smaller TorchScript modules; `EnableDynamicLoading` ships libtorch's
~1 GB of native runtimes alongside the assembly so `--bind <path>` resolves the LoadFromContext
deps locally).

### What landed

- **`Wacs.WASI.NN.TorchSharp`** (new package) — `IBackend` against
  [`TorchSharp`](https://www.nuget.org/packages/TorchSharp). Loads TorchScript modules
  via `torch.jit.load(byte[])` (byte-loaded path) or `torch.jit.load(path)` (name-keyed
  path). `Compute(...)` switches the module to `eval()` mode, dispatches inputs by
  positional indexed-name convention (`"0"`, `"1"`, …), and lifts outputs back through
  the same indexed scheme. Single-Tensor + tuple-of-Tensors + list-of-Tensors return
  shapes all unwrap to a flat indexed `NamedTensor[]`.
- **`Wacs.WASI.NN.TorchSharp.Test`** (new test project) — 8 SPI smoke tests covering
  `SupportedEncodings`, garbage-bytes → `RuntimeError`, name-registry round-trips,
  `WasiNNTorchSharpBindable` parameterless ctor.
- **`WasiPreview2RuntimeScope.BuildTorchSharpConfigureCallback`** — sibling of
  `BuildLlamaSharpConfigureCallback`. Detects the TorchSharp assembly in AppDomain
  (post-`--bind` LoadFromContext, via the round-21 fallback), instantiates
  `TorchSharpBackend.FromPaths(<env-driven-registry>)`, wires it into BOTH
  `Backends[PyTorch]` AND `LoadByNameBackend`. Combined with the ONNX + LlamaSharp
  callbacks via the existing `Delegate.Combine` chain.
- **`nuget.yml` matrix** gains `Wacs.WASI.NN.TorchSharp` under the existing
  `WACS-WASI-NN-v*` tag prefix — versioned and published with the rest of the family.
- **Docs**:
  - [`Wacs.WASI/Wacs.WASI.NN/README.md`](Wacs.WASI/Wacs.WASI.NN/README.md) backend matrix
  - [`docs/COMPONENT_CHAINING.md`](docs/COMPONENT_CHAINING.md) runtime-requirements row
  - CLI README's `--wasi-nn` flag description mentions the four-backend matrix

### Convention recap (matches LlamaSharp)

```sh
mkdir -p ./models     # drop *.pt / *.ts files in here
export WACS_WASINN_TORCH_DIR="$(pwd)/models"

TORCH=$(realpath Wacs.WASI/Wacs.WASI.NN/Wacs.WASI.NN.TorchSharp/bin/Release/net8.0/Wacs.WASI.NN.TorchSharp.dll)
wacs run my-pytorch.component.wasm --wasip2 --bind "$TORCH"
```

For GPU swap `TorchSharp-cpu` for `TorchSharp-cuda-12.1` / `-macos-x64` etc. in the
project's csproj.

### Native-library ergonomics (gap 28 — `WACS.Transpiler.Lib`)

Round-24 verification surfaced that P/Invoke from a `LoadFrom`'d
backend assembly doesn't probe the assembly's own
`runtimes/<rid>/native/` subdirectory — the `EnableDynamicLoading`
bin layout populates it correctly, but the standard P/Invoke
resolver only searches the **application's** runtimes tree, not
arbitrary loaded assemblies'. So `--bind <path-to-backend.dll>`
trapped at first DllImport on every backend with native deps
(`Unable to load shared library 'LibTorchSharp'`).

`BindingLoader.LoadAssembly` now registers a per-backend
`NativeLibrary.SetDllImportResolver` that probes the loaded
assembly's `runtimes/<rid>/native/` directory (plus a coarser-RID
fallback like `runtimes/osx/native/`, plus the assembly's own
flat dir) for DllImports issued by that assembly. ~80 LOC of
new resolver logic; idempotent across the load-then-bind double-
pass. The standard probe is preserved as a fallback (returning
`IntPtr.Zero` from the resolver hands control back).

Mirrors how .NET 8's `AssemblyDependencyResolver` is wired up
for plugin scenarios — same problem, same shape of fix.

### macOS-arm64 libomp rpath (gap 29 — `WACS.WASI.NN.TorchSharp`)

Upstream `libtorch-cpu 2.10.0`'s `libtorch_cpu.dylib` on
osx-arm64 has a hardcoded `LC_LOAD_DYLIB` entry pointing at
`/opt/homebrew/opt/libomp/lib/libomp.dylib` (a Homebrew install
path) instead of `@loader_path/libomp.dylib`. Bundled
`libomp.dylib` from the same NuGet sits next to
`libtorch_cpu.dylib`, but dyld resolves the absolute path first
and misses it on hosts without Homebrew libomp installed (most
CI machines, fresh dev installs).

`Wacs.WASI.NN.TorchSharp.csproj` gains a
`RewriteLibompRpathOnOsxArm64` MSBuild target (post-`Build`,
Unix-conditional) that runs `install_name_tool -change` to
rewrite the entry to `@loader_path/libomp.dylib`. Idempotent
(re-runs are no-ops once the entry is rewritten). Verified via
`otool -L` against the post-build dylib.

### Worked-example documentation

`Wacs.WASI.NN.TorchSharp/README.md` now covers a full XOR MLP
worked example: a `build_xor_mlp.py` training + tracing script,
the Rust guest excerpt (positional indexed-name dispatch
convention), the bare `wacs run --wasip2 --bind <TORCH>`
invocation, and the expected output. New Requirements section
documents which native-lib gaps are addressed (28, 29) and
which embedder-supplied artifacts are still required (the `.pt`
file + `WACS_WASINN_TORCH_DIR`).

### Versions

- `WACS.WASI.NN.TorchSharp` (new) — initial **0.1.0** plus
  follow-up **0.1.1** carrying the gap-29 csproj target
- `WACS.Transpiler.Lib` 0.8.12 → **0.8.13** (gap 28
  `BindingLoader` resolver hook)
- `WACS.WASI.Preview2.DependencyInjection` 0.1.5 → **0.1.6**
  (TorchSharp auto-wire extension)
- `WACS.Cli` 1.5.18 → **1.5.20** (release events for the new
  backend + ergonomics fixes)

(Untouched: `WACS.WASI.NN`, `.WASI.NN.DependencyInjection`, `.WASI.NN.OnnxRuntime`,
`.WASI.NN.LlamaSharp`, `.WASI.NN.MLNet` — adding a new sibling backend doesn't change
the family core's surface.)

## All NuGet packages — README included in published packages

Eleven packages gain a `<PackageReadmeFile>README.md</PackageReadmeFile>` entry plus a
local `README.md`. Eight of them got fresh consumer-facing READMEs; two (`WACS.WASI.NN`
and `WACS.WASI.Preview2`) already had READMEs that just needed the csproj wiring; one
(`WACS.Transpiler.Lib`) had its csproj packing the deprecated `WACS.Transpiler` CLI tool's
README — the architecture doc previously at `Wacs.Transpiler.Lib/README.md` moved to
`ARCHITECTURE.md` and a fresh embedder-focused README took its place.

Each README covers: what the package is, who should install it, a minimal install +
quick-start example, what's inside, and links to deeper docs (top-level README,
[`docs/COMPONENT_CHAINING.md`](docs/COMPONENT_CHAINING.md)). NuGet.org now renders the
package-specific README on every package's listing page.

### Versions

Patch-level bumps (README is metadata; no public-API or behavior change):

- `WACS.ComponentModel` 0.3.4 → **0.3.5**
- `WACS.ComponentModel.Bindgen.Lib` 0.1.0 → **0.1.1**
- `WACS.WASI.NN` 0.3.0 → **0.3.1**
- `WACS.WASI.NN.DependencyInjection` 0.2.1 → **0.2.2**
- `WACS.WASI.NN.OnnxRuntime` 0.2.2 → **0.2.3**
- `WACS.WASI.NN.LlamaSharp` 0.2.1 → **0.2.2**
- `WACS.WASI.NN.MLNet` 0.2.1 → **0.2.2**
- `WACS.WASI.Preview2` 0.4.0 → **0.4.1**
- `WACS.WASI.Preview2.DependencyInjection` 0.1.4 → **0.1.5**
- `WACS.WASI.Threads` 0.2.0 → **0.2.1**
- `WACS.Transpiler.Lib` 0.8.11 → **0.8.12**

(Untouched: `WACS`, `WASI.Preview1` (already had README + wiring),
`WACS.HostBindings.{Abstractions,SourceGen}` (already had READMEs + wiring),
`WACS.Cli` (already had README + wiring), `WACS.Transpiler` (deprecated; keeps its
existing deprecation-notice README).)

## WACS.Cli 1.5.18 / WACS.Transpiler.Lib 0.8.11 / WACS.WASI.NN.DependencyInjection 0.2.1 / WACS.WASI.NN.LlamaSharp 0.2.1 / WACS.WASI.NN.MLNet 0.2.1 / WACS.WASI.Preview2.DependencyInjection 0.1.4 — gaps 24 + 25 + 26 + 27: LlamaSharp / GGUF on the transpiler-direct-link path (end-to-end)

The wasi-nn LlamaSharp/GGUF harness (`guest-llm/`, Qwen2.5 0.5B
Instruct Q4_K_M) tripped `"NotFound: no named-model resolver
configured"` at the first `compute(...)` even though
`load_by_name(...)` had returned `Ok` upstream. The DI bundle's
`GraphFuncsImpl.LoadByName` only checked `NamedModelResolver` +
`Backends`, never the sibling `LoadByNameBackend` field that the
WitBindings path (`WasiNNHost.LoadGraphByNameDispatch`) uses for
backends with internal name registries.

### `GraphFuncsImpl.LoadByName` parity with `WasiNNHost`

`Wacs.WASI.NN.DependencyInjection/GraphFuncsImpl.cs` now mirrors
`WasiNNHost.LoadGraphByNameDispatch`:

```csharp
if (_config.LoadByNameBackend != null)
    return Result<...>.FromOk(new Graph(
        _config.LoadByNameBackend.LoadGraphByName(name, ExecutionTarget.CPU)));
// fall through to NamedModelResolver → bytes → backend
```

LlamaSharp's `LoadGraph(builders)` always traps
`UnsupportedOperation` (a multi-GB GGUF passed through canonical-
ABI lift would force a multi-GB host copy on every load); the
direct `LoadByNameBackend` path lets the backend resolve models
through its own registry without that round-trip. Closes gap 24
architecturally.

### LlamaSharp auto-wire in `WasiPreview2RuntimeScope`

Round-14 added `BuildOnnxConfigureCallback` to wire
`OnnxBackend` into the DI bundle's `WasiNNConfiguration` at
scope-construction time. Round-20 generalizes the pattern: a
sibling `BuildLlamaSharpConfigureCallback` detects
`Wacs.WASI.NN.LlamaSharp.LlamaSharpBackend`, builds an
env-driven registry from `WACS_WASINN_GGUF_DIR` (mirrors
`WasiNNLlamaSharpBindable`'s scan), instantiates the backend
via `FromPaths(registry)`, and wires it into BOTH
`Backends[GGML]` AND `LoadByNameBackend`. The two callbacks
combine via `Delegate.Combine` into one multicast configure that
runs against the same options instance.

`CombineCallbacks` is generic — adding a new wasi-nn backend
auto-wire requires one new `BuildXxxConfigureCallback` plus a
line in `ReflectivelyAddWasiNN`'s combine call.

### `--bind` auto-pulls DI siblings for `Wacs.WASI.NN.*`

Sub-gap 24a: when `--bind` resolves an assembly whose identity
starts with `Wacs.WASI.NN.` (LlamaSharp / MLNet / future
backends), `RunHandler.ResolveHostPackages` now adds
`Wacs.WASI.NN` + `Wacs.WASI.NN.DependencyInjection` to
host-packages automatically. Mirrors round-18's `--wasi-nn`
plumbing for the OnnxRuntime case. Without it, the resolver had
incomplete WitSource coverage and post-`compute` lifts trapped
with out-of-bounds memory access. The new `--wasi-nn-backend`
flag suggested in round-19 isn't needed — the implicit
`--bind` walk covers the same UX.

### `EnableDynamicLoading` on backend csprojs (gap 27)

Round-22 verification confirmed gaps 24-26 closed correctly —
end-to-end Qwen2.5 0.5B GGUF inference produced real output
through `wacs run --wasip2 --bind <LlamaSharp.dll>` after manual
deps staging. The remaining hurdle was a packaging issue: the
`Wacs.WASI.NN.LlamaSharp` library project's bin emitted only the
backend assembly + project refs, NOT the upstream NuGet
transitives (`LLamaSharp.dll`, `LLamaSharp.Backend.Cpu`'s
RID-specific natives, `Microsoft.Extensions.*`). At
`Assembly.LoadFrom(<path>)` time, the LoadFromContext resolver
read deps.json but couldn't satisfy the deps from the runtime's
TPA list (Wacs.Console doesn't carry LlamaSharp) or the empty
LoadFromContext directory.

Fix: `<EnableDynamicLoading>true</EnableDynamicLoading>` in
`Wacs.WASI.NN.LlamaSharp.csproj` (and the symmetric
`Wacs.WASI.NN.MLNet.csproj`). MSBuild now copies every NuGet
managed dep + RID-specific native lib into the project's bin,
and the deps.json points at them locally. Bin grows from
~10 MB to ~150 MB (LlamaSharp's natives are chunky); acceptable
for a backend whose entire purpose is loading multi-GB models.

ONNX backend takes a different path — round-1 already bundles
`Wacs.WASI.NN.OnnxRuntime` directly into `Wacs.Console`'s csproj
via `ExcludeAssets="compile"`, which is why `--wasi-nn` works
bare-name. Gap 27's fix is for the embedder-supplies-the-backend
flow (`--bind <path>`), not the bundled-default-backend flow.

Documentation: `docs/COMPONENT_CHAINING.md` gains a fully worked
GGUF inference example walking through the build + run + how
each prior fix participates. The Wacs.WASI.NN README's CLI
quick-start now points at the explicit-path form (the bare-name
`--bind Wacs.WASI.NN.LlamaSharp` only works when the assembly
is on the CLI's TPA, which it isn't unless an embedder bundles
it explicitly).

### Pre-load `--bind` assemblies before scope construction (gap 26)

Round-21 verification revealed that the `TryLoadAssembly`
AppDomain fallback was correct — but the auto-wire ran during
`WasiPreview2RuntimeScope` construction in
`ExecuteComponentTranspiled`, which fires from
`configureImports`. `--bind` doesn't run until later, in
`ApplyBindings` (intentionally, so explicit `BindHostFunction`
shims can override the wasip2 trap-stubs). At scope-construction
time, `--bind`-supplied assemblies aren't in AppDomain yet —
so the round-21 walk has nothing to find.

Fix: split the load step from the bind step. New
`BindingLoader.LoadAssembly(string)` returns just the resolved
`Assembly` without activating any `IBindable` types; existing
`LoadFromAssembly(string)` delegates to it. New
`PreloadBindAssemblies` in `RunHandler` calls
`BindingLoader.LoadAssembly` for every `--bind` / shorthand
entry BEFORE scope construction. The actual `BindToRuntime`
calls still defer to `ApplyBindings` (preserving override
semantics); `Assembly.LoadFrom` is idempotent on path so the
second pass is a no-op.

The two-phase load-then-bind pattern matches what round 1 /
round 7 already established for the IBindable lifecycle.

### `TryLoadAssembly` AppDomain fallback (gap 25)

Round-20 verification surfaced gap 25: with the LoadByName
parity fix in, the auto-wire still silently no-op'd for
`--bind <path-to-LlamaSharp.dll>` because
`WasiPreview2RuntimeScope.TryLoadAssembly` used
`Assembly.Load(name)` only. `Assembly.Load` searches the
default load context's by-name registry; `--bind <path>`
lands the assembly via `Assembly.LoadFrom` into the
`LoadFromContext`, where the by-name lookup misses.

Same architectural shape as the round-18 fix in
`HostPackageResolver.TryFindResourceImpl`. `TryLoadAssembly`
now walks `AppDomain.CurrentDomain.GetAssemblies()` on miss,
matching by `Assembly.GetName().Name` (case-insensitive). The
fallback skips dynamic assemblies and catches malformed-
metadata exceptions from collectable contexts so a single
hiccup can't blank out the search.

The `TryFindResourceImpl` AppDomain walk and the new
`TryLoadAssembly` AppDomain walk are deliberately
duplicated (both ~25 LOC) rather than extracted into a
shared helper — they're in different assemblies (resolver in
`Wacs.Transpiler.Lib`, scope in `Wacs.WASI.Preview2.DependencyInjection`)
and the cross-package coupling isn't worth a shared
utility yet.

### Test surface

- `Wacs.WASI.NN.Test/GraphFuncsImplLoadByNameTests` (3 tests):
  `LoadByNameBackend` direct-path, byte-flow fallback when
  `LoadByNameBackend` null, and the diagnostic NotFound when
  neither is wired (asserts the error message mentions
  `LoadByNameBackend` so the failure mode is discoverable).
- `Wacs.WASI.NN.LlamaSharp.Test/WasiPreview2RuntimeScopeLlamaSharpTests`:
  the auto-wire fires on a real `WasiPreview2RuntimeScope`
  construction; `IGraphFuncs.LoadByName` no longer reports the
  pre-fix "no named-model resolver" symptom. The test project
  gains references to `Wacs.WASI.NN.DependencyInjection` and
  `Wacs.WASI.Preview2.DependencyInjection` (the only test
  project where all four needed packages co-exist without a
  cycle).

### Versions

- `WACS.WASI.NN.DependencyInjection` 0.2.0 → **0.2.1**
  (LoadByName routes through LoadByNameBackend)
- `WACS.WASI.NN.LlamaSharp` 0.2.0 → **0.2.1**
  (EnableDynamicLoading: bin carries the backend's NuGet
  transitives so `--bind <path>` LoadFromContext probes
  resolve)
- `WACS.WASI.NN.MLNet` 0.2.0 → **0.2.1**
  (EnableDynamicLoading: same shape — symmetric prep for the
  embedder-supplies-the-backend flow)
- `WACS.WASI.Preview2.DependencyInjection` 0.1.2 → **0.1.4**
  (LlamaSharp auto-wire in `WasiPreview2RuntimeScope` +
  `TryLoadAssembly` AppDomain fallback)
- `WACS.Transpiler.Lib` 0.8.10 → **0.8.11**
  (`BindingLoader.LoadAssembly` load-only entry point)
- `WACS.Cli` 1.5.14 → **1.5.18** (release event +
  `--bind` → DI-sibling auto-pull +
  `PreloadBindAssemblies` ordering fix)

(Untouched: `WACS`, `WASI.Preview1`, `.Preview2`, `.WASI.NN`,
`.WASI.NN.OnnxRuntime`, `.WASI.NN.LlamaSharp`,
`.WASI.NN.MLNet`, `WACS.Transpiler.Lib`, `WACS.ComponentModel`,
`WACS.HostBindings.Abstractions`.)

## WACS.Cli 1.5.14 / WACS.Transpiler.Lib 0.8.10 — `[constructor]X` SourceGen-shape impl-class discovery falls back to AppDomain (gap 23)

The wasi-nn SLM's `Tensor::new(dimensions, ty, data)` returned
handle 0 to the guest, tripping `[method]tensor.data(0)` with
"Handle 0 is reserved as the null sentinel." Round-17
verification surfaced this as gap 23, hypothesized as a regression
in the round-9 constructor `AllocateResource` tail. The actual
root cause was different — and round-9's emit IL was still
correct.

### Root cause

`HostPackageResolver.TryFindResourceImpl` walked **only** the
explicit `HostPackages` list when looking for a SourceGen-shape
impl class (parameterless ctor + `void Create(args)`). The
WASI-NN typed interfaces (`ITensor`, `IGraph`,
`IGraphExecutionContext`) live in `Wacs.WASI.NN`, but the impl
classes (`Tensor`, `Graph`, `GraphExecutionContext`) live in
the **DI sibling** assembly `Wacs.WASI.NN.DependencyInjection`.

When the CLI runs `wacs run --wasi-nn`, `ResolveHostPackages`
historically added `Wacs.WASI.Preview2` + `Wacs.WASI.NN` —
not the DI siblings. `TryFindResourceImpl(typeof(ITensor))`
returned false → `CanEmitDirect`'s SourceGen-ctor gate
rejected `[constructor]tensor` (line 128-131 of
`DirectLinkedImportEmit`) → the call fell back to legacy
delegate dispatch — whose generated handler doesn't allocate
through `WasiPreview2Resources`, leaving 0 on the wasm side
as the constructor's i32 result.

Unit tests didn't catch this because every existing fixture
defines the impl class in the same assembly as the test, so
HostPackages always contained it.

### Fixes (defense in depth)

**Resolver fallback.** `TryFindResourceImpl` now walks
HostPackages first (matching the existing contract), then falls
back to AppDomain assemblies when the impl isn't found.
`WasiPreview2RuntimeScope.ReflectivelyAddWasiNN` already
`Assembly.Load`s the DI sibling at scope-construction time, so
the assembly is present in AppDomain before transpilation
runs — the fallback picks it up. Mirrors the three-tier
search `FindBundleType` and `FindWasiPreview2Resources`
already use for bundle/resources lookup. Catches via
`SearchForImpl` to keep `ReflectionTypeLoadException` /
`NotSupportedException` from blocking the search on a
collectable / dynamic AppDomain assembly.

**CLI host-package list.** `RunHandler.ResolveHostPackages`
now explicitly adds `Wacs.WASI.Preview2.DependencyInjection`
(when `--wasip2`) and `Wacs.WASI.NN.DependencyInjection`
(when `--wasi-nn`). Symmetric in `BuildHandler`. Avoids the
AppDomain-fallback round-trip for the common path and keeps
the resolver's first-tier search complete.

### Test surface

- `HostPackageResolver_TryFindResourceImpl_FallsBackToAppDomain`
  — passes empty HostPackages, asserts the resolver still
  finds `TestSgWidget` via AppDomain (xunit loads the test
  assembly into AppDomain).
- `DirectLinkedImport_SourceGenCtorWithParam_AllocatesAndResolves`
  — single-u32 SourceGen ctor + read; sanity check the
  with-PARAM constructor path.
- `DirectLinkedImport_SourceGenCtorWithListParams_AllocatesAndResolves`
  — `(uint[], enum, byte[])` SourceGen ctor matching the
  wasi-nn `Tensor::new` shape; checksum verification proves
  both PARAM lift and `AllocateResource` fired.

Wacs.Transpiler.Test went from 773 → 776 (+3).
All other suites unchanged.

### Versions

- `WACS.Transpiler.Lib` 0.8.9 → **0.8.10** (TryFindResourceImpl
  AppDomain fallback)
- `WACS.Cli` 1.5.13 → **1.5.14** (DI siblings added to
  ResolveHostPackages)

(Untouched: `WACS`, `WASI.Preview1`, `.Preview2`, `.Preview2.DI`,
`.WASI.NN`, `.WASI.NN.DI`, `.WASI.NN.OnnxRuntime`,
`WACS.ComponentModel`, `WACS.HostBindings.Abstractions`.)

## WACS.Cli 1.5.13 / WACS.Transpiler.Lib 0.8.9 / WACS.ComponentModel 0.3.4 — `list<tuple<string, own<R>>>` PARAM lift + Result-Ok arm store (closes wasi-nn SLM compute)

The wasi-nn SLM's `wasi:nn/inference.compute(inputs:
list<tuple<string, own<tensor>>>) -> result<list<tuple<string,
own<tensor>>>, own<error>>` had two missing direct-link branches.
Round-15-followup verification (gap 22) showed compute() reaching
ORT and returning, but the guest finding no `"logits"` output —
because the call wasn't direct-linking and the legacy delegate
path was corrupting the per-tuple string field.

### PARAM lift `(T1,...,Tn)[]`

`CanonicalSlotCount` and `EmitLiftForType` didn't recognize an
array-of-tuple-of-flat-fields. CanEmitDirect rejected compute,
forcing the call onto the legacy IBindable handler whose
list<tuple<string, own<R>>> lift mis-bound the string field.

Fix:
- `CanonicalSlotCount` adds a branch for `(T1,...,Tn)[]` where
  each Ti is a flat field (primitive / string / byte[] / Option /
  resource). Returns 2 slots: outer (i32 ptr, i32 count).
- `EmitLiftForType` adds a branch dispatching to a new
  `EmitLiftListOfRecordOrTuple` helper.
- `EmitLiftListOfRecordOrTuple` allocates a `T[]` of size
  `count`, walks per-element offsets, calls
  `EmitInlineRecordOrTupleLift` for each element, stelems into
  the array.
- `EmitInlineRecordOrTupleLift` reads each tuple field at its
  canon-ABI offset, dispatches via `EmitLiftFieldFromMem`
  (string → ReadI32×2 + LiftUtf8; byte[] → ReadI32×2 +
  LiftPrim<byte>; resource → ReadI32 + Resources.GetResource;
  primitive → ReadXxxLE), constructs the ValueTuple via
  `ResolveValueTupleCtor`.
- New `ResolveLoadMethod` helper + `LoadMethodCache` map types
  to `PrimitiveStore.ReadXxxLE` Methods.

### RETURN store `Result<list<tuple<string, own<R>>>, own<error>>`'s Ok arm

`IsResultArmStorable` accepted only primitive-element /
string-element arrays in the variable-length branch; the
list-of-tuple-of-flat-fields case fell through to the
fixed-width fallback.

Fix:
- `IsResultArmStorable` extends the array branch to accept
  `IsTupleOfPrimitives` / `IsTupleOfFlatFields` /
  `IsRecordOf...` element types.
- `EmitResultArmStore` adds an `isAggregateArray` branch
  that dispatches to `EmitListOfRecordOrTupleReturn` at the
  arm's `valueOffset` (so the (outer ptr, count) pair lands
  at retArea+valueOffset+0/+4).
- `EmitListOfRecordOrTupleReturn` refactored to take an
  optional `baseOffset` parameter for the (ptr, count)
  pair write — same approach as round-13's per-arm-offset
  refactors.

### PrimitiveStore additions

`Wacs.ComponentModel.CanonicalABI.PrimitiveStore` gains seven
read helpers: `ReadI8`, `ReadI16LE`, `ReadI64LE`, `ReadU64LE`,
`ReadF32LE`, `ReadF64LE`, `ReadBool`. Mirrors the existing Store
family. Used by direct-link's per-field-from-memory lift; the
F32/F64 helpers bit-cast through Int32/Int64 for
netstandard2.1 (matching the StoreF32/StoreF64 pattern, since
`BinaryPrimitives.ReadSingle/DoubleLittleEndian` are .NET 5+).

### Test surface

New `DirectLinkedImport_FreeFnComputeRoundtrip_LiftsAndStoresListOfTupleStringOwn`
in `Wacs.Transpiler.Test/DirectLinkedImportTests.cs`. The wat
fixture stages 3 (string, IGraph) tuples in linear memory,
calls compute, and reads back the OK-arm outer (ptr, count) +
per-element fields. Verifies:

1. `compute` direct-links (binding count = 1)
2. PARAM lift: host stub captures lifted `(string, IGraph)[]`
   with names "alpha", "beta", "gamma" matching what the guest
   staged + the same IGraph instances resolved from the
   pre-allocated handles
3. RETURN store: disc=0, outer count=3; per-element name +
   handle written at outer_ptr + i*12; handles round-trip
   through `Resources.GetResource` to the same IGraph
   instances the host returned
4. The host echoes inputs with names prefixed `out_` — names
   round-trip BOTH PARAM lift and RETURN store

`TestLoaderFuncs.LastInputs` capture confirms the lift side;
guest-readable memory probes via `read_u8` / `read_i32`
exports confirm the store side.

Wacs.Transpiler.Test went from 772 → 773 (1 added).
All other suites unchanged.

### Versions

- `WACS.ComponentModel` 0.3.3 → **0.3.4** (PrimitiveStore Read*
  helpers)
- `WACS.Transpiler.Lib` 0.8.8 → **0.8.9** (PARAM lift +
  RETURN store + ResolveLoadMethod)
- `WACS.Cli` 1.5.12 → **1.5.13** (release event)

(Untouched: `WACS`, `WASI.Preview1`, `.Preview2`,
`.Preview2.DI`, `.WASI.NN`, `.WASI.NN.DI`, `.WASI.NN.OnnxRuntime`,
`WACS.HostBindings.Abstractions`.)

## WACS.Cli 1.5.12 / WACS.WASI.NN.OnnxRuntime 0.2.2 — bundled ORT NuGet 1.21.0 → 1.22.0 (the version that actually relaxes GroupQueryAttention)

Round 15's bump to 1.21.0 was based on the round-14 hypothesis
that the contrib-op input-range relaxation landed at 1.21. The
user's round-15 verification disproved that — the actual
binary-level check on the working wasmtime host
(`strings target/release/wasi-nn-slm-host`) reports **1.22.0**,
and 1.21.0 still rejects 11 inputs to
`com.microsoft::GroupQueryAttention:1` with the same
`[min=7, max=9]` range.

Fix: pin `Microsoft.ML.OnnxRuntime` at **1.22.0** in
`Wacs.WASI.NN.OnnxRuntime.csproj`. Native dylib in test bin
verified at 1.22.0 via `strings runtimes/osx-x64/native/libonnxruntime.dylib`.

Test surface: re-ran all four NN suites against 1.22.0 — same
green pattern as 1.21.0 (10/10 + 18/18 + 6/6+2skip + 7/7). No
public-API drift between 1.20.1 and 1.22.0 for the surface
this package uses (`SessionOptions`, `InferenceSession`,
`OrtValue`).

The sibling shutdown crash (`libc++abi: mutex lock failed`
after a guest panic on macOS-arm64) reproduced on 1.21.0;
unverified at 1.22.0. Track separately if it persists past
the user's next local repro.

### Versions

- `WACS.WASI.NN.OnnxRuntime` 0.2.1 → **0.2.2** (NuGet floor 1.22.0)
- `WACS.Cli` 1.5.11 → **1.5.12** (release event)

(Untouched: `WACS`, `WASI.Preview1`, `.Preview2`, `.Preview2.DI`,
`.WASI.NN`, `.WASI.NN.DI`, `WACS.Transpiler.Lib`,
`WACS.ComponentModel`, `WACS.HostBindings.Abstractions`.)

## WACS.Cli 1.5.11 / WACS.WASI.NN.OnnxRuntime 0.2.1 — bundled ORT NuGet 1.20.1 → 1.21.0 (Gemma 3 GroupQueryAttention shape)

The wasi-nn SLM (Gemma 3 270M ONNX export) loaded all the way to
ORT's `InferenceSession` constructor after round 14 closed gap 20,
then tripped graph validation:

```
[ErrorCode:InvalidGraph] This is an invalid model.
In Node, ("/model/layers.0/attn/GroupQueryAttention",
GroupQueryAttention, "com.microsoft", -1) ...
Error Node has input size 11 not in range [min=7, max=9].
```

The contrib op `com.microsoft.GroupQueryAttention` widened its
allowed input range from 7..9 to 7..11 across ORT 1.20→1.21 (added
optional `attention_bias` + positional inputs). Gemma 3's export
emits all 11 inputs, so it loads on 1.21+ and trips graph
validation on 1.20.x.

`Wacs.WASI.NN.OnnxRuntime/Wacs.WASI.NN.OnnxRuntime.csproj` now
pins `Microsoft.ML.OnnxRuntime` at **1.21.0**. No public-API
break for the surface this package uses (`SessionOptions`,
`InferenceSession`, `OrtValue`); verified by the matching
wasi-nn host's `ort 2.0.0-rc.10` Rust dependency loading the
same model bytes successfully.

Test surface: `Wacs.WASI.NN.OnnxRuntime.Test` (10/10) +
`Wacs.WASI.NN.Test` (18/18) + `Wacs.WASI.NN.LlamaSharp.Test`
(6/6, 2 skip) + `Wacs.WASI.NN.MLNet.Test` (7/7) all pass
unchanged — no API drift visible from our consumer side.

This is a downstream-dependency-version gap, not an
architectural one: the canonical-ABI lift, DI bundle, backend
registration, direct-link emit, and resource-handle path
closed in rounds 13-14 are all correct. The bump just gives
ORT enough op coverage to validate a real SLM graph.

### Versions

- `WACS.WASI.NN.OnnxRuntime` 0.2.0 → **0.2.1** (NuGet bump only)
- `WACS.Cli` 1.5.10 → **1.5.11** (release event for the
  bundled ORT bump)

(Untouched: `WACS`, `WASI.Preview1`, `.Preview2`, `.Preview2.DI`,
`.WASI.NN`, `.WASI.NN.DI`, `WACS.Transpiler.Lib`,
`WACS.ComponentModel`, `WACS.HostBindings.Abstractions`.)

## WACS.Cli 1.5.10 / WACS.WASI.Preview2.DependencyInjection 0.1.2 — wasi-nn ONNX backend wires through DI under `--wasi-nn`

After round 13's `byte[][]` fix unblocked direct-link `graph.load`,
the SLM still surfaced `InvalidEncoding: No backend registered for
encoding ONNX`. Two layered bugs in
`WasiPreview2RuntimeScope.ReflectivelyAddWasiNN`:

1. **Wrong-instance mutation.** The legacy
   `AutoRegisterOnnxBackend` post-hoc-mutated a
   `WasiNNConfiguration` it pulled out of the descriptor's
   `ImplementationInstance`. With WASI.NN's
   `services.TryAddSingleton(opts.Configuration)` registration
   landing the instance from `new WasiNNDependencyInjectionOptions()`,
   `GraphFuncsImpl(sp.GetRequiredService<WasiNNConfiguration>())`
   could resolve a different physical object — empty `Backends`,
   `InvalidEncoding` at guest-call time.
2. **Silent type lookup miss.** Even after switching to the
   configure-callback approach, `nnAsm.GetType(
   "Wacs.WASI.NN.Types.GraphEncoding")` was reading the
   sibling-namespace type out of the
   `Wacs.WASI.NN.DependencyInjection` assembly — the type lives
   in `Wacs.WASI.NN`. `GetType` returned null, the early-return
   short-circuited the configure delegate to null, and
   `AddWasiNN(services, null)` ran with no backend wiring at all.

Fix: `BuildOnnxConfigureCallback` now derives the encoding +
backend interface types from `AddBackend`'s parameter
signature (single source of truth), and `AddWasiNN` is invoked
with a pre-built `Linq.Expressions.Compile()`'d delegate that
runs INSIDE `AddWasiNN`'s own configure step — so the same
`WasiNNConfiguration` instance the singleton resolves is the
instance the backend was added to. Surfaces the failure modes
that DO remain (OnnxBackend type missing, parameterless ctor
throws) as stderr warnings so the next round of debugging
isn't a guessing game.

Test surface: new `WasiPreview2RuntimeScopeTests` in
`Wacs.WASI.NN.OnnxRuntime.Test` (the only test project where
WASI.Preview2.DI + WASI.NN.DI + WASI.NN.OnnxRuntime co-exist
without a cycle). Constructs a real scope, reaches
`IGraphFuncs` through the composite bundle, and asserts a
`graph.load(_, GraphEncoding.Onnx, _)` does NOT short-circuit
with `InvalidEncoding`. The test captures stderr from
`WasiPreview2RuntimeScope` so a future regression's
diagnostic warning shows up in the failure message.

### Versions

- `WACS.WASI.Preview2.DependencyInjection` 0.1.1 → **0.1.2**
  (configure-callback wiring + diagnostic stderr)
- `WACS.Cli` 1.5.9 → **1.5.10** (release event for the
  Preview2.DI bump)

(Untouched: `WACS`, `WASI.Preview1`, `.Preview2`,
`.WASI.NN.*`, `WACS.Transpiler.Lib`, `WACS.ComponentModel`,
`WACS.HostBindings.Abstractions`.)

## WACS.Cli 1.5.9 / WACS.Transpiler.Lib 0.8.8 / WACS.ComponentModel 0.3.3 — `byte[][]` PARAM direct-link (closes the wasi-nn SLM gap)

The wasi-nn SLM's `wasi:nn/graph-funcs.load(builders: list<list<u8>>,
encoding, target) -> result<own<graph>, own<error>>` had a
`byte[][]` parameter that `CanonicalSlotCount` didn't recognize.
`CanEmitDirect` rejected the binding, the call fell back to
delegate dispatch through the IBindable's WitBindings handler, and
the OK-arm `IGraph` handle landed in `host.Graphs` (WitBindings's
own resource registry) instead of `WasiPreview2Resources`. The
subsequent `[method]graph.init-execution-context` direct-linked
correctly and looked up the handle in `WasiPreview2Resources` —
miss, "Resource handle 4 is not registered."

Fix: thread `byte[][]` through the direct-link IL emit pipeline:

- `Wacs.ComponentModel.CanonicalABI.ListMarshal.LiftByteArrayList(
   MemoryInstance memory, int listPtr, int count) -> byte[][]`
  walks the outer (inner_ptr, inner_len) pair table and copies
  each inner buffer out via `mem.AsSpan(...).ToArray()`. Symmetric
  with the existing `PrimitiveStore.StoreByteArrayList` on the
  store/lower side.
- `DirectLinkedImportEmit.CanonicalSlotCount` recognizes
  `typeof(byte[][])` as a 2-i32-slot wire shape (outer ptr, count).
- `EmitLiftForType` adds a `byte[][]` branch that emits IL calling
  the new helper.
- New cached `LiftByteArrayListMethod` `MethodInfo`.

Side effect: the SLM's `load` now direct-links cleanly. The OK-arm
IGraph allocates in `WasiPreview2Resources` (the same registry the
direct-link IL looks up), so `init-execution-context`'s subsequent
`Resources.GetResource(IGraph, handle)` resolves correctly. Closes
the wasi-nn handle path.

Test surface: replaces round-10's gate-only
`DirectLinkedImport_FreeFnByteJaggedParam_GateAccepts` with a true
end-to-end test
`DirectLinkedImport_FreeFnByteJaggedParam_LiftsListOfBytes`. The
wasm fixture writes the (outer_ptr, outer_count) header + per-
element (inner_ptr, inner_len) pairs + inner buffers into memory,
calls the import, and verifies:

1. `load-bytes` direct-links (binding count = 1)
2. The host stub captures the lifted `byte[][]` matching what the
   guest staged (`{0x11, 0x22, 0x33}`, `{0xAA, 0xBB, 0xCC, 0xDD}`)
3. Encoding and target args round-trip
4. The OK-arm IGraph handle resolves through `WasiPreview2Resources`
   (proves single-registry consistency post-fix)

Wacs.Transpiler 771 unchanged in count (the test was renamed +
upgraded, not added). All other suites unchanged.

### Versions

- `WACS.ComponentModel` 0.3.2 → **0.3.3** (LiftByteArrayList helper)
- `WACS.Cli` 1.5.8 → **1.5.9** (release event)
- `WACS.Transpiler.Lib` 0.8.7 → **0.8.8** (CanonicalSlotCount + emit)

(Untouched: `WACS`, `WASI.Preview1`, `.Preview2`,
`.HostBindings.Abstractions`, `WACS.WASI.NN`. The library mechanism
is purely additive — `byte[][]` now joins `byte[]`, `string[]`,
and `T[]`-of-primitives in the recognized PARAM shapes.)

## WACS 0.13.7 / WACS.Cli 1.5.8 / WACS.Transpiler.Lib 0.8.7 — round-12 follow-up: predicate alignment + trap-stub-friendly shadow

Round 12 introduced a runtime-level shadow rule for direct-link-
covered entities. Two issues surfaced under SLM verification
(round-12 follow-up):

1. **Predicate mismatch.** The pre-pass marked everything the
   resolver matched (interface granularity), but the IL emit only
   direct-links shapes `CanEmitDirect` accepts (per-method).
   Resolver-matched-but-emit-rejected entities (e.g.
   `wasi:nn/errors.[method]error.code` when its emit gate
   rejects, or any binding with an unsupported param shape) got
   shadowed but never had IL emitted, leaving no fallback.

2. **Trap-stub shadowing.** The shadow rule fired
   unconditionally, blocking `ComponentImportStubs.RegisterAll`'s
   first-call trap-stub registration too. Without that
   placeholder in `_entityBindings`, the runtime's instantiation
   pre-validation (`WasmRuntimeInstantiation.cs:169`) threw "The
   imported Function was not provided by the environment" before
   any user-level code ran.

Two-line fix in each direction:

**Predicate alignment.** `ComponentTranspiler`'s pre-pass now
mirrors `CallEmitter.EmitImportCall`'s direct-link gate exactly:
resolver match + `PreferredBundleType` set + `CanEmitDirect`
accepts + (resource methods need `PreferredResourcesType`). Same
predicate, same order — pre-pass and IL emit can't disagree on
which entities are direct-link covered.

**Trap-stub-friendly shadow.** `WasmRuntime.BindHostFunction`'s
shadow check fires only when the entity is marked AND already has
a binding. The first registration (typically the trap-stub) goes
through; second-and-later registrations (the IBindable
overrides) drop. The trap-stub stays in `_entityBindings` as a
never-invoked placeholder while direct-link IL handles the
actual dispatch.

Test surface unchanged in count (still 3 [Fact]s in
`Wacs.Core.Test.BindingTests`); semantics updated:

- `BindHostFunction_DirectLinkCoverage_FirstRegisters_SecondShadows`
  — first call goes through, second is dropped.
- `BindHostFunction_NoCoverage_RegistersNormally` — sanity:
  unmarked entities still bind on every call.
- `BindHostFunction_PartialCoverage_SelectiveShadow` — covers
  the SLM mixed-ABI scenario (WIT covered, WITX not).

### Versions

- `WACS` 0.13.6 → **0.13.7** (shadow rule semantics)
- `WACS.Cli` 1.5.7 → **1.5.8** (no code change; same release event)
- `WACS.Transpiler.Lib` 0.8.6 → **0.8.7** (pre-pass predicate)

### Out of scope (separate gap if it surfaces)

The wasi-nn SLM still hits a registry split when `load`'s
`byte[][]` PARAM trips a `CanonicalSlotCount` rejection — the
import falls back to delegate dispatch through the IBindable's
WitBindings handler, allocating in `host.Graphs`, while
`init-execution-context` direct-links and looks up in
`WasiPreview2Resources`. Closing that requires either:

- Adding `byte[][]` (and similar jagged-array) PARAM support to
  `CanonicalSlotCount` + `DirectLinkedImportEmit`, or
- Bridging the WitBindings resource registries
  (`host.Graphs`/`Tensors`/`Errors`/`Contexts`) to share their
  i32 namespace with `WasiPreview2Resources`.

Either is a substantive change tracked as gap 19.

## WACS 0.13.6 / WACS.Cli 1.5.7 / WACS.Transpiler.Lib 0.8.6 — direct-link coverage shadows BindHostFunction registrations

Replaces the round-11 CLI gating kludge (`if (opts.WasiNN &&
!opts.Wasip2)`) with a runtime-level architectural rule. The kludge
was fragile in the ways the user called out — hardcoded the
`Wacs.WASI.NN.OnnxRuntime` package name, tied the carve-out to
specific CLI flag combinations, and didn't generalize to future
wasi-* packages or programmatic embedders that wire both paths.

### Architectural rule

`WasmRuntime` tracks a set of `(module, entity)` pairs provided
by transpiler-direct-link bundles:

```csharp
public void MarkEntityProvidedByDirectLink((string, string) id);
public bool IsEntityProvidedByDirectLink((string, string) id);
```

`BindHostFunction` (both delegate and `IFunctionInstance`
overloads) silently no-ops registrations for entities in this
set. The emitted IL hardcodes the call into the bundle's typed
interface and bypasses the runtime entity registry, so any
later registration for the same entity would shadow nothing
useful — and for resource-returning host paths, would alias the
resource-handle namespace across two independent registries (the
SLM gap-18 trip site).

### Pre-pass

`ComponentTranspiler.TranspileSingleModule` walks the primary
core module's imports BEFORE invoking `configureImports`. For
every import where the resolver matches a binding, it calls
`runtime.MarkEntityProvidedByDirectLink`. So when `configureImports`
later runs `WasiPreview2RuntimeScope` construction +
`ApplyBindings` IBindables, every bundle-covered entity's
registration silently drops.

### Selective shadow

The rule is per-entity, not per-package. An IBindable that
covers BOTH bundle-covered and bundle-uncovered entities (e.g.
`WasiNNHost.BindToRuntime` calls both `WitxBindings.Bind` for
the legacy `wasi_ephemeral_nn` core-wasm ABI AND `WitBindings.Bind`
for the WIT component-model ABI) gets its WIT registrations
shadowed (covered by the bundle) and its WITX registrations
through (not covered). Mixed-ABI guests don't lose the legacy
path.

### CLI revert

`Wacs.Console/Verbs/RunHandler.cs::ApplyBindings` reverts the
`opts.WasiNN && !opts.Wasip2` gating. The architectural rule
now lives in the runtime; the CLI doesn't need to know which
packages are direct-link-covered. Future wasi-* host packages
(wasi-tls, wasi-keyvalue, etc.) automatically benefit — drop
the package's `[WitSource]` interfaces into a bundle, and any
matching IBindable's `BindHostFunction` calls drop without
config.

### Test surface

3 new [Fact]s in `Wacs.Core.Test.BindingTests`:

- `BindHostFunction_DirectLinkCoverage_SilentlyShadowsRegistration`
  — mark, then BindHostFunction; entity registry stays empty.
- `BindHostFunction_NoCoverage_RegistersNormally` — sanity:
  unmarked entities still bind.
- `BindHostFunction_PartialCoverage_SelectiveShadow` — mark only
  the WIT entity; verify the WITX BindHostFunction still
  registers (mixed-ABI safety).

Total Wacs.Core 394 → **397** (+3). All other suites unchanged.

### Versions

- `WACS` 0.13.5 → **0.13.6** (new public API on `WasmRuntime`)
- `WACS.Cli` 1.5.6 → **1.5.7** (revert kludge)
- `WACS.Transpiler.Lib` 0.8.5 → **0.8.6** (pre-pass in
  `TranspileSingleModule`)

(Untouched: `WACS.ComponentModel`, `WASI.Preview1`, `.Preview2`,
`.HostBindings.Abstractions`, `WACS.WASI.NN`. The library
mechanism replaces the CLI workaround; no name-based carve-outs
anywhere.)

## WACS.Cli 1.5.6 — `--wasi-nn` skips legacy IBindable under `--wasip2` to close registry split

Pre-fix, `wacs run --wasip2 --wasi-nn` registered the WASI.NN
backend twice — once via `Wacs.WASI.NN.OnnxRuntime`'s `IBindable`
(which calls `WitBindings.Bind` → registers BindHostFunction
handlers + WASI.NN's internal `host.Graphs` / `host.Tensors` /
`host.Errors` resource registries) and once via the wasip2
RuntimeScope's `AutoRegisterOnnxBackend` (which wires the ONNX
backend into the DI bundle's `WasiNNConfiguration`, surfaced
through `WasiPreview2NNBundle`'s `IGraphFuncs` to the transpiler's
direct-link emit, with handles minted in `WasiPreview2Resources`).

The two registries hold the same `i32` handle namespace but no
bridge between them. A guest minting `wasi:nn/graph-funcs.load`'s
return handle through one path and looking it up later through
the other gets either `Resource handle N is not registered` (if
the lookup misses) or `Handle 0 is reserved as the null sentinel`
(if a default-init slot leaked through). The `wasi-nn-slm.wasm`
demo trips this between `load()` and
`graph.init_execution_context()`.

Fix per round-10's option (2): under `opts.Wasip2`, skip the
WASI.NN IBindable from the `ApplyBindings` path. The
`ReflectivelyAddWasiNN` flow already wires the ONNX backend to
the direct-link side; the IBindable's `WasiNNHost` (separate
`Graphs`/`Tensors`/`Errors`) is redundant and structurally
incorrect under wasip2. Interpreter-only `--wasi --wasi-nn`
(Preview 1 + WITX legacy ABI) keeps the IBindable since
direct-link isn't on its path.

```diff
-if (opts.WasiNN) paths.Add("Wacs.WASI.NN.OnnxRuntime");
+if (opts.WasiNN && !opts.Wasip2)
+    paths.Add("Wacs.WASI.NN.OnnxRuntime");
```

Verified by the round-10 follow-up probe (`/tmp/nn-probe`): the
30-line wasi-nn shim that calls `load()` then
`graph.init_execution_context()` traps pre-fix at the second call
("Resource handle 4 is not registered"); post-fix the
WitBindings registration doesn't happen and the direct-link path
mints the handle in `WasiPreview2Resources` where the lookup
finds it.

Out of scope (separate gap if it surfaces): a programmatic
embedder that wires both paths explicitly (not via the CLI) hits
the same registry split. A library-level `WasiNNHost
.SuppressWitBindings` opt-out is the natural follow-up but not
needed to close the SLM trip site.

## WACS 0.13.5 / WACS.Cli 1.5.5 / WACS.Transpiler.Lib 0.8.5 — direct-link emit accepts SourceGen-shape resource constructors

`Wacs.ComponentModel.Bindgen.SourceGen` emits resource constructors
as `void Create(args)` instance methods on the resource interface
(rather than static factories returning the interface). The
`Wacs.WASI.NN.DependencyInjection.Tensor` impl class follows that
contract — public parameterless ctor + `void Create(...)` for the
two-step `Activator.CreateInstance` then `Create` lift the canonical
ABI's `[constructor]X` calls into.

Pre-fix, `DirectLinkedImportEmit.cs:101` rejected this shape
(`if (!method.IsStatic) return false`). The constructor fell
through to legacy delegate dispatch, which never bound a real
handle for it, and the guest received 0 (the canonical-ABI null
sentinel). The first downstream `[method]X.<x>` call AVs the host
on `Resources.GetResource(typeof(IFace), 0)` — observed end-to-end
in the `wasi-nn-slm.wasm` SLM after the round-7+8 high-address
fixes unblocked it that far.

`HostPackageResolver` adds `TryFindResourceImpl(Type
resourceInterface, out Type implType)` that walks the loaded
host-package assemblies for a public class implementing the
interface with a public parameterless constructor. Cached per-
interface; first match wins (stable order across host packages).

`DirectLinkedImportEmit`'s constructor gate now accepts both
shapes:

- **Static factory** — existing path. Method is static, returns
  the interface, IL emits `Call → AllocateResource`.
- **Void instance method** — new path. Method is non-static and
  returns void, resolver finds an impl class. IL emits
  `Newobj <impl>; dup; stloc inst; castclass <iface>` before the
  arg lift loop, then the lift loop pushes args, then `Callvirt
  <Create>` (void), then `ldloc inst; ldarg ctx; ldfld Resources;
  ldtoken <iface>; call typeof; ldloc inst; callvirt
  AllocateResource → handle`.

Test surface: new
`DirectLinkedImportTests.DirectLinkedImport_SourceGenCtorThenInstance_AllocatesAndResolves`
defines `ISgWidget` (SourceGen-shape, with `void Create();
read: func() -> u32;`) plus `TestSgWidget` (parameterless ctor +
sentinel-recording Create). Wasm imports `[constructor]widget` +
`[method]widget.read`, calls them in sequence, asserts the
sentinel value (42) round-trips. Pre-fix the gate rejects the
SourceGen shape; post-fix the test passes.

Out of scope (separate work): `wasi-nn-slm.wasm` end-to-end
verification stays the user's call locally to avoid the round-4 /
round-6 overclaim pattern.

## WACS 0.13.4 / WACS.Cli 1.5.4 / WACS.Transpiler.Lib 0.8.4 — high-address bulk memory ops + MemSlice chokepoint

Round 7 closed `(int)ea` truncation in the load/store helpers
(`MemoryHelpers.{Load,Store}*`) but missed the bulk-op family and the
`[OpHandler]`-dispatch chokepoint. Both had the same shape and the
same crash mode — any guest writing to a memory address past
`int.MaxValue` AVs the host process. Rust's release-mode `vec![0u8; N]`
lowers to a single `memory.fill` after `cabi_realloc`, so non-trivial
allocations past 2 GiB trip it.

Migrated to the `nuint` overloads added in 0.13.3:

- `Wacs.Transpiler.Lib/AOT/Emitters/BulkEmitter.cs`
  `BulkHelpers.{MemoryCopy, MemoryFill, MemoryInit}` — widen
  `dst` (and `src` for the dst-side memory in MemoryCopy) to
  `nuint` at the start, route through `mem.AsSpan(nuint, int)`.
  `MemoryInit`'s `src` stays `int` (data segment is byte[]-bounded).
- `Wacs.Core/Wacs.Core/Instructions/MemoryHandlers.cs` `MemSlice`
  — single chokepoint for every `[OpHandler]` load/store dispatch.
  Last line `return mem.AsSpan((int)ea, width)` becomes
  `return mem.AsSpan((nuint)ea, width)`. This site was missed in
  round 7's per-instruction-file sweep.
- `Wacs.Core/Wacs.Core/Instructions/MemoryBulk.cs` —
  `InstMemoryInit.Execute` (line 235), `InstMemoryCopy.Execute`
  (line 389), `InstMemoryFill.Execute` (line 459) all switch
  guest-memory address args from `(int)x` to `(nuint)x`.

Test surface: 3 new [Fact]s in
`Wacs.Transpiler.Test.MemoryHelpersHighAddressTests` covering
`BulkHelpers.MemoryFill / MemoryCopy / MemoryInit` at
`addr = 0x80000400` (~2 GiB + 1 KiB) on a NativePointer
33000-page memory. Pre-fix each AVs; post-fix bytes round-trip.
Total in that suite: 8/8 (5 from gap 15 + 3 from gap 16).

Out of scope: atomics still pin `int ea` through abstract
`InstAtomicLoad.DoLoad` signatures. Same-shape gap, different
cohort. Follow-up.

## WACS 0.13.3 / WACS.Cli 1.5.3 / WACS.Transpiler.Lib 0.8.3 — high-address load/store on NativePointer memories

`MemoryHelpers.StoreI32` / `LoadI32` (and every load/store/narrow/F32/F64
sibling) cast the effective address to `int` on the final
`mem.RefAs<byte>(...)` / `mem.AsSpan(...)` call. With ea > `int.MaxValue`
— anything past 2 GiB into a NativePointer-backed linear memory —
that cast wrapped to a negative pointer offset; the kernel signaled
SIGSEGV and the .NET runtime aborted with `AccessViolationException`.
Bypassed managed exception handling, so the wasm-trap-to-exit-1
path didn't catch it.

The bounds check itself was correct (`ea` is `long` and compared
against `mem.ByteLength` which is `nuint`). Only the truncating cast
on the access call was wrong.

`MemoryInstance` adds `nuint` overloads alongside the existing `int`
ones:
- `RefAs<T>(nuint ea)` — `byte* + nuint` pointer arithmetic on
  `NativeBase`; ManagedArray branch keeps the safe `(int)ea` cast
  (Array.MaxLength bounds the byte[] backing ≤ 2 GiB).
- `AsSpan(nuint offset, int length)` — same shape for narrow
  load/store siblings (`StoreI32_8`, etc.).

Migrated call sites:
- `Wacs.Transpiler.Lib/AOT/Emitters/MemoryEmitter.cs`
  `MemoryHelpers` — every `(int)ea` cast (59 sites across i32/i64
  + every narrow variant + f32/f64) now passes `(nuint)ea`.
- `Wacs.Core/Wacs.Core/Instructions/Memory/{I32,I64,F}MemoryLoad.cs`
  + `Inst{I32,I64}Store.cs` + `FMemoryStore.cs` — interpreter
  per-instruction handlers had the same shape; now route through
  the `nuint` overloads.

Test surface: new
`Wacs.Transpiler.Test.MemoryHelpersHighAddressTests` covers
`StoreI32` / `LoadI32` / `StoreI64` / `LoadI64` / `StoreI32_8` +
`LoadI32_8U` / `StoreF32` / `LoadF32` / `StoreF64` / `LoadF64` at
`ea = 0x80000000 + 1024` (~2 GiB into the memory) on a NativePointer
33000-page (~2.0625 GiB) instance. Pre-fix every test AVs;
post-fix all five round-trip cleanly. `NativeMemory.AllocZeroed`
is lazy-zero on calloc so the 2 GiB virtual reservation does not
commit physical pages.

Out of scope (separate gap): atomics. `AtomicHelpers.CheckEa`
still returns `int`, and the `int ea` parameter cascades through
the abstract `InstAtomicLoad.DoLoad(ExecContext, int ea)` /
`InstAtomicStore.DoStore` signatures. Same shape as gap 15 but a
different cohort of guests (atomic-using shared-memory threading);
follow-up.

## WACS 0.13.2 / WACS.Cli 1.5.2 / WACS.Transpiler.Lib 0.8.2 / WACS.ComponentModel 0.3.2 — host paths route through MemoryInstance; retire byte[] pinning across canonical-ABI

NativePointer-mode memories carry an empty sentinel `Array.Empty<byte>()`
in `MemoryInstance.Data` so accidental `mem.Data[i]` access surfaces
loudly. Pre-fix, every host-side canonical-ABI path pinned that field
directly: the AotLinked active-data-segment install copied through
`BulkHelpers.CopySegmentToMemory(byte[] dst, …)`; the canonical-ABI
lower path called `Buffer.BlockCopy(value, 0, mem.Data, …)`; the lift
path read `_memory.Data[disc]` and passed `_memory.Data` to
`StringMarshal.LiftUtf8` and `ListMarshal.LiftPrim`. All AOORed in
NativePointer mode.

Routes every canonical-ABI host path through
`MemoryInstance.AsSpan(int, int)` (the existing mode-aware accessor)
so both `ManagedArray` and `NativePointer` backings work the same.
Helper signatures migrated from `byte[]` to `MemoryInstance`:

- `StringMarshal.LiftUtf8` / `LiftUtf16` / `LiftLatin1OrUtf16` / `CopyToGuest`
- `ListMarshal.LiftPrim<T>` / `LiftStringList` / `LiftStringListUtf16` / `CopyArrayToGuest<T>`
- `BulkHelpers.CopySegmentToMemory`
- `ModuleInit.CopyDataSegment` (interpreter active-segment install)

`PrimitiveStore` gains a reader sibling family — `ReadU8`, `ReadU16LE`,
`ReadU32LE`, `ReadI32LE` — used at IL emit time to decode disc bytes
and (ptr, len) header pairs. The scalar writer family
(`StoreI8` / `StoreU8` / `StoreI16` / … / `StoreBool`) now takes
`MemoryInstance` instead of `byte[]`.

Transpiled module class's `Memory` property changes type from
`byte[]` to `MemoryInstance`. Saved DLLs from v0.8.1 keep the old
shape; v0.8.2 generates the new shape. Consumers that read
`instance.Memory` directly need to update — `mem.Data` becomes
`mem.AsSpan(...)` for byte access.

IL emit sites in `DirectLinkedImportEmit` and `ComponentExportsEmit`
drop the `Ldfld MemoryInstance.Data` instruction at every helper
call site (the `MemoryInstance` is left on the stack instead) and
replace `BitConverter.ToInt32(byte[], int)` lookups with
`PrimitiveStore.ReadI32LE(MemoryInstance, int)`. Variant disc-byte
reads use `PrimitiveStore.ReadU8/U16/U32` instead of `Ldelem_U1`.

Test surface: new `data-segment-component` fixture (active data
segment + string return). `ComponentInstanceTests
.Component_data_segment_install_and_string_lift_under_storage`
covers the interpreter component path × `MemoryStorageMode`;
`ComponentTranspilerTests
.TranspileSingleModule_data_segment_install_and_lift_honor_storage`
covers `EmissionTarget × MemoryStorageMode` (4 cases). Both flavors
of guest-memory shape are exercised: segment install at module ctor
+ string lift on call.

Existing `StringMarshalTests` / `ListMarshalTests` updated to stage
inputs in a `MemoryInstance` rather than a bare `byte[]`.

Out of scope (separate gaps): `AtomicHelpers` (transpiler atomic
ops still pin `mem.Data` for `ref byte` semantics), MemoryInstance's
own `WriteInt32` / `WriteUtf8String` convenience methods (used by
WASI Preview1), and `Wacs.WASI.NN`'s `ExecContextExtensions`. Each
fails the grep'able `\.Data\b on MemoryInstance` invariant outside
the `MemoryInstance.cs` file in domains independent of canonical-ABI.

## WACS 0.13.1 / WACS.Cli 1.5.1 / WACS.Transpiler.Lib 0.8.1 / WACS.ComponentModel 0.3.1 — `--native-memory` honored on every component path

`--native-memory` was silently no-oped for component-mode runs:
the CLI pinned the storage mode but neither
`Wacs.ComponentModel.Runtime.ComponentInstance.Instantiate` (the
interpreter component path) nor
`ModuleClassGenerator.EmitMemoryArray` (the AotLinked emission)
read the pin. Components requesting more than the
`ManagedArray` ~2 GiB cap got `memory.grow → -1` regardless of the
flag.

The pin migrates from `Wacs.Transpiler.AOT.ModuleInit.CurrentMemoryStorage`
(only readable from the transpiler layer) to
`Wacs.Core.Runtime.AmbientRuntime.MemoryStorage` so every layer
above `Wacs.Core` shares one source of truth.

Reads added:
- `Wacs.ComponentModel.Runtime.ComponentInstance.Instantiate`
  (single-core and multi-core paths) constructs `RuntimeOptions`
  with `MemoryStorage = AmbientRuntime.MemoryStorage`.
- `Wacs.Transpiler.AOT.ModuleClassGenerator.EmitMemoryArray` emits
  `Ldsfld AmbientRuntime.MemoryStorage` before `Newobj` against
  the 2-arg `MemoryInstance(MemoryType, MemoryStorageMode)` ctor,
  so the runtime value of the pin reaches every memory the
  AotLinked path constructs.

Test surface: new `grow-memory-component` fixture (exports
`grow-big: func() -> s32` whose core does `(memory.grow 50000)`).
`Wacs.ComponentModel.Test.ComponentInstanceTests
.Component_memory_honors_AmbientRuntime_storage` exercises the
interpreter component path (returns -1 under ManagedArray, 1
under NativePointer);
`Wacs.Transpiler.Test.ComponentTranspilerTests
.TranspileSingleModule_memory_init_honors_AmbientRuntime_storage`
covers the cross-product of `EmissionTarget × MemoryStorageMode`.
`NativeMemory.AllocZeroed` is lazy-zero (calloc on Unix,
VirtualAlloc on Windows), so the 3 GiB virtual reservation does
not commit physical pages.

## WACS 0.13.0 / WACS.Cli 1.5.0 / WACS.Transpiler.Lib 0.8.0 / WACS.ComponentModel 0.3.0 / WACS.WASI.Preview2 0.4.0 / WACS.WASI.Preview1 0.13.0 / WACS.HostBindings.Abstractions 0.3.0 — Linear-memory storage modes, memory64, and component-model lift fixes

Lifts WACS's linear-memory backing to a host-selected mode and
plumbs that mode through every layer of the runtime, so the wasm32
4 GiB ceiling and memory64's 2^48 ceiling are both reachable.
Also closes two component-model lift correctness bugs that
surfaced under realistic host-side memory growth.

### Linear-memory storage modes

`MemoryInstance` carries two backings selected via
`RuntimeOptions.MemoryStorage`:

- **`ManagedArray`** (default): managed `byte[]` grown via
  `Array.Resize`. Capped at `Array.MaxLength` (~2 GiB).
- **`NativePointer`**: `byte* NativeBase` + `nuint NativeSize`
  allocated via `NativeMemory.AllocZeroed` (.NET 6+) or
  `Marshal.AllocHGlobal` + `InitBlockUnaligned` (legacy). Grow
  allocates a new buffer, `Buffer.MemoryCopy`s the live bytes,
  frees the old. Capped at `WasmMaxPages` (4 GiB) for memory32
  modules and `WasmMaxPages64` (2^48) for memory64.

New public surface on `MemoryInstance`:

- `StorageMode` (read-only) — which backing this instance uses.
- `byte* NativeBase` + `nuint NativeSize` — public for emit code.
- `nuint ByteLength` — authoritative byte length, both modes.
- `Span<byte> AsSpan(int offset, int length)` — mode-dispatched
  span access. The existing `[Range]` indexer also dispatches.
- `ref T RefAs<T>(int ea) where T : unmanaged` — mode-dispatched
  `ref T`. Atomic load/store/RMW routes through this so the same
  `Interlocked.*` / `Volatile.*` sites work on both backings.
- `IDisposable` — `Dispose` frees the native buffer in
  NativePointer mode, no-op in ManagedArray. A finalizer
  backstops native-mode leaks.

`RuntimeOptions.MemoryStorage` (default `ManagedArray`) flows from
`WasmRuntime.InstantiateModule` through to the `MemoryInstance`
ctor. Every interpreter memory-access site (the `MemSlice`
chokepoint covering 25+ `[OpHandler]` load/store/narrow/bulk
ops, the per-instruction memory load/store classes, bulk init/
copy/fill, SIMD v128, and atomics) dispatches through the
mode-aware surface. The transpiler's emit follows the same
pattern: `MemoryHelpers` and `BulkHelpers` take `MemoryInstance`
and dispatch per access; `MemoryEmitter` / `SimdEmitter` /
`BulkEmitter` pass `MemoryInstance` to the helpers directly;
`EmitMemorySize` uses `Call get_Size` instead of
`Ldfld Data; Ldlen`.

`ManagedArray` callers stay byte-stable; NativePointer is
covered by `MemoryInstanceNativeStorageTests` (allocation, grow
preservation + zero-fill, indexer + AsSpan parity, dispose
idempotency) and `MemoryNativePointerEndToEndTests` ([Theory]
cases running every memory op in both modes through real wasm
fixtures).

### memory64

memory64 modules (`(memory i64 N)`) execute end-to-end through
the interpreter and transpiler when paired with NativePointer.
Bounds checks use a wrap-safe unsigned form:

```csharp
if ((ulong)ea > (ulong)mem.ByteLength
    || (ulong)mem.ByteLength - (ulong)ea < (ulong)width)
    trap;
```

Negative `ea` casts to a huge ulong that fails the first
clause; `ea` near `ByteLength` fails via subtract-and-compare
without overflow risk. The check covers single-byte / narrow /
full-width loads and stores, SIMD, atomics, and bulk
init/copy/fill. `OpStack.PopAddr` no longer traps on negative —
memory64 addresses with the high bit set are valid wasm.
`InstTableGet` / `InstTableSet` also moved to unsigned compare
so table64 (`(table i64 …)`) indices behave correctly.

All four spec.test fixtures under `spec/test/core/memory64/`
pass on both the WAST and WAST-transpiled paths.

memory64 modules going through the AOT saved-DLL path
(`wacs aot --wasi`) work today only when the effective address
fits in int32 — the transpiler's emitted memory-op IL truncates
`(int)ea` at the AsSpan call site. Spec memory64 tests pass
because the test wat wraps to small `ea`; arbitrary >2 GiB
transpiled access does not. The interpreter and direct
`wacs run` paths are unaffected. `WacsHostMemory.AsSpan(int, int)`
is also int-bounded; host bindings reading >2 GiB views need a
future `MemoryHandle`-style API.

### `wacs run --native-memory`

`wacs run --native-memory model.wasm` (or
`--wasip2 --native-memory ...` for components) backs the
guest's linear memory with native-pointer storage. The flag
flips `RuntimeOptions.MemoryStorage` for the interpreter
`InstantiateModule` call and pins the static
`ModuleInit.CurrentMemoryStorage` (a new public field, default
`MemoryStorageMode.ManagedArray`) that the transpiler's
`InitializationHelper` reads when constructing transpiled
module classes. `ExecuteSingleCore` and `ExecuteComponent`
restore the prior values on exit so subsequent in-process
callers (test harnesses, library hosts) see the original mode.

### `WacsHostMemory` mode-aware

The host-binding ABI carries a NativePointer-mode case alongside
the managed `byte[]` case. Wasip1 hosts running with
`MemoryStorageMode.NativePointer` produce a `WacsHostMemory`
that dispatches reads and writes against native memory.

`Wacs.HostBindings.Abstractions.WacsHostMemory`:

- New `WacsHostMemory(IntPtr nativeBase, int length)` ctor.
  The struct tracks both backings (`byte[]? _data` +
  `IntPtr _nativeBase`) and dispatches every accessor through
  a null-check on `_data` — JIT inlines to a single branch per
  access.
- New `IsNative` property — true when the view is over native
  memory.
- All accessors (`ReadByte`/`WriteByte`/`AsSpan`/`ReadInt32LE`/
  `WriteInt32LE`/`ReadInt64LE`/`WriteInt64LE`/`Contains`/
  `WriteUtf8String`/`ReadUtf8String`/`ReadStruct`/`ReadStructs`/
  `WriteStruct`) work in either mode.
- `Data` getter still returns a `byte[]` for back-compat — but
  in NativePointer mode it returns `Array.Empty<byte>()`.
  Legacy callers that reach for `.Data` directly fail loud
  (AOOR on first index) instead of silently zero-reading; they
  should migrate to `AsSpan`.

`Wacs.WASI.Preview1.Clock.WacsHost` (the Preview1 ExecContext →
WacsHostMemory adapter) branches by `MemoryInstance.StorageMode`.
NativePointer-backed memories take the `(IntPtr, int)` ctor
with `(IntPtr)mem.NativeBase` and
`min(NativeSize, int.MaxValue)` length.

`Wacs.HostBindings.Test`: 14 tests (was 8). Six new cases
allocate via `NativeMemory.AllocZeroed`, exercise the
accessors, and free the buffer.

### wasip2 host bindings

The wasip2 host-binding stack threads `MemoryInstance` instead
of raw `byte[]` everywhere — about 30 helpers in `MemoryReader`
/ `MemoryWriter`, the `ExecContextExtensions` shortcuts, ~150
callsites across `SocketsBindings`, `FilesystemBindings`,
`HttpTypes`, `Cli`, `Clocks`, `Io`, and `Random`, plus 39
private `Write*` helpers in those binding files. Every read and
write goes through the mode-dispatching `mem.AsSpan(...)` /
`mem.RefAs<T>(...)` / `mem.ByteLength` surface, so a wasip2
binding works against either backing without per-binding
awareness.

API changes:

- `MemoryReader.{ReadUtf8String, ReadByteArray, ReadByteArrayList,
   ReadI32LE, ReadU16LE, ReadU32LE, ReadU64LE}`: `byte[] memory`
  → `MemoryInstance memory`.
- `MemoryWriter.{WriteI32LE, WriteU16LE, WriteU32LE, WriteU64LE,
   WritePrimitiveLE, WriteResultUnitOk, ZeroRange}`: same.
- `MemoryWriter.WriteUtf8StringAllocated` / `WriteOptionString`:
  `Func<byte[]> getMemory` → `MemoryInstance memory`. Callers
  no longer need to model the post-`cabi_realloc` re-fetch —
  `mem.AsSpan` reads the live backing on each access.
- `ExecContextExtensions.Memory(this ExecContext ctx)`: returns
  `MemoryInstance`, not `byte[]`. Callers passing `ctx.Memory`
  as a method group invoke it as `ctx.Memory()`.

Per-binding-file changes follow a uniform pattern: `mem[ptr]`
→ `mem.AsSpan(ptr, 1)[0]`; `Array.Copy(src, X, mem, Y, len)`
→ `new ReadOnlySpan<byte>(src, X, len).CopyTo(mem.AsSpan(Y, len))`;
`Encoding.UTF8.GetString(mem, ptr, len)` →
`Encoding.UTF8.GetString(mem.AsSpan(ptr, len))`.

`ErrorCodeEncoderTests`'s `BumpAllocator` test fixture wraps a
real `MemoryInstance` (1-page) instead of a raw `byte[]`;
assertions go through a thin indexer/Span helper.

### Component-model lift fixes

Two correctness bugs in `DirectLinkedImportEmit`:

**Records with `option<X>` fields.**
`wasi:filesystem/types.descriptor.stat` returns
`result<descriptor-stat, error-code>`, where descriptor-stat is
`record { type, link-count, size, opt<datetime>×3 }`. The
predicate path rejected this record because
`IsRecordOfPrimitives` walked fields with the non-resolver
`IsFlatField` and bailed on the option fields; direct-link emit
fell back to the `IImports` proxy and the proxy returned a
default-zero `DescriptorStat`. Resolver-aware
`IsFlatField(t, resolver)` now accepts `Option<X>` whenever
`IsAggregateReturnSupported(t, resolver)` recognizes the
Option's wire form. `IsAggregateReturnSupported`'s record +
tuple branches use the resolver-aware predicate so option
fields cascade through. `EmitTupleOrRecordFieldStore`
dispatches Option fields to `EmitOptionStoreAt` with a per-
field base-address local. `MaxFieldAlign`, `AlignOfFlatField`,
`SizeOfFlatField`, `SizeOf` pick up Option-aware overloads so
per-field offsets align on the inner type's `MaxAlignOf`.

E2E coverage: new `E2E_DescriptorStat_RecordWithOptionFields`
exercises a stub `IDescriptor.Stat()` returning a known `Size`
through the `wasi-fs-stat-component` fixture.

**`cabi_realloc`-driven `memory.grow` invalidates captured byte[].**
`MemoryInstance.Grow` does `Array.Resize(ref Data, …)`, which
reallocates the backing `byte[]`. Every helper in
`PrimitiveStore` captured `byte[] dest` BEFORE calling
`cabi_realloc`, so the post-realloc copy targeted the stale
(pre-grow) array's `int Length`, throwing AOOR for any
allocation that crossed a page boundary. Rust std hid the trap
behind "out of memory" because `fs::read` loops on `read(buf)`
past 24 KiB.

Every cabi_realloc-using helper (`StoreString` /
`StoreStringUtf16` / `StoreStringLatin1OrUtf16` /
`StoreByteArray` / `StorePrimitiveArray<T>` /
`StoreByteArrayList` / `StorePrimArrayList<T>` /
`StoreStringList` / `StoreListOfStringList` /
`StoreListOfByteArrayList`) now takes `MemoryInstance mem`
instead of `byte[] dest` and reads `mem.Data` per access.
`mem.Data` is read AFTER each cabi_realloc, so writes target
the post-grow array. The fixed-width primitive helpers
(`StoreI8` / `StoreU8` / … / `StoreBool`) still take
`byte[] dest` — they have no cabi_realloc and no grow risk.

`DirectLinkedImportEmit`'s emit sites split into two prefixes:
variable-length helpers receive `MemoryInstance`, fixed-width
helpers receive `byte[] dest` as before. The split runs through
the top-level dispatch, `EmitTupleOrRecordFieldStore`,
`EmitOptionStoreAt`, `EmitVariantStoreAt`, and
`EmitResultArmStore`.

Regression coverage: new `PrimitiveStoreGrowTests` (3 cases):
byte[] across grow, string across grow, byte[][] with
mid-iteration grow. The cabi_realloc lambda calls
`mem.Grow(...)` to mirror Rust std's growing realloc.

### Tests

| Suite | Total | Notes |
|---|---|---|
| Wacs.Core | 394 | +31 (allocation/grow units, [Theory] mode pairs, memory64 fixtures, atomic round-trips) |
| Wacs.Transpiler | 752 | +1 e2e (DescriptorStat record-with-options) |
| Wacs.ComponentModel | 350 | +3 (PrimitiveStoreGrowTests) |
| Wacs.WASI.Preview2 | 189 | unchanged (BumpAllocator fixture rewritten over MemoryInstance) |
| Wacs.WASI.Preview1 | 72 | unchanged |
| Wacs.HostBindings | 14 | +6 (NativePointer-mode WacsHostMemory accessors) |
| Spec.Test | 770/772 | +8 (4 memory64 + 4 table64 fixtures) |

## WACS.Transpiler.Lib 0.7.3 / WACS.Cli 1.4.1 / WACS.WASI.Preview2 0.3.1 / WACS.WASI.Preview2.DependencyInjection 0.1.1 — gap 9: preopens reach the wasip2 transpiler engine

Closes the gap that prevented `wacs run --wasip2 -d models repro.wasm`
(reading `/models/x.txt`) from succeeding under the transpiler
engine. The reproducer now runs end-to-end:

```
$ wacs run --wasip2 -d models preopen-repro.wasm
got: hi
```

Two layered fixes:

1. **WACS.Transpiler.Lib (`DirectLinkedImportEmit`)**: extends the
   per-field aggregate emit to recognize tuple/record fields that
   are resource interfaces (`own<R>`) alongside the existing
   primitive / string / byte[] cases. New shape covered:
   `list<tuple<own<R>, string>>` (the gap-9 reproducer's
   `wasi:filesystem/preopens.get-directories` return) and the
   broader env/args/headers/accept "list of (resource, label)"
   shape class. Per-element wire layout: handle@+0 (i32, 4B) +
   string-ptr@+4 (i32, 4B) + string-len@+8 (i32, 4B) for the
   gap-9 shape; per-element store dispatches per-field —
   `ctx.Resources.AllocateResource(typeof(IRes), value) +
   StoreI32` for `own<R>`, `cabi_realloc + StoreString` for
   strings, primitive `StoreXxx` for primitives. Resolver-aware
   variants of the predicates (`IsTupleOfFlatFields`,
   `IsRecordOfFlatFields`, `SizeOfFlatField` overload,
   `IsFlatField` overload) keep the existing primitive-only
   paths byte-stable; only the list-of-aggregate path consults
   the resolver.

2. **WACS.WASI.Preview2.DependencyInjection
   (`WasiPreview2RuntimeScope`)**: new one-shot owner of the DI
   scope that binds the wasip2 host package against the
   transpiler runtime. Auto-detects WASI.NN.DI and registers the
   composite `WasiPreview2NNBundle` whenever both packages are
   on the load path — required because the transpiler emits its
   direct-link IL against the composite type at transpile time;
   handing back the base bundle here trips
   `InvalidCastException` at the first import call. Embedders
   that want preopens hand them in via the scope's `preopens`
   parameter instead of re-implementing `IPreopens` +
   `services.AddSingleton`.

3. **WACS.WASI.Preview2 (`Preopens`)**: restored
   `Preopens(IEnumerable<(string hostPath, string guestPath)>)`
   ctor so the scope can build a `Preopens` instance from any
   iterable mount-pair source.

4. **WACS.Cli (`RunHandler`)**: the `--dir` flag now accepts the
   wasmtime-style `host::guest` mount-pair syntax in addition to
   the bare-path form. Validation checks the host-path side
   only. The wasip2 path constructs a `WasiPreview2RuntimeScope`
   inside `configureImports` so the bundle the transpiler
   receives is the same one the run uses.

5. **WACS.Transpiler.Lib (`ComponentMainHost.Run`)**: accepts
   optional `prebuiltBundle` / `prebuiltResources` parameters so
   the run path can hand off the scope's bundle/resources
   directly. Saved-dll `Program.Main` IL keeps the old reflective
   fallback (passes `null` / `null`).

Verification:
- 750/751 (1 SKIP) Wacs.Transpiler tests pass — including a new
  `E2E_Preopens_GetDirectories_ListResourceStringTuple` E2E test
  that transpiles `Spec.Test/components/fixtures/wasi-preopens-component`
  with a 3-entry `IPreopens` stub and verifies `count` returns 3.
- 347 ComponentModel + 189 Preview2 + 18 WASI.NN + 72 Preview1 +
  355 Core + 13 Bindgen + 8 HostBindings tests pass.
- End-to-end: `wacs run --wasip2 -d models repro.wasm` reads
  `/models/x.txt` cleanly; hello-wasip2 unchanged.

## Spec.Test fixtures — WASI 0.2.3 → 0.2.8 bump

Bumps the `Spec.Test/components/wasi-cli` submodule pointer from
v0.2.3 to v0.2.8, and propagates the version bump across every
fixture and test asserton:

- 168 fixture WIT files: `@0.2.3` → `@0.2.8` in package /
  use / import declarations.
- 68 fixture WAT files: `@0.2.3` import strings updated.
- 101 committed `<fixture>/wasm/<base>.component.wasm` binaries
  regenerated via `Spec.Test/components/build_fixtures.sh`.
- Hello-world reference (12 files, 9 with `v0_2_3`-baked
  filenames) regenerated via
  `Spec.Test/components/build_hello_world_reference.sh` with
  `wit-bindgen-cli 0.30.0` (the pin).
- 7 test C# files: `@0.2.3` / `0.2.3` / `v0_2_3` → `@0.2.8` /
  `0.2.8` / `v0_2_8` in fixture-loading assertions, package-name
  constructor calls, and reference-filename strings.

Net delta: 324 files changed, 398+ / 1777-. The size asymmetry is
the regenerated wasm binaries — `wasm-tools` 1.221's encoder packs
slightly tighter than the originals were (no semantic difference;
the .wat / .wit are byte-stable input → byte-stable output for any
given tool version).

The runtime-side WACS.WASI.Preview2 was already at 0.2.8 (PR #120);
this brings the test fixtures into alignment, retiring the
"deliberately decoupled" caveat in the README.

## [WACS.WASI.Preview2 0.3.0] — Bundled WIT bumped to WASI 0.2.8

Refreshes the vendored WIT tree under `Wacs.WASI.Preview2/wit/`
from upstream `WebAssembly/wasi-cli@v0.2.8` (latest stable patch,
released after v0.2.3 with zero ABI changes — only doc clarifications
and version-string bumps in `use` clauses). All hardcoded
`wasi:*@0.2.3` strings in the per-subsystem `*Bindings.cs` files
update in lockstep.

The 0.2.3 → 0.2.8 delta is purely cosmetic at the wire level — the
Component Model spec stabilizes minor revisions of WASI, so guests
compiled against any 0.2.x version bind to this version-tolerantly.
What changes: the version annotation in error messages, the strings
`wacs inspect --imports` reports, and the canonical `[WitSource]`
package identity the source-gen emits.

The `Spec.Test/components/wasi-cli` submodule and the test fixtures
under `Spec.Test/components/fixtures/` stay pinned at v0.2.3 — they
exercise the loader/emitter against a specific frozen version. The
two coordinates are deliberately decoupled.

## [WACS.Core 0.12.2] — Version-tolerant GetBoundEntity

Mirrors PR #119's `HostPackageResolver.TryResolve` fallback for the
interpreter path: when an exact `(module, entity)` lookup misses,
strip the trailing `@<version>` and try again, then fall back to an
O(n) scan over all keys for any matching the same stripped module
+ entity. Lets guests built against newer WASI patch revisions
bind to host packages registered against older ones (or vice
versa), since wasm Component Model treats minor revisions of WASI
as ABI-stable.

## [WACS.ComponentModel 0.2.0] — WIT parser accepts pre-release semver tags

`WitLexer` now emits dedicated `Dash` and `Plus` tokens (only when
not part of `->` or kebab-case identifiers). `WitParser.ParseSemver`
consumes them as the optional pre-release / build suffixes per
semver, populating `WitVersion.Prerelease` / `Build`.

Closes the `wasi:nn@0.2.0-rc-2024-10-28` (and any future rc-tagged)
WIT package's "unexpected character '-'" failure path. Unblocks the
SourceGen-driven host-interface emission for wasi-nn (see
WACS.WASI.NN 0.3.0).

## [WACS.WASI.NN.DependencyInjection 0.2.0] — Concrete resource impls

Replaces the GraphStub / ErrorStub placeholders with real resource
implementations (`Tensor`, `Graph`, `GraphExecutionContext`, `Error`)
of the source-gen interfaces. Each class has a parameterless ctor
so the canonical-ABI resource-construct lift can
`Activator.CreateInstance` it; instance methods either route to the
backend SPI (`Graph` → `IBackendGraph`, `GraphExecutionContext` →
`IBackendContext`) or hold pure state (`Tensor`, `Error`).

`GraphFuncsImpl.Load` / `LoadByName` now return real `Graph`
instances; `Graph.InitExecutionContext` mints a real
`GraphExecutionContext`; `compute` bridges between the wasi-nn
resource handles and the backend SPI's `NamedTensor` values
(copying output bytes so the resource handle owns its data
independent of the next compute).

Smoke tests in `Wacs.WASI.NN.Test/DependencyInjectionResourceTests`
cover the round-trip + double-construction + access-before-construct
guards.

The remaining piece for the SLM workload's transpiler-direct-link
path is multi-bundle wiring in `ComponentMainHost`: the existing
ctor-arity-based emit assumes a single `object hostBundle` slot,
so a component importing both `wasi:cli/*` (Preview2) and
`wasi:nn/*` can't yet have both bundles wired through one slot.
The resolver's `bundleType` parameter takes a single type today;
extending to a composite bundle (or `Type[]`) is the open work.

## [WACS.WASI.NN.DependencyInjection 0.1.0] — WasiNNBundle scaffolding

New package mirroring `Wacs.WASI.Preview2.DependencyInjection`. Ships
the `WasiNNBundle` that the transpiler's `HostPackageResolver`
direct-links wasi-nn's stateless `graph.load` /
`graph.load-by-name` against, plus
`services.AddWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new
OnnxBackend()))` for DI registration.

`GraphFuncsImpl` is the concrete `Nn.IGraphFuncs` implementation —
delegates to the configured `WasiNNConfiguration` backends (same
registry the interpreter binding consults). `Result<IGraph,
IError>` returns route through `GraphStub` / `ErrorStub`
placeholders that satisfy the type contract.

The resource-method-direct-link (`graph.init-execution-context`,
`tensor.constructor`, `inference.compute`) is the next deferred
chunk — the `GraphStub.InitExecutionContext` returns
`unsupported-operation` with a clear "wait for the resource-impl
PR" message rather than silently mis-dispatching. Resource methods
on the interpreter `BindToRuntime` path continue to work via the
hand-written `WitBindings` today.

## [WACS.WASI.NN 0.3.0] — Source-gen [WitSource] interfaces

Wires `Wacs.ComponentModel.Bindgen.SourceGen` against
`wit/wasi-nn.wit`, producing `[WitSource]`-decorated interfaces
under `Wacs.WASI.NN.Nn.{Errors, Graph, Inference, Tensor}`. The
transpiler's `HostPackageResolver` discovers these to direct-link
component-model wasi-nn imports — symmetric with how
`Wacs.WASI.Preview2` wires its hand-migrated subsystems.

The hand-written `WitBindings` continues to own the interpreter-
side `BindHostFunction` wiring; the generated interfaces feed the
transpiler-direct-link path on the wasip2 component path.

## [WACS.WASI.Preview2 0.2.0] — WasiPreview2Host composite + UseWasiPreview2 extension

`WasiPreview2Host` is the interpreter-side composite that wires every
sub-binding (random, clocks, io, streams, cli, filesystem, optionally
sockets + http) onto a `WasmRuntime` from one shared
`ResourceContext`. Symmetric with `WasiNNHost` — interpreter
consumers no longer thread the resource context through eight
separate `BindToRuntime` calls.

`runtime.UseWasiPreview2(b => b.WithStdout(...).EnableSockets())` is
the matching one-liner. Default posture matches Wasmtime: host
clocks/random/cli stdio + sandboxed-no-fs are wired, sockets and http
require explicit opt-in. The
`Wacs.WASI.Preview2.DependencyInjection` bundle path remains the
perf-optimized (transpiler direct-link) wiring.

## [WACS.Cli 1.4.0] — Component-mode ergonomics: auto-dispatch + --bind + --wasi-nn

`wacs run --wasip2 my.component.wasm` now starts a stock command
component without `--call`. The CLI looks for the canonical
`wasi:cli/run@<version>#run` export (matched via the new
`[WasmName]` round-trip attribute) and dispatches it automatically;
falls back to `_start`, then to a helpful error listing the
available exports. Aligns with wasmtime / jco / wasmer behavior for
stock command components.

`--bind <asm>` is now honored on the component paths
(`ExecuteComponent` + `ExecuteComponentTranspiled`), not just on the
core paths. Custom IBindable host packages can satisfy component
imports the same way they do for core modules. On the
component-transpiler path bindings run AFTER the default trap-stub
registration so `--bind` overrides cover the imports they care about.
`--bind` accepts both file paths and assembly names (resolves via
`Assembly.LoadFrom` / `Assembly.Load`, mirroring `--host-package`).

`--wasi-nn` shorthand: equivalent to
`--bind Wacs.WASI.NN.OnnxRuntime`. The DLL is bundled with the CLI
(via `ExcludeAssets="compile"` like Preview2) so it resolves out of
the box. For other backends (MLNet, LlamaSharp), pass the package
name through `--bind` directly.

## [WACS.WASI.NN 0.2.0] — IBindable + UseWasiNN extension

`WasiNNHost` now implements `IBindable` (it already exposed
`BindToRuntime(WasmRuntime)` — declaring the interface is
truth-in-advertising). Lets it ride the `--bind` discovery path.

New `runtime.UseWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()))`
extension method. Replaces the
config → host → BindToRuntime sequence with the same shape we want
across the WASI host family.

## [WACS.WASI.Threads 0.2.0] — IBindable polish for symmetry

- Tagged `[assembly: WasiHostPackage]` so
  `runtime.AutoDiscoverHostPackages()` finds it alongside the
  other tagged WASI packages.
- New `runtime.UseWasiThreads()` extension method — one-liner
  symmetric with `UseWasiPreview2` / `UseWasiNN`.
- New `--wasi-threads` CLI flag (shorthand for
  `--bind Wacs.WASI.Threads`); the package is bundled with the
  CLI so the flag resolves out-of-box.

`WasiThreads` already implemented `IBindable` with a parameterless
ctor, so `--bind Wacs.WASI.Threads` worked before this change.
This is consistency polish across the WASI host family.

## [WACS.WASI.NN.MLNet 0.2.0] — Parameterless WasiNNMLNetBindable for --bind

Adapter exposing a parameterless ctor that pre-registers the
ML.NET-flavored ONNX backend. `--bind Wacs.WASI.NN.MLNet` activates
it via `BindingLoader`, identical shape to the OnnxRuntime adapter.
Tagged `[assembly: WasiHostPackage]` for `AutoDiscoverHostPackages`.

## [WACS.WASI.NN.LlamaSharp 0.2.0] — Parameterless WasiNNLlamaSharpBindable

Adapter for the GGUF / LlamaSharp backend with environment-variable-
driven name registry. Set `WACS_WASINN_GGUF_DIR=/path/to/models` and
every `*.gguf` file in that directory is registered under its
filename-sans-extension. Empty registry is fine — guests calling
`load-by-name` get `NotFound` rather than a trap.

`--bind Wacs.WASI.NN.LlamaSharp` activates it for guests using
`graph-encoding.ggml`. For richer registries (HF cache scan,
per-model `ModelParams`, custom paths), embedders should construct
`LlamaSharpBackend` directly via `runtime.UseWasiNN(b => b.AddBackend(...))`.

Tagged `[assembly: WasiHostPackage]`.

## [WACS.WASI.NN.OnnxRuntime 0.2.0] — Parameterless WasiNNOnnxBindable for --bind

Adapter exposing a parameterless ctor that pre-registers the ONNX
backend. `BindingLoader.LoadFromAssembly` activates it
automatically, so `wacs run my.wasm --wasip2 --bind Wacs.WASI.NN.OnnxRuntime`
(or the new `--wasi-nn` shorthand) is the whole story for stock
ONNX components — no per-consumer shim DLL.

## [WACS.HostBindings.Abstractions 0.2.0] — `[WasmName]` + `[WasiHostPackage]`

`[WasmName(string)]` carries the original wasm name on
auto-generated IExports/IImports methods. Round-trips a sanitized
CLR identifier (`wasi_cli_run_0_2_0_run`) back to its wasm form
(`wasi:cli/run@0.2.0#run`) for dispatch and diagnostics. Stamped
automatically by the WACS interface generator; hand-written types
implementing those interfaces don't need to apply it.

`[assembly: WasiHostPackage]` flags an assembly as auto-discoverable
by the runtime's host-package scan
(`runtime.AutoDiscoverHostPackages()`). Pairs with
`runtime.UseHostPackages(name1, name2, …)` for the explicit-list
shape. Either path activates every `IBindable` with a parameterless
ctor that the tagged assembly ships.

## [WACS.Transpiler.Lib 0.7.2] — `[WasmName]` emit, ComponentMainHost auto-resolve, BindingLoader name resolution

`InterfaceGenerator` stamps `[WasmName]` on every IExports / IImports
method, preserving the original wasm name through CLR-identifier
sanitization. Survives Reflection.Emit and PersistedAssemblyBuilder
paths; still dropped by Lokad.ILPack saved-dll output (a
follow-up).

`ComponentMainHost.Run` now accepts a null `exportName` and
auto-resolves `wasi:cli/run@<version>#run` via `[WasmName]` before
falling back to `_start`. Used by the `wacs run --wasip2`
component-command auto-dispatch path.

`BindingLoader.LoadFromAssembly(string)` now accepts either a file
path (`Assembly.LoadFrom`) or an assembly name (`Assembly.Load`),
matching `ResolveHostPackages` so `--bind` and `--host-package` have
identical resolution semantics.

New `WasmRuntime.UseHostPackages(name1, name2, …)` and
`WasmRuntime.AutoDiscoverHostPackages()` extension methods: the
explicit-list and AppDomain-scan shapes for ergonomic IBindable
wire-up. The scan uses the new `[WasiHostPackage]` assembly
marker.

## [WACS.Transpiler.Lib 0.7.1] — Re-instantiation restores dropped active data segments

Each Module instance's ctor copies active data segments from the
process-wide `ModuleInit` registry, then drops them per spec §4.5.4 so
later `memory.init` calls observe an empty segment. The drop turns the
dict entry into an empty array (not a removal) — fine for instance 1,
broken for instance 2: `CopyDataSegment` reads the empty entry and
memory comes up zeroed. Surfaces whenever a transpiled Module class
gets multiple `Activator.CreateInstance` calls in the same process.

`InitializationHelper.InitializeCore` step 4a now restores from
`ModuleInitData.SavedDataSegments` (already populated in step 6 of the
first init) when the live registry entry is empty. Adds a
`ModuleInit.RestoreDataSegment` overwriting variant —
`RegisterDataSegmentAt` is no-op-on-collision by design (cross-process
AotLinked path) and would skip the empty-entry case otherwise.

The "multi-memory bug" investigation that surfaced this: the
interpreter's binary parser handles multi-memory + active-data-with
-explicit-memidx (DataFlags=2) correctly — verified against
hand-encoded bytes byte-identical to wat2wasm output, plus all 32
multi-memory spec wast fixtures. The actual gap was on the transpiler
side and not multi-memory-specific. The new
`AotLinkedSupportsActiveDataWithExplicitMemIdx` test exercises both
the per-memidx routing and the re-instantiation path; the stale
"interpreter gap" comment in `AotLinkedSupportsMultiMemory` is gone.

## [WACS.Cli 1.3.0 + WACS.Transpiler.Lib 0.7.0] — PersistedAssemblyBuilder, RVA-mapped data, EmissionTarget.Auto

The transpiler retires `Lokad.ILPack 0.3.1` for the .NET 9+
[`PersistedAssemblyBuilder`](https://learn.microsoft.com/dotnet/fundamentals/runtime-libraries/system-reflection-emit-persistedassemblybuilder).
Lokad NRE'd on `Ldtoken` of any field created via
`DefineInitializedData`, which had been blocking RVA-mapped data
segments end-to-end. With PAB, that path works.

### RVA-mapped WASM data segments

WASM data segment bytes are now stored as RVA-mapped initialized data
in the emitted PE — bytes live in the `.sdata`/`.rdata` section,
demand-paged from disk by the OS loader, surfaced zero-copy as
`ReadOnlySpan<byte>` via `RuntimeHelpers.CreateSpan<byte>`. The
serialized codec blob (`__WACSInit.Data`) that bridges
saved-and-reloaded modules' empty registry state is RVA-mapped too.
Net effect: the compressed-segment + base64-in-`#US` path the prior
transpiler used is gone. Smaller PEs (~62.5% smaller blob storage on
data-segment-heavy modules), cold start that doesn't pay for a
`Convert.FromBase64String` over the whole codec.

### `EmissionTarget.Auto` is the new default

`AotLinked` emission inlines the `ThinContext` ctor as IL constants
and skips the codec stack entirely. v0.5 introduced it as an opt-in;
v0.7 widens its supported envelope (multi-result indirect dispatch,
multi-memory, exception tags, passive data + element segments,
imported functions) and turns on **`EmissionTarget.Auto`**, which
promotes feasible modules to `AotLinked` and falls back to `Standard`
for shapes outside the conservative envelope. Cuts first-trial cold
start by ~50% on promoted modules. Consumers that need codec
semantics (cross-process registry hint, etc.) can pin
`EmissionTarget.Standard`; consumers willing to fail loudly on
unsupported shapes can pin `EmissionTarget.AotLinked`.

### `ImportDispatcher` throws on missing handlers

Previously, `ImportDispatcher.Create` would silently default-return
when a wasm import had no matching `IImports` member; v0.7 throws
`InvalidOperationException` by default so missing wires fail at
construction time. The lenient default-return behavior is still
available via `ImportDispatcher.Create(..., lenient: true)`, which
`ComponentMainHost` keeps using because component-mode imports often
land via a different code path.

### `wacs aot` cross-csproj fix

`wacs aot` produces a host csproj that statically references the
transpiled `.dll`. PAB stamps the saved DLL's corelib AssemblyRef as
`System.Private.CoreLib` (the runtime-impl identity), but the C#
compiler resolves base types from the ref-pack `System.Runtime` —
without intervention, the host csproj trips CS0012 at compile time.
A new
[`CoreLibAssemblyRefRewriter`](Wacs.Transpiler.Lib/AOT/CoreLibAssemblyRefRewriter.cs)
post-processes a copy of the baked bytes at `SaveAssembly` time,
swapping the AssemblyRef name + PKT in place; type-forwards keep
runtime semantics intact. The rewriter file documents the rationale,
the byte-level edits, the one known limitation (generic-instantiation
FieldRefs across the renamed boundary in isolated ALCs), and the
two upstream conditions under which the hack can be deleted.

`Wacs.Transpiler.Lib` and `Wacs.Console` move to `net9.0`. `Wacs.Core`
remains `netstandard2.1` so embedders on Unity / Godot / older .NET
keep working unchanged.

## [WACS 0.12.1 + WACS.WASI.Preview1 0.12.0] — WAT parser parity, wasm-3.0 spec tip, wasi-testsuite Phase 4

### Wacs.Core 0.12.0 — in-process WAT/WAST parser at full parity

Every wast in the WebAssembly spec testsuite (SIMD, GC, relaxed-SIMD,
hex-float edge cases) round-trips identically through both the binary
and the in-process WAT/WAST pipelines. CI no longer shells out to
`wasm-tools` / `wast2json` to convert .wast fixtures to binary before
running them.

Highlights:

- WAT parser: full 237/237 instruction-dispatch coverage (GC + SIMD).
- Hex-float precision matches the binary parser bit-for-bit.
- Inline `(table funcref (elem $f …))` aligns with wabt.
- WAST runner: `assert_trap (module …)` + the various `ExnNN` shapes
  pass through the same module-instantiation hooks as binary fixtures.
- `BinaryModuleParser` no longer carries cross-parse static state.

### Wacs.Core 0.12.1 — wasm-3.0 spec submodule tip d7aada5

Tracks the upstream `WebAssembly/spec` submodule to commit `d7aada5`,
picking up:

- Inclusive memory page-count limit (PRs #105/#106/#108).
- Tail-call to imported (host) functions (#1872).
- `array.new_data` bounds (#1881).
- Malformed memop reserved bits (#1886/#1936).
- table64 unsigned u64 literal parsing + K dispatch (#104).
- `(module definition …)` validate-only support.
- u32 offset enforcement on memory32 load/store.
- `;;` line-comment CR termination.

### WACS.WASI.Preview1 0.12.0 — wasi-testsuite Phase 4 (43 → 67 of 72)

Lifts 23 fixtures across six subphases (PR #101):

- Phase 4.1 — symlink behavior (lifts 6 fixtures).
- Phase 4.2 — trailing-slash semantics + `path_link` no-follow.
- Phase 4.3 — `fd_readdir` synthesizes `.` and `..`.
- Phase 4.4 — fd-on-dir + preopen errno alignment (4 fixtures).
- Phase 4.5 — rights / lifecycle / timestamp fixes (2 fixtures).
- Phase 4.7 — directory rights split + `path_open` hardening.

## [WACS 0.11.0 + WACS.Transpiler.Lib 0.6.0] — Branch hinting

Wires the WebAssembly [Branch Hinting](https://github.com/WebAssembly/branch-hinting)
proposal end-to-end:

- **WACS 0.11.0** parses the `metadata.code.branch_hint` custom
  section into `Module.BranchHints` (a `(funcIdx → byte_offset →
  BranchHint)` map). The full payload is retained verbatim including
  the length-prefixed data vector so future revisions to the
  proposal can extend the hint encoding without a parser change.
  Every parsed instruction inside a function body now carries its
  body-relative byte offset on `InstructionBase.ByteOffsetInFunc` —
  the lookup key against the hint map.

- **WACS.Transpiler.Lib 0.6.0** consumes the hints in two emission
  shapes:
    * `if`-with-`else` hint=unlikely → `EmitIf` swaps the test
      (`Brtrue then_label` instead of `Brfalse else_label`) and
      emits the else-arm as the hot fall-through with the then-arm
      as the cold side-jump.
    * `if`-without-`else` hint=unlikely → new `_coldTailEmissions`
      mechanism on `FunctionCodegen` lifts the body out of the
      linear flow entirely. Hot path is `Brtrue cold_label;
      <fall-through>`; cold body is emitted between the function
      body's terminator and the funcEndLabel mark, with a back-jump
      to the if's endLabel to resume normal flow. Non-reducible CFG;
      RyuJIT and ILC handle it.

  Optimistic per design: the IL expresses the hint via ordering and
  branch sense. We don't claim downstream JIT/AOT honors it beyond
  what its own block-layout pass already does (RyuJIT tier-1 will
  eventually overrule us with profile data anyway). The bet pays
  off most for `wacs aot` / NativeAOT cold paths where there's no
  profile data to rely on.

The README's Branch Hinting feature row updates from "Custom section
ignored" to describe the new transpiler integration.

Validation is intentionally permissive ("optimistic"): the parser
rejects duplicate `(funcidx, offset)` entries and out-of-range
funcidx, but does NOT cross-validate that each hint's target offset
lands on an `if`/`br_if` instruction. Consumers can re-check at
use site if they need stricter semantics.

## [WACS.Cli 1.2.0 + WACS.WASI.Preview1 0.11.0 + WACS.HostBindings.* 0.1.0 + WACS.Transpiler.Lib 0.5.0] — `wacs aot` end-to-end + WASI rename

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
