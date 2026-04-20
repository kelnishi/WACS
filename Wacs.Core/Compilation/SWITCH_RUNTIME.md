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
described below runs it at ~590 ms with super-instructions (800 ms without).
More importantly, phase M's iterative-dispatch refactor eliminated the
native-stack depth ceiling that used to cap WASM recursion at 48 levels —
the switch runtime now handles the full 2048-deep call chains that the
polymorphic path supports. The rest of the document is how that was done
without surrendering spec conformance.

**As of phase M, all 723 spec tests pass** — parity with the polymorphic
runtime.

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
    │    │   (virtual Execute)│      │    (one native frame)   │   │
    │    │                    │      │    → SwitchRuntime.Run  │   │
    │    │                    │      │      → Generated-       │   │
    │    │                    │      │        Dispatcher.Run   │   │
    │    │                    │      │        ┌──────────────┐ │   │
    │    │                    │      │        │ one outer    │ │   │
    │    │                    │      │        │ while-loop   │ │   │
    │    │                    │      │        │ iterates     │ │   │
    │    │                    │      │        │ through every│ │   │
    │    │                    │      │        │ WASM call    │ │   │
    │    │                    │      │        │ (push frame  │ │   │
    │    │                    │      │        │ onto ctx.    │ │   │
    │    │                    │      │        │ _switchCall- │ │   │
    │    │                    │      │        │ Stack, swap  │ │   │
    │    │                    │      │        │ code+pc+Frame│ │   │
    │    │                    │      │        │ locals; pop  │ │   │
    │    │                    │      │        │ on exit)     │ │   │
    │    │                    │      │        └──────────────┘ │   │
    │    └────────┬───────────┘      └────────┬────────────────┘   │
    │             ↓                            ↓                    │
    │          PopScalars (same in both paths)                      │
    └───────────────────────────────────────────────────────────────┘
```

**Shared:** module parsing, validation, instantiation, `Store`, `Frame`,
`OpStack`, the pooled `Value[]` locals array, `WasmException`/`TrapException`,
host-function calls.

**Switch-only:** the annotated bytecode stream compiled by `BytecodeCompiler`,
the generated dispatch loop in `GeneratedDispatcher.Run`, the hoisted
register-like locals that the inner loop works with, the explicit call stack
(`ctx._switchCallStack`) that replaces native-frame recursion per WASM call,
and a handful of helper classes (`StreamReader`, `StreamFusePass`) that exist
to keep the hot loop dense.

Host-function calls transition from switch back to polymorphic automatically:
`Call`'s generated body checks `target is FunctionInstance` and only runs the
iterative push-frame-and-switch sequence for WASM functions; for host
functions it calls `target.Invoke(ctx)` which runs the polymorphic
marshalling path.

### Polymorphic's frame stack vs switch's call stack

Both paths share `ctx.OpStack` (the operand value stack) but diverge on how
they track *call frames* (the per-function locals + module context):

| Aspect | Polymorphic | Switch (iterative, phase M+) |
|---|---|---|
| Call-frame storage | `ctx._callStack: Stack<Frame>` | `ctx._switchCallStack: Stack<SwitchCallFrame>` |
| Per-call native frames | zero | zero |
| Depth bound | `Attributes.MaxCallStack` (2048) | same (2048) |
| Heap per level | one pooled `Frame` object + locals `Value[]` | one pooled `Frame` + locals + one 32 B `SwitchCallFrame` struct |

Phase M eliminated the native-stack dependency that used to cap switch-runtime
recursion at ~48 levels. See §6.M.

---

## 3. Invocation flow (phase M — iterative)

Entry: `WasmRuntime.InvokeViaSwitch(func, funcType, args)` in
[`Wacs.Core/Runtime/WasmRuntimeSwitch.cs`](../Runtime/WasmRuntimeSwitch.cs).

```csharp
Context.OpStack.PushScalars(funcType.ParameterTypes, args);
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

`ControlHandlers.InvokeWasm` is now a thin entry-point wrapper (phase M
collapsed its old native-recursion body). It does the one-time frame setup
for the outermost function, then hands off to the iterative dispatcher:

1. Read the `SwitchCompiled` field on `FunctionInstance`; if null,
   `BytecodeCompiler.Compile` and cache it. Compile is deterministic; benign
   races just duplicate work.
2. Rent a `Value[]` locals array from `ArrayPool<Value>.Shared`, sliced to the
   exact parameter + declared-locals count. Pop args from the caller's OpStack
   into `locals[0..paramCount]`.
3. Rent a `Frame` from `ExecContext._framePool`; wire `Module` and `Locals`.
   Set `ctx.Frame` to this entry frame.
4. Call `SwitchRuntime.Run(ctx, compiled)` inside a try/finally that drains
   any residue on `ctx._switchCallStack` (defensive — only non-WasmException
   throw paths can leave residue), restores the outer caller's Frame, and
   returns the entry Frame + rented locals to their pools.

Note: the old `SwitchCallDepth` counter and `SwitchMaxCallStack = 48` guard
are **gone**. The iterative model caps depth via the heap-allocated
`ctx._switchCallStack` (`Attributes.MaxCallStack = 2048`, same as polymorphic).

`SwitchRuntime.Run` is now a trivial wrapper:

```csharp
public static void Run(ExecContext ctx, CompiledFunction func) {
    ctx.SwitchPc = 0;
    ctx.SwitchPcBefore = 0;
    GeneratedDispatcher.Run(ctx, func);
}
```

All the handler-aware exception handling that used to live here has moved
*inside* `GeneratedDispatcher.Run`'s outer try/catch — it runs per-exception,
walking `ctx._switchCallStack` and calling `TryResumeWithHandlerTable` on
each level's `HandlerEntry[]` until either a matching `try_table` resumes
execution or the stack is empty and the exception rethrows.

### The actual dispatch loop

`GeneratedDispatcher.Run` hoists its per-frame state into mutable locals so
`Call`/`CallIndirect`/`CallRef` (and their tail-call variants) can push a
`SwitchCallFrame` onto `ctx._switchCallStack` and re-aim the locals at the
callee without calling any helper that would grow the native stack:

```csharp
public static void Run(ExecContext ctx, CompiledFunction entryFunc) {
    var _opStack = ctx.OpStack;
    ref Value _registersRef = ref _opStack.FirstRegister();
    int _stackCount = _opStack.Count;

    // Per-frame state — reassigned on Call (switch to callee) and on pop
    // (restore caller).
    byte[] code = entryFunc.Bytecode;
    HandlerEntry[] handlers = entryFunc.HandlerTable;
    var _localsSpan = ctx.Frame.Locals.Span;
    ref byte _codeBase = ref ArrayRefHelper.GetByteRef(code);
    int _pc = ctx.SwitchPc;

    while (true) {
      try {
        while (true) {
            // Function-exit check (pc past end, or Return set it to int.MaxValue).
            if (_pc >= code.Length) {
                if (ctx._switchCallStack.Count == 0) {
                    _opStack.Count = _stackCount; return;          // outermost done
                }
                var popped = ctx._switchCallStack.Pop();
                ArrayPool<Value>.Shared.Return(popped.CalleeRentedLocals, true);
                ctx.ReturnFrame(ctx.Frame);           // return callee's frame
                code        = popped.Code;
                handlers    = popped.Handlers;
                ctx.Frame   = popped.WasmFrame;
                _codeBase   = ref ArrayRefHelper.GetByteRef(code);
                _localsSpan = ctx.Frame.Locals.Span;
                _pc         = popped.ResumePc;
                // DO NOT reset _stackCount from _opStack.Count here —
                // the callee's local tracking is authoritative (see §6.M).
                continue;
            }

            ctx.SwitchPcBefore = _pc;
            byte primary = Unsafe.Add(ref _codeBase, _pc);
            _pc++;
            switch (primary) {
                case 0x10: {  // Call — iterative push + switch
                    uint funcIdx = ...;
                    _opStack.Count = _stackCount;
                    var target = ctx.Store[...];
                    if (target is FunctionInstance wasmFunc) {
                        var compiled = wasmFunc.SwitchCompiled ?? Compile(wasmFunc);
                        // Pop arity args into newly-rented callee locals
                        var rented  = ArrayPool<Value>.Shared.Rent(totalCount);
                        var newSpan = new Memory<Value>(rented, 0, totalCount).Span;
                        for (int i = paramCount-1; i >= 0; i--)
                            newSpan[i] = _opStack.PopAnyFast();
                        var newFrame = ctx.RentFrame();
                        newFrame.Module = wasmFunc.Module;
                        newFrame.Locals = new Memory<Value>(rented, 0, totalCount);
                        // Bounded-depth check
                        if (ctx._switchCallStack.Count >= ctx.Attributes.MaxCallStack)
                            throw new WasmRuntimeException(...);
                        // Push caller state, switch all locals to callee
                        ctx._switchCallStack.Push(new SwitchCallFrame(
                            code, handlers, ctx.Frame, _pc, rented));
                        code        = compiled.Bytecode;
                        handlers    = compiled.HandlerTable;
                        ctx.Frame   = newFrame;
                        _codeBase   = ref ArrayRefHelper.GetByteRef(code);
                        _localsSpan = newSpan;
                        _pc         = 0;
                        _stackCount = _opStack.Count;   // fresh for callee
                        continue;
                    }
                    target.Invoke(ctx);                   // host function — native call
                    _stackCount = _opStack.Count;
                    continue;
                }
                // ... 0x11 CallIndirect, 0x14 CallRef: analogous ...
                // ... 0x12/0x13/0x15 tail-call variants: release current frame
                //     WITHOUT pushing, then switch to callee ...
                // ... all other opcodes: unchanged from phase L emission ...
            }
        }
      } catch (WasmException we) {
        // Unwind: try current function's handlers at ctx.SwitchPcBefore.
        // On miss, pop one level and retry at the caller's resume-1. On
        // empty stack, rethrow.
        ...
      }
    }
}
```

`SwitchCallFrame` is a 5-field struct (32 B on 64-bit): `byte[] Code`,
`HandlerEntry[] Handlers`, `Frame WasmFrame` (the *caller's* frame at push
time), `int ResumePc`, `Value[] CalleeRentedLocals` (for pool return on
pop). The backing `Stack<SwitchCallFrame>` is preallocated to
`Attributes.InitialCallStack = 512` at ExecContext construction — push/pop
is a struct-copy into/out of that array with no heap activity unless depth
exceeds 512 (then the array doubles; zero-alloc thereafter for the lifetime
of the ExecContext).

### Tail calls come for free

`return_call` (0x12), `return_call_indirect` (0x13), and `return_call_ref`
(0x15) replace the current frame in place: release the current frame's
rented locals + Frame back to pools, swap dispatch locals to point at the
callee, *do not* push a new `SwitchCallFrame`. The caller's prior entry
on `ctx._switchCallStack` (if any) remains — when the callee eventually
returns, it returns to the caller's caller, skipping us. Correct tail-call
semantics, bounded by `MaxCallStack` on the heap, zero native-stack cost.

### Exception unwinding

The outer `try/catch` around the dispatch loop fires once per
`WasmException`. The handler:

1. Calls `SwitchRuntime.TryResumeWithHandlerTable(ctx, handlers, exn, pcBefore, out handlerPc)` —
   walks the current function's `HandlerEntry[]` looking for the innermost
   `try_table` whose `[StartPc, EndPc)` range covers `pcBefore`. On match,
   sets `ctx.SwitchPc = handlerPc` and returns `true`.
2. If matched: set the dispatch-local `_pc = handlerPc`, `_stackCount =
   _opStack.Count` (handler-resume shifts the stack), `goto __resume;`
   re-enters the outer loop at the handler.
3. If miss: pop one `SwitchCallFrame` (return its callee rented + Frame),
   restore the caller's `code/handlers/Frame/pc`. Retry at
   `popped.ResumePc - 1` (the caller's opcode pc of the Call that got us
   here — within the caller's try_table range if the call was inside one).
4. If `ctx._switchCallStack.Count == 0` and still no match: rethrow. The
   exception propagates out of Run → out of InvokeWasm → into
   `InvokeViaSwitch`'s `catch (WasmException)`, which flushes the stack and
   raises `UnhandledWasmException`.

The `EnclosingBlock` linked list on `BlockTarget` is *not* walked at
runtime — `BytecodeCompiler` already linearized every `try_table` into the
function's `HandlerTable` sidecar during the emit pass, keyed by
`(StartPc, EndPc)` ranges that cover the `try_table` region of the
annotated stream.

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

### Phase L — shrink Run's frame 624 → 448 B

Three small experiments on `Run`'s local set; kept the two that stuck.

**Kept (A)** — unhoist `_codeLen`. Replaced the `_codeLen` int local with
an inline `code.Length` read at the `while` guard. Removing that one 4-byte
local freed the register the JIT was reserving for it; cascaded into
~160 B of spill-slot savings (the JIT had been parking other hot values
to stack to preserve the reservation). Zero bench regression.

**Reverted (B)** — unhoist `_localsSpan`. Reading `ctx.Frame.Locals.Span`
inline per LocalGet/LocalSet/Tee saved another 32 B of frame, but each
access included a `Memory<Value>.get_Span()` call; swFuse regressed 15–22%
on fib/fac/sum. Not worth the 32 B.

**Kept (C)** — switch Run from `ReadOnlySpan<byte> code` to `byte[] code`.
Eliminates the 16 B span-struct parameter-spill slot. Uses
`MemoryMarshal.GetArrayDataReference(code)` via a tiny
[`ArrayRefHelper`](GetArrayDataReferencePolyfill.cs) netstandard2.1
polyfill. Frame: 464 → 448 B. Bench flat.

Net: frame 624 → 448 B (−28%). Per-level recursion cost dropped from
~1 KB to ~800 B. Still not enough on its own to pass the 3 failing
`even(100)`/`odd(200)` tests under a 256 KiB xUnit thread stack — that
required the native-recursion model to go entirely (phase M).

### Phase M — iterative Run: no native recursion per WASM call

**Motivation**: Phase L plugged the per-frame leaks; the per-*depth* cost
was fundamental. Every WASM `call` still added three native frames
(InvokeWasm + SwitchRuntime.Run + GeneratedDispatcher.Run), and xUnit's
thread stack ran out around depth 48. The three spec tests exercising
`even(100)`/`odd(200)` needed depth ≥ 201 and trapped prematurely.

**Change**: replaced the native-recursion call model with an explicit
call stack on `ExecContext`. Conceptually: instead of calling a function
to dispatch the callee, the Call opcode body pushes a record of the
caller's state onto `ctx._switchCallStack` and mutates the dispatch
loop's per-frame locals to point at the callee. Execution stays in the
same outer `GeneratedDispatcher.Run` invocation — one native frame,
N WASM-call frames.

```csharp
// Per-frame state in Run becomes mutable:
byte[] code             = entryFunc.Bytecode;
HandlerEntry[] handlers = entryFunc.HandlerTable;
ref byte _codeBase      = ref ArrayRefHelper.GetByteRef(code);
var _localsSpan         = ctx.Frame.Locals.Span;
int _pc                 = 0;
```

**Pieces**:

* `SwitchCallFrame` struct (5 fields, 32 B): `byte[] Code`,
  `HandlerEntry[] Handlers`, `Frame WasmFrame` (caller's),
  `int ResumePc`, `Value[] CalleeRentedLocals`.
* `ExecContext._switchCallStack: Stack<SwitchCallFrame>` preallocated to
  `InitialCallStack = 512` slots at construction (16 KiB).
* `GeneratedDispatcher.Run`: signature changed to `(ExecContext,
  CompiledFunction)`. Outer `while(true) { try { while(true) { ... } }
  catch { ... } }` — the inner loop dispatches opcodes, the outer-loop
  catch unwinds for exception handling.
* `Call` (0x10), `CallIndirect` (0x11), `CallRef` (0x14) emit
  iterative push-frame-and-switch sequences. Tail variants (0x12, 0x13,
  0x15) emit release-in-place-and-switch (no push).
* `ControlHandlers.InvokeWasm` collapsed from a recursive wrapper to a
  thin "rent entry frame, call Run once" setup. No more
  `SwitchCallDepth` counter or `SwitchMaxCallStack` guard.
* Exception unwinding walks `ctx._switchCallStack`, calling
  `TryResumeWithHandlerTable` on each level's `HandlerEntry[]`.

**Depth bound**: `Attributes.MaxCallStack = 2048` — same as polymorphic,
64 KiB of heap per ExecContext at max capacity.

**Result**: **723/723 spec tests pass** — parity with polymorphic. The
3 depth-limited failures (`call.wast`, `call_indirect.wast`,
`call_ref.wast`) now pass.

**Three subtle correctness notes**:

1. *Do not resync `_stackCount = _opStack.Count` on pop-frame.* The
   callee's local `_stackCount` is authoritative across its entire
   execution (expression-body opcodes update only the local, not the
   field). `_opStack.Count` was last synced at Call entry and is stale
   by exactly the callee's net stack effect. Reading it on pop would
   give the pre-call value; the callee's pushed result would be lost.
   (Found by: `fac(5)` returning `1` instead of `120`.)

2. *InvokeWasm's defensive finally-drain returns `ctx.Frame` (the
   current callee), not `popped.WasmFrame` (the caller).* The push
   records the caller as `WasmFrame`; at pop, we're exiting the
   callee whose frame is `ctx.Frame`. Reversing the direction
   double-frees the entry frame on the abort path.

3. *`FlushCallStackForSwitch` clears `OpStack.Count` to 0 unconditionally
   instead of draining via `PopAny`.* In the iterative model, each active
   WASM frame's intermediate stack values stay on the shared OpStack
   simultaneously. Deep recursion can push the count past
   `_registers.Length` before the depth guard catches it; a subsequent
   `PopAny` would re-throw `IndexOutOfRangeException` and mask the real
   `WasmRuntimeException`. On the abort path the data is already lost,
   so just zero the count.

**Bench** (median of 4 runs vs phase L baseline):

| workload | switch phase L | switch phase M | swFuse phase L | swFuse phase M |
|---|---|---|---|---|
| fib-iter(5M) | 1111 ms | **801 ms** (−28%) | 509 ms | 587 ms (+15%) |
| fac(20)×250k | 320 ms | **243 ms** (−24%) | 167 ms | 200 ms (+20%) |
| sum(5M) | 495 ms | 647 ms (+31%) | 443 ms | 530 ms (+20%) |

`switch` (no super-instructions) **big wins** from eliminating the two
native frames per call (InvokeWasm + SwitchRuntime.Run ≈ 320 B/level)
and from avoiding each call's prologue/epilogue.

`swFuse` regressed ~15–20%. The outer `try/catch` wrapping Run's
dispatch loop adds exception-region IL that the JIT handles
conservatively, forcing some loop-local state to memory. Super-
instructions are more sensitive to this than plain opcodes because
DispatchFF is already hot and any extra memory ops compound. Likely
recoverable by splitting the handler-free path (no try/catch needed)
from the handler-carrying path — a follow-up.

`sum` regression across both modes is less obvious; current hypothesis
is that the iterative loop's frame-size change interacts with the loop
body's scalar-sum pattern differently than the recursive model did.
Worth disasm-investigating but not a correctness issue.

### Phase N — eager compile + cached CompiledFunction fields

**Motivation**: disasm of phase M's `Run` showed each WASM `call` hit a
null-check on `wasmFunc.SwitchCompiled` + a `Signature.ParameterTypes.Arity`
dereference + a per-slot `new Value(ValType)` init loop + a per-call
`ctx.Attributes.MaxCallStack` dereference. None of these belong in the
hot path — all derivable at compile time or hoistable.

**Change**: four cached fields + one instantiation-time walker.

1. **Strict eager compilation in `WasmRuntime.LinkModule`.** When
   `UseSwitchRuntime = true` at Instantiate time, every module-owned
   `FunctionInstance` is compiled through `BytecodeCompiler.Compile`
   as part of linking. The generator's `Call`/`CallIndirect`/`CallRef`
   cases then dereference `wasmFunc.SwitchCompiled` with no null check.
   Gated on the flag so poly-side `SuperInstruction` (which rewrites into
   `WacsCode` ops the bytecode compiler doesn't accept) stays unaffected.
2. **`CompiledFunction.ParamCount`** — cached from
   `Signature.ParameterTypes.Arity` at compile time. Call-family cases
   read the int field directly instead of walking through `Type →
   ParameterTypes → Arity`.
3. **`CompiledFunction.DefaultLocalsTemplate`** — pre-built `Value[]`
   holding per-slot defaults for declared locals. Call-site init replaces
   the per-slot `new Value(ValType)` loop with a single `Array.Copy`
   into the ArrayPool-rented buffer, plus the arg-pop loop. Applies both
   to `ControlHandlers.InvokeWasm` (entry) and to the generator's
   iterative Call cases.
4. **`_maxCallStack` hoisted at Run entry** — the Call-family depth
   guard reads a local int instead of `ctx.Attributes.MaxCallStack`
   (two field loads) per call.

**Bonus fix**: `BytecodeCompiler`'s handler-table pass now handles
`InstExpressionProxy` as a catch target (returns the `int.MaxValue`
return-sentinel pc, mirroring the branch-resolution path). Latent bug
previously masked by lazy compile — eager-compile on the full spec suite
was the gate that surfaced it.

**Bench** (median of 3, same workloads/hardware as §7):

| workload | switch phase M | switch phase N | swFuse phase M | swFuse phase N |
|---|---|---|---|---|
| fib-iter(5M) | 800 ms | **737 ms** (−8%) | 585 ms | 535 ms (−9%) |
| fac(20)×250k | 245 ms | **228 ms** (−7%) | 200 ms | 178 ms (−11%) |
| sum(5M) | 645 ms | **605 ms** (−6%) | 530 ms | 461 ms (−13%) |

The `fac` improvement is direct attribution: every one of the 5M calls
now skips the null-check, the Arity-walk, and the per-slot Value init
loop. The `fib-iter` and `sum` improvements are subtler — those
workloads have only one top-level call, so the Call-path wins are
negligible. Most of the delta is from eager compile warming L1i with
the callee's bytecode during instantiation, and from RyuJIT's slightly
better register allocation on the shortened Call case bodies (fewer
GC-tracked struct locals = lower register pressure).

**Tried and reverted**: a pending-trampoline try/catch refactor —
capture the exception in a local inside catch, run the unwind (which
mutates `code`, `handlers`, `_codeBase`, `_localsSpan`, `_pc`,
`_stackCount`) *outside* the try so the inner-loop dispatch locals
don't have to stay memory-observable across may-throw points. Disasm
showed no frame change and no per-iteration instruction change —
the 1424 B frame is driven by GC-tracked struct locals inside the 6
Call-family case blocks (each declares its own `__compiled`,
`__rented`, `__newLocals: Memory<Value>`, `__newSpan: Span<Value>`,
`__newFrame`), not by try/catch liveness. RyuJIT doesn't coalesce
GC-tracked locals across disjoint case blocks in a 5500-line switch.
Reverted to keep the generator simpler.

**Frame analysis** (for follow-up consideration):

The Call-family tail (compile-check → rent-from-template → push-frame →
switch-to-callee) is identical across the 6 Call opcodes but the JIT
allocates 6 separate slots for each GC-tracked local it holds. Extracting
the tail into a helper method would save ~1000 B of frame space but adds
a `blr` per WASM call — net-negative on call-heavy benchmarks.

The bigger per-iteration cost is that `_pc`, `code`, `_localsSpan`,
`_stackCount` all spill to frame between opcodes (~8 mem ops in the
fetch/advance/dispatch header before any opcode body runs). This is
register-allocation failure across 172+ case blocks, not frame size —
the JIT exhausts its register budget and spills even the hottest locals.
Not cleanly addressable without structural changes (per-case helpers
regressed historically; large switch exhausts register pressure).

---

## 7. Measured results

Comparison of a 3-benchmark micro-suite across the optimization phases.
All numbers median of 4 runs, M1 Pro, .NET 8, `net8.0` target framework,
`DOTNET_TieredCompilation=0`.

Phase N (current head):

```
              polymorphic     super       switch      swFuse     switch/poly   fuse/super
fib-iter(5M):    1067 ms     639 ms       737 ms      535 ms       0.69x        0.84x
fac(20)×250k:     478 ms     238 ms       228 ms      178 ms       0.48x        0.75x
sum(5M):          888 ms     623 ms       605 ms      461 ms       0.68x        0.74x
```

* **polymorphic**: virtual-dispatch `ProcessThreadWithOptions`.
* **super**: polymorphic + pre-existing super-instruction pass
  (`InstAggregate2_1` etc.) wrapping sequences inside polymorphic's own
  stream.
* **switch**: switch runtime, no stream-fuser.
* **swFuse**: switch runtime + stream-fuser (`StreamFusePass`), the
  intended shipping configuration.

Plain `switch` is the fastest across all three benchmarks even without
the stream-fuser. `swFuse` beats polymorphic-super by 12–26%, and plain
`switch` beats polymorphic by 31–52%. Phase N's Call-family cleanup
shaved the remaining Call overhead (null-check, Arity walk, per-slot
Value init, MaxCallStack dereference) — the fac workload, which exercises
Call 5M times, saw the largest relative gains.

Correctness: **118/118 wast files pass** on the spec-suite survey
(`Spec.Test/survey-switch-runtime.sh`), parity with polymorphic.
Compilation tests: 50/50.

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

### 8.4 Phase M's try/catch pessimising the swFuse hot path

The outer `try/catch (WasmException)` wrapping Run's dispatch loop is
required for per-function exception-handler unwinding (the catch walks
`ctx._switchCallStack` and calls `TryResumeWithHandlerTable` on each
level). The exception region adds IL metadata that RyuJIT treats
conservatively — some loop-local state gets spilled to the frame so the
catch can observe it.

Measurement: switching from phase L's native-recursion model (where
each Run invocation's try/catch was scoped to one function at a time,
and handler-free functions skipped the catch entirely) to phase M's
single all-encompassing try/catch cost swFuse ~15–20% across fib/fac.
Plain `switch` still net-wins because the saved native-frame overhead
dominates.

The obvious fix is to split the dispatch loop: a handler-free inner
loop that runs until the callee pushes a frame with `Handlers.Length >
0`, at which point we switch to a handler-aware inner loop with the
try/catch. Most WASM functions have no `try_table`, so the fast path
would stay exception-region-free. Deferred to a follow-up.

### 8.5 `sum(5M)` regression under phase M

Plain `switch` on `sum(5M)` went from 495 ms (phase L) to 645 ms
(phase M) — worse than the ~30% gain on fib/fac. `sum` is an iterative
i64-accumulate loop with no function calls, so the iterative/recursive
change should be neutral. Hypothesis: the added per-call-state locals
in Run (`code`, `handlers` as mutable vars) interact badly with the
i64-arith hot path's register allocation. Needs disasm investigation.

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

* `Wacs.Compilation/DispatchGenerator.cs` — the generator. ~1500 lines
  including comments; contains all emission logic, parameter
  classification, body rewrites, the inline expansion passes, and the
  phase-M iterative Call/CallIndirect/CallRef/tail-call case emission.

Generated output (one file, produced at build time):

* `Wacs.Core/obj/*/Wacs.Compilation/Wacs.Compilation.DispatchGenerator/GeneratedDispatcher.g.cs`

Hand-written runtime support:

* `Wacs.Core/Compilation/CompiledFunction.cs` — class holding the
  annotated bytecode, handler table, locals count, signature,
  plus the phase-N caches: `ParamCount` and `DefaultLocalsTemplate`
  (pre-built `Value[]` for call-site locals init).
* `Wacs.Core/Compilation/BytecodeCompiler.cs` — the link-pass consumer
  that walks `InstructionBase[]` and emits the annotated stream +
  handler table.
* `Wacs.Core/Compilation/StreamFusePass.cs` — the optional super-
  instruction rewriter; gated by
  `ctx.Attributes.UseSwitchSuperInstructions`.
* `Wacs.Core/Compilation/StreamReader.cs` — retained for the handful of
  places (validation-only tooling, `MinimalDispatcher`) that haven't
  been migrated to inline reads.
* `Wacs.Core/Compilation/SwitchRuntime.cs` — trivial entry wrappers
  (post-phase-M) plus `TryResumeWithHandler` /
  `TryResumeWithHandlerTable` for the iterative catch.
* `Wacs.Core/Compilation/SwitchCallFrame.cs` — phase M's per-call
  struct: `Code`, `Handlers`, `WasmFrame` (caller), `ResumePc`,
  `CalleeRentedLocals`.
* `Wacs.Core/Compilation/HandlerEntry.cs` — struct holding
  `{StartPc, EndPc, TagIdx, HandlerPc, ResultsHeight, Arity, Kind}`.
* `Wacs.Core/Compilation/GetArrayDataReferencePolyfill.cs` — tiny
  netstandard2.1 forwarder so `Run` can use the same reference-producing
  call on both target frameworks.
* `Wacs.Core/Compilation/GeneratedDispatcher.cs` — a ~20-line partial
  declaration so the generator has something to hook into.
* `Wacs.Core/Compilation/MinimalDispatcher.cs` — a reference
  implementation used by the `UseMinimal` dev flag for cross-checking
  generated output against a naïve dispatcher.

OpStack `Fast` variants:

* `Wacs.Core/Runtime/OpStack.cs` — `PopI32Fast`, `PushI32Fast`, …,
  `FirstRegister`. These are the method-call equivalents of the inline
  expansion; called from cold sub-methods.

ExecContext fields added/changed over the phases:

* `SwitchPc`, `SwitchPcBefore` — Phase F. Seeded at entry, updated by
  the dispatch loop, read by the handler-resume path.
* `_switchCallStack: Stack<SwitchCallFrame>` — **Phase M.** The
  explicit per-WASM-call frame record. Preallocated to
  `InitialCallStack = 512` slots at ExecContext construction (16 KiB);
  doubles on depth growth up to `MaxCallStack = 2048`.
* `SwitchCallDepth`, `TailCallPending` — **Phase M removed these.**
  `SwitchCallDepth` is replaced by `_switchCallStack.Count`;
  `TailCallPending` is gone because return_call/etc. handle the tail
  replacement inline in the Call case body.
* `Attributes.MaxCallStack` — tunable depth limit (default 2048, used
  by both polymorphic `_callStack` and switch `_switchCallStack`).
  `Attributes.SwitchMaxCallStack` is still defined but unused post-M.
* `Attributes.UseSwitchSuperInstructions` — gate for `StreamFusePass`.
* `UseSwitchRuntime` — **Phase N** uses this flag at Instantiate time
  to decide whether to eagerly compile every module-owned
  `FunctionInstance`. Flipping the flag *after* instantiation leaves
  `SwitchCompiled` null on callees, and the generator's Call-family
  cases assume non-null — so flag-flip-after-instantiate is unsupported.

---

## 11. Contributor notes

* **Adding an op**: write an `[OpSource]` (pure scalar) or `[OpHandler]`
  method in `Wacs.Core/Instructions/…`. The generator picks it up on
  next build. No wiring, no table edit. **Exception:** the 6 call-family
  opcodes (`0x10`/`0x11`/`0x12`/`0x13`/`0x14`/`0x15`) are hand-crafted
  directly in `DispatchGenerator.EmitIterativeCallCases` — they need
  access to the mutable per-frame locals (`code`, `handlers`,
  `_codeBase`, `_localsSpan`, `_pc`) that only the outer Run loop owns.
  The `[OpHandler]` methods for these opcodes in
  `ControlHandlers.cs` are effectively dead code (retained as a reference
  implementation and for the polymorphic runtime's use; the switch
  generator's filter skips them).
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
  (call.wast `even`/`odd`), try_table/throw tests, and the
  `assert_exhaustion` runaway-recursion tests are the canaries — they
  exercise stack-height invariants, `ctx.SwitchPcBefore`, the
  sync-bracket interactions, *and* (post-M) the iterative frame
  push/pop + handler-table unwinding.
* **Modifying Run's outer loop**: any change to how `code`, `handlers`,
  `_codeBase`, `_localsSpan`, or `_pc` get swapped on Call/pop needs to
  keep these invariants:
   1. At pop, do NOT reset `_stackCount = _opStack.Count` — the local
      has the authoritative running count; the field is stale by the
      callee's net stack effect.
   2. On a push, the stored `WasmFrame` is the *caller's* frame (the
      current `ctx.Frame` just before the switch); the stored
      `CalleeRentedLocals` is the *callee's* newly-rented array. On
      pop, return `ctx.Frame` (the callee) to the Frame pool and
      `CalleeRentedLocals` to the ArrayPool; then restore
      `ctx.Frame = popped.WasmFrame`.
   3. Emit `ctx.SwitchPcBefore = _pc` at iteration start, before the
      opcode fetch, so the handler-unwind path (and
      `SwitchRuntime.TryResumeWithHandlerTable`) sees the opcode's
      pre-fetch pc when a throw fires mid-case.
