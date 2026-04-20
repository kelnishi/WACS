# Switch Runtime Architecture

This document describes the **switch runtime** — a source-generated monolithic
WebAssembly interpreter that sits alongside the canonical polymorphic runtime
in WACS. It is opt-in at runtime via `WasmRuntime.UseSwitchRuntime = true` and
shares the same parsing, validation, instantiation, frame, OpStack, and
exception machinery as the polymorphic path. Only dispatch changes.

The text below walks top-down: what the polymorphic runtime does, what the
switch runtime does differently, the components, the bytecode format, the
source generator, and the specific optimization passes phase by phase with
before/after CIL snippets and measured results.

---

## 1. Why a second runtime

The polymorphic runtime dispatches WASM opcodes through virtual method calls on
`InstructionBase` subclass instances. The shape of the hot loop is:

```csharp
// Wacs.Core/Runtime/WasmRuntimeExecution.cs
while (++Context.InstructionPointer >= 0) {
    InstructionBase inst = Context._currentSequence[Context.InstructionPointer];
    inst.Execute(Context);                // virtual call
}
```

`Execute` is declared on `InstructionBase` and overridden by every concrete
subclass (`InstI32BinOp`, `InstLocalGet`, …). The JIT cannot devirtualize
because the receiver type is only known at run time. Each iteration pays:

* **Array indexer with bounds check** for `_currentSequence[pointer]`.
* **Indirect branch** through the v-table slot for `Execute`.
* **Virtual-call convention** on the callee side: prologue, epilogue, access to
  per-instruction fields (`_linkedLabel`, `_linkedLabels[]`, `BlockTarget`
  fields) that were already resolved at link time but still live behind a
  `this` pointer.
* **Stack-operand fetch via method calls**: `context.OpStack.PopI32()` /
  `PushI32(…)`. These methods are `[AggressiveInlining]` but the inlining
  budget is method-local; they inline well into small callers and not at all
  into big ones.

Running `fib(5M)` through this loop was measured at 1050 ms; the switch runtime
described below runs it at 520 ms. The rest of the document is how that was
done without surrendering spec conformance.

---

## 2. Two stacks side by side

```
    ┌───────────────────────────────────────────────────────────────┐
    │  WasmRuntime.CreateInvoker(addr, options)                    │
    │    → GenericDelegate(args)                                   │
    │       → PushScalars(params, args)   (same in both paths)     │
    │       ↓                                                       │
    │    branch on ctx.UseSwitchRuntime:                           │
    │                                                               │
    │    ┌────────────────────┐      ┌─────────────────────────┐   │
    │    │ polymorphic path   │      │ switch path             │   │
    │    ├────────────────────┤      ├─────────────────────────┤   │
    │    │ ProcessThreadWith- │      │ InvokeViaSwitch         │   │
    │    │   Options          │      │  → InvokeWasm           │   │
    │    │   (virtual Execute)│      │    → SwitchRuntime.Run  │   │
    │    │                    │      │      → Generated-       │   │
    │    │                    │      │        Dispatcher.Run   │   │
    │    └────────┬───────────┘      └────────┬────────────────┘   │
    │             ↓                            ↓                    │
    │          PopScalars (same in both paths)                      │
    └───────────────────────────────────────────────────────────────┘
```

**Shared:** module parsing, validation, instantiation, `Store`, `Frame`,
`OpStack`, the pooled `Value[]` locals array, `WasmException`/`TrapException`,
exception-handler resumption search, host-function calls.

**Switch-only:** the annotated bytecode stream compiled by `BytecodeCompiler`,
the generated dispatch loop in `GeneratedDispatcher.Run`, the hoisted
register-like locals that the inner loop works with, and a handful of helper
classes (`StreamReader`, `StreamFusePass`) that exist to keep the hot loop
dense.

Host-function calls transition from switch back to polymorphic automatically:
`Call`'s generated body checks `target is FunctionInstance` and only calls
`ControlHandlers.InvokeWasm` for WASM functions; for host functions it calls
`target.Invoke(ctx)` which runs the polymorphic marshalling path.

---

## 3. Invocation flow

Entry: `WasmRuntime.InvokeViaSwitch(func, funcType, args)` in
[`Wacs.Core/Runtime/WasmRuntimeSwitch.cs`](../Runtime/WasmRuntimeSwitch.cs).

```csharp
Context.OpStack.PushScalars(funcType.ParameterTypes, args);
Context.SwitchCallDepth = 0;
try {
    ControlHandlers.InvokeWasm(Context, func);
} catch (WasmException we) {
    FlushCallStackForSwitch();
    throw new UnhandledWasmException(...);
} catch (IndexOutOfRangeException ioe) {
    // overflow via Value[] exhaustion → surface as WasmRuntimeException
    ...
}
Context.OpStack.PopScalars(funcType.ResultType, results);
return results;
```

`ControlHandlers.InvokeWasm` (shared between both runtimes — it lives in
[`Instructions/ControlHandlers.cs`](../Instructions/ControlHandlers.cs)) does
the frame setup that every WASM function call requires:

1. Read the `SwitchCompiled` field on `FunctionInstance`; if null,
   `BytecodeCompiler.Compile` and cache it. Compile is deterministic; benign
   races just duplicate work.
2. Rent a `Value[]` locals array from `ArrayPool<Value>.Shared`, sliced to the
   exact parameter + declared-locals count. Pop args from the caller's OpStack
   into locals[0..paramCount].
3. Rent a `Frame` from `ExecContext._framePool`; wire `Module` and `Locals`.
4. Check `SwitchCallDepth` against `SwitchMaxCallStack` (default 48). This
   ensures runaway recursion surfaces as a `WasmRuntimeException` before the
   CLR's StackOverflowException terminates the process.
5. Swap `ctx.Frame`, bump depth, enter `SwitchRuntime.Run(ctx, compiled)`
   inside a try/finally that returns the locals array and frame to their pools
   on any exit.

`SwitchRuntime.Run` picks a dispatch entry:

```csharp
if (handlers.Length == 0) {
    GeneratedDispatcher.Run(ctx, code);   // handler-free fast path
    return;
}
while (true) {
    try { GeneratedDispatcher.Run(ctx, code); return; }
    catch (WasmException we) {
        if (!TryResumeWithHandler(ctx, handlers, we.Exn, ctx.SwitchPcBefore))
            throw;
        // loop: TryResumeWithHandler set ctx.SwitchPc; re-enter Run.
    }
}
```

`TryResumeWithHandler` walks the function's `HandlerEntry[]` sidecar looking
for the innermost `try_table` whose `[StartPc, EndPc)` range contains the
throwing opcode's pc. Handler-free functions skip the try/catch entirely —
zero IL overhead for the common case.

---

## 4. Annotated bytecode format

`BytecodeCompiler.Compile` transforms the validated `InstructionBase[]` into a
fixed-width byte stream. The design premise: everything that the dispatch
loop would otherwise recompute at run time is pre-baked.

**Opcode encoding**: `(byte)primary` for 1-byte opcodes, `(byte)prefix,
(byte)secondary` for `0xFB..0xFE` (GC, ExtCode, SimdCode, AtomCode), and the
synthetic `0xFF` prefix for super-instructions (see §7).

**Immediate widths** are *fixed* — no LEB128 at run time:

| Kind | Bytes | Notes |
|---|---|---|
| `i32` const | 4 | little-endian s32 |
| `i64` const | 8 | little-endian s64 |
| `f32`/`f64` const | 4/8 | IEEE bit pattern |
| `v128` const / shuffle lanes | 16 | |
| index (local/global/func/…) | 4 | u32 |
| lane index | 1 | u8 |
| memarg | 16 | `{align:u32, memidx:u32, offset:u64}` |
| block-type | 4 | post-validation: s32 type index (sentinels for scalar types) |
| branch triple | 12 | `{target_pc:u32, restore_stack:u32, arity:u32}` |
| `br_table` | `4 + 12·(N+1)` | `{count:u32, triples[N], default_triple}` |
| `try_table` catch entry | 20 | `{tag_idx, handler_pc, restore_stack, arity, kind}` |

**Structural opcodes are elided**: `block`, `loop`, `end` carry no runtime
work once branch targets are pre-resolved — they emit zero bytes.

**`if` encodes as** `[if_opcode][else_pc:u32][end_pc:u32]`; conditional skip
to else/end on a zero condition. `else` compiles to an unconditional branch
to end.

**`try_table`** emits to the sidecar `HandlerEntry[]`; its body bytes are
plain ops. When an exception propagates out of `GeneratedDispatcher.Run` and
`SwitchRuntime.Run`'s catch fires, the sidecar is consulted.

### Why fixed widths

LEB128 decoding has an unpredictable branch per byte. At 5M instructions per
second that's tens of millions of branches that the dispatch hot loop doesn't
need to see. Fixed widths turn immediate reads into a single unaligned load
(`Unsafe.ReadUnaligned<T>(ref code[pc])`) plus a constant `pc += N`.

Price paid: the annotated stream is ~2–3× larger than the original wasm
bytes. For a typical module this is a few hundred KiB — a trade we make
once per function, at first call.

---

## 5. The source generator

[`Wacs.Compilation/DispatchGenerator.cs`](../../Wacs.Compilation/DispatchGenerator.cs)
is a Roslyn `IIncrementalGenerator` that runs at build time and produces
`GeneratedDispatcher.g.cs` — the `Run` method plus its per-prefix sub-methods
(`DispatchFB`, `DispatchFC`, …) and the cold-primary sub-method
`DispatchCold`.

### 5.1 The two attribute kinds

Any static method marked with one of two attributes contributes a case to the
generated switch:

```csharp
// Pure stack-only numeric op. Generator pops operands, calls body as an
// expression, pushes result. No ExecContext or immediate parameters.
[OpSource(OpCode.I32Add)]
private static int ExecuteI32Add(int i1, int i2) => i1 + i2;

// General handler. Can take ExecContext, code span, ref pc, [Imm] params,
// and stack operands. Body may call arbitrary OpStack / Store / Frame APIs.
[OpHandler(OpCode.I32Const)]
private static void I32Const(ExecContext ctx, [Imm] int value)
    => ctx.OpStack.PushI32(value);
```

`[OpSource]` is the compact sugar; `[OpHandler]` is the full form. Internally
they lower to the same `DispatchEntry` struct — the generator just routes
parameter kinds differently.

### 5.2 Parameter classification

For each handler method the generator classifies every parameter:

| CLR type / modifier | `ParamKind` | Treatment |
|---|---|---|
| `ExecContext` | `Ctx` | Forwarded as `ctx` (aliased if renamed) |
| `ReadOnlySpan<byte>` | `Code` | Forwarded as `code` (aliased if renamed) |
| `ref int pc` | `RefPc` | Body's `pc` tokens rewritten to the caller's pc var |
| `[Imm] int/uint/long/…/V128` | `Immediate` | Emit `Unsafe.ReadUnaligned<T>` + `pc += width` |
| anything else (`int`, `long`, `float`, `Wacs.Core.Runtime.Value`, `V128`) | `Stack` | Emit a pop into a same-named local, reverse order |

### 5.3 Emission structure

The generated `Run` method hoists a set of locals from `ExecContext` fields,
then runs one giant switch inside a while loop. Pseudocode:

```csharp
public static void Run(ExecContext ctx, ReadOnlySpan<byte> code) {
    var _opStack     = ctx.OpStack;
    var _localsSpan  = ctx.Frame.Locals.Span;
    ref Value _registersRef = ref _opStack.FirstRegister();
    int _stackCount  = _opStack.Count;
    int _pc          = ctx.SwitchPc;
    ref byte _codeBase = ref MemoryMarshal.GetReference(code);
    int _codeLen     = code.Length;

    while (_pc < _codeLen) {
        ctx.SwitchPcBefore = _pc;
        byte primary = Unsafe.Add(ref _codeBase, _pc);
        _pc++;
        switch (primary) {
            case 0x6A: /* i32.add */ ...; continue;
            case 0x41: /* i32.const */ ...; continue;
            // …hot primaries inline…
            case 0xFB: _opStack.Count = _stackCount;
                       _pc = DispatchFB(ctx, code, _pc);
                       _stackCount = _opStack.Count;
                       continue;
            case 0xFC: /* same pattern for ExtCode */ ...
            case 0xFD: /* SimdCode */ ...
            case 0xFE: /* AtomCode */ ...
            case 0xFF: /* WacsCode (super-instructions) */ ...
            default:   _opStack.Count = _stackCount;
                       _pc = DispatchCold(ctx, code, _pc, primary);
                       _stackCount = _opStack.Count;
                       continue;
        }
    }
    _opStack.Count = _stackCount;
}
```

The hot/cold split is governed by `HotPrimaryOpcodes()` in the generator —
essentially everything from the OpProfile survey that fires above 1% in real
workloads (consts, locals/globals, arith, compare, essential loads/stores,
control flow). The cold set (div/rem, rotl, saturating conversions, rare
reference ops, memory.size/grow) routes through `DispatchCold` — one extra
function call per cold op, which is tolerable because the profile says they
are rare.

Every prefix opcode (0xFB..0xFF) dispatches to its own sub-method. This keeps
`Run`'s switch table small enough that the JIT lowers it to a jump-table
branch instead of a linear chain of compares; the SIMD/GC/atomic op bodies
pay the sub-method call cost instead.

### 5.4 Per-case emission template

For each case the generator emits, in order:

1. **Parameter aliases** — if a handler renamed `ExecContext` to `context`,
   emit `ExecContext context = ctx;`. Reference copies; no overhead.
2. **Immediate reads** — inline `Unsafe.ReadUnaligned<T>` off `_codeBase`
   for each `[Imm]` parameter, width-fixed.
3. **Stack pops** — in reverse parameter order, into named locals matching
   the handler's signature.
4. **Handler body** — for expression-bodied handlers, emit the expression
   wrapped in an inline push; for block-bodied handlers, emit the full body
   with `return expr;` rewritten to `{ __r = expr; goto __end_N; }`.
5. **Push / continue** — push the result (if non-void) and either `continue;`
   (inside `Run`'s loop) or `return pc;` (inside a sub-method).

These rules compose with the phase-by-phase optimizations in §6.

### 5.5 The catch for handler-body text

Some handlers use `ctx.OpStack.PushI32(v)`, `ctx.OpStack.PopAny()`, etc.
directly in their body text. `RewriteCtxAccess` rewrites that text to match
whatever the current case is emitting — in practice, substituting the inline
pop/push expansion for the method call. This means the *source* of a handler
stays readable (`=> ctx.OpStack.PushI32(value)`) while the *generated* code
is a single inline assign-and-increment sequence.

---

## 6. Optimization phases A–K

Each phase was a targeted change guided by a measured disassembly or a
profiling result. I-keep the original `[OpSource]` / `[OpHandler]` source of
every opcode stable across all phases; only the generator and helper classes
evolve.

### Phase A+B — inline the `Run` method; drop the worker thread

**Before (`commit 1fd367b^`)**: `TryDispatch` was called in a loop from a
dedicated `Thread` with a 32 MiB stack, because the per-op dispatch frame
(from wrapper local functions and register-bank locals) routinely exhausted
the default 1 MiB stack. Each opcode dispatched through a `[OpSource]`
wrapper local function — the compiler treated each `Op_*` as a separate
method-local body with its own stack slots.

**Change**: rewrite the generator to emit the body inline *at the case site*
— no wrapper local functions. Also hoist the OpStack and the current frame's
`Locals.Span` into method locals at `Run` entry, so repeated `ctx.OpStack.X`
and `ctx.Frame.Locals.Span[idx]` accesses don't keep reading through `ctx`.

Switch-runtime entry was also changed to run synchronously on the caller's
thread (no worker thread), since the frame per activation shrank from ~20
KiB to ~1 KiB.

```csharp
// Before (conceptual):
static int Op_I32Add(int i1, int i2) => i1 + i2;
case 0x6A: {
    int i2 = ctx.OpStack.PopI32();
    int i1 = ctx.OpStack.PopI32();
    int r = Op_I32Add(i1, i2);
    ctx.OpStack.PushI32(r);
    return true;
}

// After phase A+B (conceptual, still uses method-call pops/pushes):
case 0x6A: {
    int i2 = _opStack.PopI32();
    int i1 = _opStack.PopI32();
    _opStack.PushI32(i1 + i2);
    continue;
}
```

Switch runtime drops from worker-thread-bound to inline-callable; `fib(5M)`
moves from infeasible (stack exhaustion at depth ~40) to running cleanly on
the default thread.

### Phase C — inline immediate reads at emit time

**Before (`commit 1fd367b`)**: Each immediate was read by calling
`StreamReader.ReadU32(code, ref pc)` — an `[AggressiveInlining]` helper that
the JIT declined to inline into `Run` because `Run` was too large.
Disassembly showed a PLT-style indirect dispatch (~11 instructions plus a
`blr`) per immediate.

**Change**: emit `Unsafe.ReadUnaligned<T>` inline at each `[Imm]` parameter
site. The generator computes the byte width from the CLR type and writes
the code directly into the case body.

```csharp
// Before:
uint idx = StreamReader.ReadU32(code, ref _pc);  // call

// After:
uint idx = Unsafe.ReadUnaligned<uint>(
    ref Unsafe.Add(ref _codeBase, _pc));
_pc += 4;
```

Every immediate read reduces to one `ldur` on ARM64 (unaligned load). The
pc increment is a constant add. Across a 5M-op benchmark this saves tens of
millions of indirect branches.

### Phase D — `SkipLocalsInit`

**Change**: annotate `Run` with `[SkipLocalsInit]`. The hoisted case-local
scalars (i1, i2, etc.) are always assigned before read; eliminating the
`.locals init` flag removes the JIT's automatic frame zero-fill at method
entry (several hundred bytes of `dczva` on ARM64 for a ~600-byte frame).

Verified via disassembly that the frame-zeroing loop at `G_M000_IG01`
shortens or disappears for scalar slots. Object-reference slots still get
initialized (required by the GC contract), but those are a minority.

### Phase E — hot/cold primary-opcode split

**Observation**: `Run`'s switch table had 150+ primary-op cases. The JIT's
switch lowering uses a jump-table for dense ranges and a binary tree for
sparse ones. With 150+ entries, the compiler was emitting a mix of both and
the hot cases sat past a tree of compares.

**Change**: the generator inlines only `HotPrimaryOpcodes()` in `Run`'s
switch — roughly the primary ops that fired above 0.5% in the OpProfile
survey across coremark / wasm2wat / perl / f64-numeric samples. Everything
else routes to a single `default:` case that calls `DispatchCold`, a
sub-method with its own switch covering the long-tail primaries.

Effect: `Run`'s jump-table shrinks to ~100 entries, dense in the 0x00..0xAC
range. The JIT lowers it as a direct `br x_jumpTableBase` branch. Cold ops
pay one extra function call + one re-hoist of `_opStack`/`_localsSpan` each
time they execute, but per-function amortised cost stays under 1% on every
profile we measured.

### Phase F — pc/pcBefore from `ref int` to `ExecContext` fields

**Before**: `TryDispatch(ref int pc, ref int pcBefore)` was the generator's
output. The `ref int pc` parameter aliased `_pc` back to a caller stack slot,
and RyuJIT treated it conservatively — spilling `_pc` to memory on every
write, even inside a leaf case body. Disassembly showed 3× `ldr+str` pairs
around every immediate read.

**Change**: pass state via two new `ExecContext` fields, `SwitchPc` and
`SwitchPcBefore`. The generated `Run` seeds locals from those fields at
entry and (originally) writes them back in a `finally`. The `ref int pc` is
gone from the signature.

```csharp
// Before (signature): TryDispatch(ExecContext ctx, ReadOnlySpan<byte> code, ref int pc, ref int pcBefore) : bool
// After  (signature): Run(ExecContext ctx, ReadOnlySpan<byte> code) : void
//   body: int _pc = ctx.SwitchPc; int _pcBefore = ctx.SwitchPcBefore;
//         ... while (...) { ... } finally { ctx.SwitchPc = _pc; ... }
```

The ref-parameter pin goes away; `_pc` becomes a normal local that the JIT
is free to allocate however it likes. Measured 2× speedup across the bench
(`fib`, `fac`, `sum` all roughly halved).

### Phase G — bytecode-stream super-instruction fuser

[`StreamFusePass`](StreamFusePass.cs) walks the annotated stream after
`BytecodeCompiler` and rewrites short op sequences into single synthetic
`0xFF`-prefixed super-ops. Example patterns (not exhaustive):

| Pattern | Fused op | What it saves |
|---|---|---|
| `local.get A; local.set B` | `LocalGetSet(from=A, to=B)` | one decode, one push+pop |
| `i32.const K; local.set A` | `LocalI32ConstSet(k, to=A)` | same |
| `local.get A; local.get B; i32.add` | `I32LLAdd(A, B)` | two decodes + pop+push |
| `local.get A; i64.extend_i32_s` | `I64ExtendI32SL(A)` | decode + push |

Fused ops are declared as `[OpHandler]` methods under the `WacsCode` prefix
and go through the same generator pipeline. They dispatch via `Run`'s
`case 0xFF` into `DispatchFF`, which is a normal prefix sub-method.

The fuser runs at module instantiation; the `CompiledFunction` cache
ensures we pay that cost exactly once per function regardless of how many
times it's invoked.

### Phase H — inline ShiftResults fast-path + expanded fusion

**Issue**: every `Br` / `BrIf` / `BrTable` unconditionally called
`_opStack.ShiftResults(arity, resultsHeight)` even when the current stack
height already matched `resultsHeight` (the common case — natural fall-
through). `ShiftResults` is short but can't be reliably inlined from `Run`.

**Change**: the generator recognises the `ShiftResults(arity, height)`
pattern in rewritten body text and rewrites it to an explicit branch:

```csharp
// Before:
_opStack.ShiftResults((int)arity, (int)resultsHeight);

// After:
if (_opStack.Count != (int)resultsHeight)
    _opStack.ShiftResultsSlow((int)arity, (int)resultsHeight);
```

The `Slow` suffix is the method `ShiftResults` used to delegate to — we
just route the wrapper's inner work directly, skipping the method call on
the common fall-through path.

Plus an expanded fusion catalogue: 19 new super-ops covering i32/i64 L-L
binary arith, L-L relationals, and common extension patterns. These move
fib/fac/sum in `swFuse` mode from 1.5–2× the polymorphic baseline to
~0.5× (half the time).

### Phase I — inline pop/push against a hoisted `_registersRef`

**Issue**: every `_opStack.PopI32Fast()` / `PushI32Fast()` in the emitted
cases was an out-of-line `blr` call. The Fast variants are
`[AggressiveInlining]` but RyuJIT declines — `Run` is a large method with
a large inlining budget already spent on case bodies.

**Change**: the generator *expands the body of those Fast methods at the
call site*, using a hoisted reference to `_registers[0]`:

```csharp
// Hoisted at Run entry:
ref Value _registersRef = ref _opStack.FirstRegister();  // MemoryMarshal.GetArrayDataReference

// Before (method call to Fast):
int i2 = _opStack.PopI32Fast();       // blr
...
_opStack.PushI32Fast(result);         // blr

// After (inline):
int i2 = Unsafe.Add(ref _registersRef, --_opStack.Count).Data.Int32;
...
Unsafe.Add(ref _registersRef, _opStack.Count).Type      = ValType.I32;
Unsafe.Add(ref _registersRef, _opStack.Count).Data.Int32 = result;
_opStack.Count++;
```

The two `Unsafe.Add` calls in the push sequence compute the same address;
the JIT CSEs them to one.

**Scope discipline**: the inline expansion is gated per call site. Inside
`Run` (the hot path) pops/pushes are inline. Inside the per-prefix
sub-methods (`DispatchFB`..`DispatchFF`, `DispatchCold`) the emits stay
as Fast method calls. The reason: unconditional inline everywhere grew
the per-frame size enough that deeply recursive WASM
(`call_indirect.wast`, 48 levels) stack-overflowed the default thread
stack. Keeping the cold paths method-call-based keeps their frames small.

### Phase J — hoist `_opStack.Count` + in-place pop-op-push for same-type ops

Two complementary changes on the same disassembly observation: an
`I32Add` was doing 10 memory accesses per op, four of them on `Count`
alone (`Count` is a public mutable field on a class; the JIT can't keep
it in a register across an opcode body because any method call on the
`_opStack` reference could in principle alias it).

**(A) Hoist `Count` into a local**:

```csharp
int _stackCount = _opStack.Count;  // at Run entry
// ... all emitted pops/pushes work on _stackCount in registers ...
_opStack.Count = _stackCount;       // at loop exit (normal) and at sync points (throwable)
```

Sync points are (i) every sub-method call, (ii) every block-bodied handler
that invokes non-inlined `OpStack` methods. Expression-bodied hot cases
(the arith ops) don't sync — they just read/write `_stackCount`.

**(B) In-place pop-op-push for same-type `[OpSource]` ops**:

When a handler's stack parameters and return type are all the same scalar
type, the bottom operand doesn't need to leave its slot. We read it,
compute the new value, write back:

```csharp
// Binary same-type (i32.add):
int i2 = Unsafe.Add(ref _registersRef, --_stackCount).Data.Int32;  // pop top (y)
int i1 = Unsafe.Add(ref _registersRef, _stackCount - 1).Data.Int32; // peek new top (x)
Unsafe.Add(ref _registersRef, _stackCount - 1).Data.Int32 = (i1 + i2); // overwrite x

// No Type write (unchanged), no ++Count (we only popped one).
```

**Counted operation reductions on `I32Add`**:

| Phase | Mem ops | Breakdown |
|---|---|---|
| I (baseline for this comparison) | 10 | 2 pops × (1 Count r/w + 1 Data r) + 1 push × (1 Type w + 1 Data w + 1 Count r/w) + 1 loop-Count r/w |
| J (A only) | 7 | same shape but Count is register-resident (-3 Count r/w) |
| J (A+B, binary same-type) | **3** | 1 Count `--` (register), 1 slot read, 1 slot write |

Bench impact on swFuse: fac −12%, sum −16%, fib flat. `switch` (plain)
beats polymorphic on fac (0.68×) and sum (0.60×).

### Phase K — remove `Run`'s try/finally

**Motivation**: the try/finally wrapping the dispatch loop was forcing
the JIT to keep `_pc` and `_pcBefore` memory-observable on every
potential throw point. The intention was: removing the try/finally lets
`_pc` live in a register.

**Change**:

* Loop no longer in a try/finally.
* `ctx.SwitchPcBefore = _pc;` written directly at each iteration start
  (covers the handler-resume path without a separate `_pcBefore` local).
* `_opStack.Count = _stackCount;` written explicitly after the while
  loop on normal exit.
* `ControlHandlers.InvokeWasm` saves/restores `ctx.SwitchPcBefore` in
  its existing try/finally, so nested exception propagation walks back
  up through each outer frame's own pcBefore (without this, outer
  `try_table` handlers fail to find the caller's pc in their range).
* `ctx.SwitchPc` is no longer written — nothing post-Run reads it on
  normal exit; handler resume resets it via `TryResumeWithHandler`.

**Disasm verification**: `_pc` is still spilled. RyuJIT had 10
callee-saved registers in use (x19–x28; many for hoists: `_opStack`,
`_codeBase`, `_codeLen`, `_registersRef`, `_stackCount`, span reference
temporaries) and chose to evict `_pc` to the stack regardless of the
exception region. The try/finally wasn't the pin; register pressure was.

Net change per iteration: 6 pc-related mem ops → 5 (one fewer `_pcBefore`
ldr). Plus the finally's 3 writes on exit are gone and the frame prologue
is simpler. Bench: fac and sum −6% each; fib flat. A modest win from
the secondary effects, even though the stated register-allocation goal
didn't pan out.

---

## 7. Measured results

Comparison of a 3-benchmark micro-suite across the optimization phases.
All numbers median of 4 runs, M1 Pro, .NET 8, `net8.0` target framework,
`DOTNET_TieredCompilation=0`.

```
              polymorphic     super       switch      swFuse     switch/poly   fuse/super
fib-iter(5M):    1050 ms     620 ms      1110 ms      520 ms       1.05x        0.85x
fac(20)×250k:     480 ms     240 ms       335 ms      170 ms       0.70x        0.70x
sum(5M):          870 ms     610 ms       520 ms      450 ms       0.60x        0.74x
```

* **polymorphic**: virtual-dispatch `ProcessThreadWithOptions`.
* **super**: polymorphic + pre-existing super-instruction pass
  (`InstAggregate2_1` etc.) wrapping sequences inside polymorphic's own
  stream.
* **switch**: switch runtime, no stream-fuser.
* **swFuse**: switch runtime + stream-fuser (`StreamFusePass`), the
  intended shipping configuration.

The consistent story: `switch` cuts memory traffic per opcode enough to
match the polymorphic runtime *without* super-instructions, and `swFuse`
(= switch + stream fuser) cuts it enough to match or beat
polymorphic-with-super.

---

## 8. Non-wins

### 8.1 InlineIL.Fody / threaded dispatch (Phase 7 scaffolding)

Goal: end each case with an inline IL `switch` to the next case, instead
of re-entering the jump table. Per-case peephole of ~3 instructions would
have amortised across the 100+ hot cases.

Status: scaffolded (`ThreadedExperiment.cs` + Fody wiring), proven to work
for a single op, shelved for three reasons:

1. `PublishAot=true` succeeded at the time but the AOT compiler's
   acceptance of hand-crafted `switch` IL is not guaranteed across
   SDK versions.
2. Bench gains on a proof-of-concept case were <3%, below the
   maintenance threshold.
3. Phase I+J+K achieved most of what Phase 7 was aiming for — the
   per-iteration dispatch preamble is already short.

### 8.2 Register-resident `_pc`

Phase K's stated goal. Removing the try/finally didn't achieve it; RyuJIT's
register allocator prioritises other hoisted state over `_pc`. Would
require reducing the number of competing hoists. Not yet attempted.

### 8.3 `[AggressiveInlining]` on `Run`-called helpers

Every `[AggressiveInlining]` annotation on OpStack and StreamReader methods
was measured to be a no-op in the `Run` context — `Run` is too large to
honour inlining requests from callees, so the attribute has no effect.
Works fine from smaller callers (the polymorphic path, tests), so we keep
the attribute — it just doesn't earn its keep on the switch path, and every
phase from C onward has expanded the bodies inline at emit time anyway.

---

## 9. AOT compatibility

Every phase has preserved `IsAotCompatible=true` on `Wacs.Core.csproj`:

* No `System.Reflection.Emit` / `DynamicMethod` on the switch runtime path.
* No `Type.GetMethod`, `Activator.CreateInstance`, or attribute
  introspection at run time.
* No late-bound generic instantiation that AOT can't pre-resolve.
* Build-time source generation only; all metadata lookups (the generator's
  `[OpSource]`/`[OpHandler]` discovery, parameter classification, body
  extraction) happen during compilation, not execution.

The pre-existing `InstructionSource.Get` in
[`Compilation/InstructionSource.cs`](InstructionSource.cs) uses
`GetMethods` + `GetCustomAttributes` and is therefore *not* on the switch
runtime's path. It's retained for diagnostic tooling that prints
per-opcode info at debug time.

---

## 10. File map

Generator (builds into `Wacs.Compilation.dll`, a `netstandard2.0` assembly
for source-generator compatibility):

* `Wacs.Compilation/DispatchGenerator.cs` — the generator. ~1300 lines
  including comments; contains all emission logic, parameter
  classification, body rewrites, and the inline expansion passes.

Generated output (one file, produced at build time):

* `Wacs.Core/obj/*/Wacs.Compilation/Wacs.Compilation.DispatchGenerator/GeneratedDispatcher.g.cs`

Hand-written runtime support:

* `Wacs.Core/Compilation/CompiledFunction.cs` — `record` holding the
  annotated bytecode, handler table, locals count, and signature.
* `Wacs.Core/Compilation/BytecodeCompiler.cs` — the link-pass consumer
  that walks `InstructionBase[]` and emits the annotated stream +
  handler table.
* `Wacs.Core/Compilation/StreamFusePass.cs` — the optional super-
  instruction rewriter; gated by
  `ctx.Attributes.UseSwitchSuperInstructions`.
* `Wacs.Core/Compilation/StreamReader.cs` — retained for the handful of
  places (validation-only tooling, `MinimalDispatcher`) that haven't
  been migrated to inline reads.
* `Wacs.Core/Compilation/SwitchRuntime.cs` — `Invoke` top-level wrapper,
  `Run` handler-aware dispatcher, `TryResumeWithHandler`.
* `Wacs.Core/Compilation/HandlerEntry.cs` — struct holding
  `{StartPc, EndPc, TagIdx, HandlerPc, ResultsHeight, Arity, Kind}`.
* `Wacs.Core/Compilation/GeneratedDispatcher.cs` — a ~20-line partial
  declaration so the generator has something to hook into.
* `Wacs.Core/Compilation/MinimalDispatcher.cs` — a reference
  implementation used by the `UseMinimal` dev flag for cross-checking
  generated output against a naïve dispatcher.

OpStack `Fast` variants:

* `Wacs.Core/Runtime/OpStack.cs` — `PopI32Fast`, `PushI32Fast`, …,
  `FirstRegister`. These are the method-call equivalents of the inline
  expansion; called from cold sub-methods.

ExecContext fields added over the phases:

* `SwitchPc`, `SwitchPcBefore` — Phase F. Seeded at entry, updated by
  the dispatch loop, read by the handler-resume path.
* `SwitchCallDepth` — Phase A. Bounded recursion counter for
  `InvokeWasm`'s depth check.
* `Attributes.SwitchMaxCallStack` — tunable depth limit (default 48).
* `Attributes.UseSwitchSuperInstructions` — gate for `StreamFusePass`.
* `TailCallPending` — Phase G tail-call support; set by
  `return_call*` handlers so `InvokeWasm` can re-enter without growing
  the managed stack.

---

## 11. Contributor notes

* **Adding an op**: write an `[OpSource]` (pure scalar) or `[OpHandler]`
  method in `Wacs.Core/Instructions/…`. The generator picks it up on
  next build. No wiring, no table edit.
* **Adding an immediate type**: extend `ImmWidth` and `TypeToDataField`
  in the generator. Update `BytecodeCompiler.SizeOf` to match.
* **Super-instruction patterns**: add a recogniser to `StreamFusePass`,
  a corresponding `[OpHandler]` under `WacsCode`, and verify the fused
  form beats the unfused on the bench.
* **Don't add reflection calls** anywhere under `Wacs.Core.Compilation/**`
  or on any method the generated dispatcher calls. `PublishAot=true` is
  the per-phase gate; it must keep passing.
* **When a phase appears to work on a subset of tests**: verify the
  complete `Spec.Test` run. Multi-value block/if tests, mutual recursion
  (call.wast `even`/`odd`), and try_table/throw tests are the canaries —
  they exercise stack-height invariants, `ctx.SwitchPcBefore`, and the
  sync bracket interactions that aren't obvious from a unit test.
