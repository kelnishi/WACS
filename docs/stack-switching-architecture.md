# Stack Switching — architecture & coverage

WACS implements the [WebAssembly Stack Switching proposal][prop]
(typed continuations: `cont.new` / `cont.bind` / `suspend` /
`resume` / `resume_throw` / `switch`). The runtime lives in
`Wacs.Core` and underpins the Component-Model async dispatcher
documented in [wasip3-architecture.md](./wasip3-architecture.md).

[prop]: https://github.com/WebAssembly/stack-switching

## Layers

```
                          ┌────────────────────────────────────┐
                          │  Component-Model async dispatcher  │
                          │  (Wacs.ComponentModel/Async/*)     │
                          └─────────────────┬──────────────────┘
                                            │ calls IContinuationContext
                                            ▼
┌───────────────────────────────────────────────────────────────┐
│ Runtime concurrency layer  Wacs.Core/Runtime/Concurrency/     │
│                                                               │
│   ContInstance ─ runtime continuation object (IGcRef)         │
│   ContinuationStore ─ module-local allocator (standalone mode)│
│   IContinuationContext ─ host hook (mixed-mode = ExecContext, │
│                          standalone-mode = ThinContext)       │
│   ResumeHandlerFrame ─ on-stack frame holding installed       │
│                        on-tag handlers for the current resume │
│   IConcurrencyPolicy ─ pluggable host concurrency strategy    │
│   StackSwitchingHelpers ─ static entry points the transpiler  │
│                            emits CIL `call`s to               │
│   SuspensionException ─ control-flow throw used to unwind     │
│                          to the matching `resume` handler     │
└───────────────────────────────────────────────────────────────┘
                                            ▲
                                            │ interpreter ↔ helpers
                                            │ share the same surface
                                            ▼
┌───────────────────────────────────────────────────────────────┐
│ Interpreter instructions  Wacs.Core/Instructions/             │
│                                                               │
│   StackSwitching.cs ─ InstContNew / InstContBind / InstSuspend│
│                       / InstResume / InstResumeThrow / InstSwitch │
└───────────────────────────────────────────────────────────────┘
```

## Opcodes (Wacs.Core/Wacs.Core/OpCodes/OpCode.cs)

| Hex  | Token          | Instruction class           | Stack effect                                                |
|------|----------------|-----------------------------|-------------------------------------------------------------|
| 0xE0 | `cont.new`     | `InstContNew`               | `[(ref null $ft)] → [(ref $ct)]`                            |
| 0xE1 | `cont.bind`    | `InstContBind`              | `[t1*..tn* (ref null $ct1)] → [(ref $ct2)]` (partial apply) |
| 0xE2 | `suspend`      | `InstSuspend`               | `[t1*..tn*] → [t1'*..tm'*]` (tag-determined)                |
| 0xE3 | `resume`       | `InstResume`                | `[t1*..tn* (ref null $ct)] → [t1'*..tm'*]`                  |
| 0xE4 | `resume_throw` | `InstResumeThrow`           | `[t1*..tn* (ref null $ct)] → [t1'*..tm'*]` (re-throw tag)   |
| 0xE5 | `switch`       | `InstSwitch`                | `[t1*..tn* (ref null $ct)] → [t1'*..tm'*]` (tail-resume)    |

All six classes are in `Wacs.Core/Wacs.Core/Instructions/StackSwitching.cs`.

## Heap- & val-type extensions

`Wacs.Core/Wacs.Core/Types/Defs/HeapType.cs`:

| Token     | HeapType byte (signed/unsigned) | Meaning                          |
|-----------|---------------------------------|----------------------------------|
| `cont`    | `0x68` (-0x18)                  | non-null continuation reference  |
| `nocont`  | `0x75` (-0x0b)                  | empty continuation type          |

`Wacs.Core/Wacs.Core/Types/Defs/ValType.cs`:

| Token       | Encoding                                                          |
|-------------|-------------------------------------------------------------------|
| `contref`   | `HeapType.Cont \| NullableRef \| SignBit`  (`ValType.ContRef`)    |
| `ref cont`  | `HeapType.Cont \| Ref \| SignBit`          (`ValType.ContRefNN`)  |

`ContType` is the IR class wrapping a `FuncTypeIdx` (the
continuation's parameter / result vector) — looked up by
`cont.new $ct` / `cont.bind $ct1 $ct2` / `resume $ct`.

## ContInstance — the runtime value

`Wacs.Core/Wacs.Core/Runtime/Concurrency/ContInstance.cs`:

- Implements `IGcRef`; stored in `Value.GcRef` for any
  `ContRef` / `ContRefNN` value.
- Holds the `ContType` (signature), the `IDelegateRef` wrapping
  the captured function reference, and `BoundArgs` (the prefix
  partial-applied via `cont.bind`).
- `State` enum: `Fresh` → `Running` → (`Suspended` or `Completed`).
  `Fresh` instances are allocated by `cont.new`; `cont.bind`
  produces another `Fresh` whose `BoundArgs` is the extended
  prefix; `resume` / `switch` transition to `Running`; a
  thrown `SuspensionException` whose tag matches an installed
  handler captures the running computation as a NEW `Fresh`
  instance passed to the handler arm; normal return marks
  `Completed`.

## Two execution modes

Stack switching runs under both engines, with one shared
contract — `IContinuationContext`.

### Mixed mode (interpreter + transpiled functions)

`ExecContext` is live; `Wacs.Core/Wacs.Core/Runtime/ThinContext.cs`
implements `IContinuationContext` by forwarding to the runtime's
`Store`, `Frame`, and `OpStack`. The interpreter's
`Inst*.Execute(exec)` methods and the transpiler's emitted
`StackSwitchingHelpers.*` calls converge on the same paths.

### Standalone mode (pure-transpiled module)

No `ExecContext`. `ThinContext` falls back to the module-local
`ContinuationStore` for `cont.new` / `cont.bind` / `suspend`;
the Tag array is captured at transpile time and lives on the
module object.

`resume` / `resume_throw` / `switch` in standalone mode would
need typed delegate marshaling for arbitrary continuation
signatures — currently throws `NotSupportedException` (the
`StandaloneInvokeUnsupported` message). The user-facing impact
is limited: every component-model component path goes through
mixed-mode anyway (the CM runtime owns the `ExecContext`).

## StackSwitchingHelpers — the transpiler ABI

`Wacs.Core/Wacs.Core/Runtime/Concurrency/StackSwitchingHelpers.cs`
exposes one static method per opcode, each taking typed CLR
arguments instead of popping from the interpreter `OpStack`.
The transpiler emits CIL `call` sites against these helpers so
emitted IL never imports interpreter internals.

```csharp
// transpiled CIL for cont.new $ct:
ldarg <ctx>          // IContinuationContext
ldloca <funcref>     // ref Value
ldc.i4 <typeIdx>
call ContNewStandalone(IContinuationContext, ref Value, int)
// → Value (ContRef carrying the new ContInstance)
```

Six entry points: `ContNewStandalone`, `ContBindStandalone`,
`SuspendStandalone`, `Resume`, `ResumeThrow`, `Switch`. The
helpers branch on `exec != null` to pick the runtime's
allocator vs the module-local one.

## Suspend / resume control flow

`suspend $tag` walks up the host's call stack throwing
`SuspensionException(tag, payload)`. Each `resume` / `switch`
opcode installs a `ResumeHandlerFrame` on the host's
stack with the on-tag handlers from its immediate; the catch
arm in the emitted IL (or in `Inst*.Execute`) compares the
incoming `Tag` against the frame's handler list, captures the
running computation as a fresh `ContInstance`, and dispatches
to the matching label.

`resume_throw` and `switch` reuse the same throw / catch
machinery — `switch` is morally "send + suspend in one step"
and is treated as a `suspend` immediately followed by a
`resume` of the switched-to continuation.

## Validation

`Wacs.Core/Wacs.Core/Validation/` validates:

- `cont.new`'s funcref type matches the `cont $ft` definition.
- `cont.bind`'s prefix matches the source-cont's param types
  and the residual signature matches the target-cont.
- `suspend $tag` only appears under a `resume` / `resume_throw`
  / `switch` that installs a handler for `$tag`, or inside a
  cont whose ambient handler set covers `$tag`.
- `try_table` catch arms and the stack-switching handler set
  are validated independently — `SuspensionException` and
  `UnhandledWasmException` are distinct types so the two
  unwinds don't cross-catch.

## Concurrency policy

`IConcurrencyPolicy` (Wacs.Core/Wacs.Core/Runtime/Concurrency/)
lets a host plug in cooperative or preemptive thread strategies
for `WasmThread` / `IWasmThreadHost`. Default
`NotSupportedPolicy` rejects thread creation; `ThreadBasedHost`
+ `HostDefinedPolicy` route to CLR threads. The async
dispatcher (next doc) treats this as the substrate for
`backpressure.set` and `waitable-set.wait` blocking — wasm
suspends become CLR `await` points and CLR
`TaskCompletionSource` completions translate back into resume
points.

## Spec conformance

The stack-switching submodule lives at
`Spec.Test/stack-switching/`; its proposal fixtures are run
under both the interpreter and the transpiler via the same
Spec.Test harness used for the core wasm tests. Failures bubble
up through the existing `Wacs.Spec.Test` xUnit collection.

## What's deferred

- **Standalone-mode `resume` / `resume_throw` / `switch`** —
  transpiled modules instantiated without a `WasmRuntime` host
  reject these ops at runtime. Mixed-mode (the
  ComponentInstance / CLI path) works.
- **Continuation persistence across threads** — `ContInstance`
  is single-threaded; a continuation created on thread A
  cannot be resumed on thread B. The current dispatcher honors
  this by serializing dispatcher entry through the owning
  `ExecContext`.

## Related docs

- [wasip3-architecture.md](./wasip3-architecture.md) — how the
  CM async dispatcher builds on this substrate.
- [WIT_HARNESS_APPROACH.md](./WIT_HARNESS_APPROACH.md) — the
  IDL-driven harness emitter that calls into the async
  dispatcher from generated C# bindings.
