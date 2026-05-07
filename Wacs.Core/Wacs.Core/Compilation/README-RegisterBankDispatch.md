# Register-bank dispatch design

Design note for the DispatchGenerator refactor that rewrites the switch
runtime's inner loop to look like a real VM interpreter instead of a call
stack of small static methods.

Target file: `Wacs.Core/Compilation/GeneratedDispatcher.cs` (source-generated).
Target generator: `Wacs.Compilation/DispatchGenerator.cs`.

## Problem with the current shape

The current generator emits each `[OpHandler]` / `[OpSource]` method as a
static local function inside `TryDispatch`, with the case body preparing
operands as freshly-named locals and calling the local function:

```csharp
case 0x6A0000: {                       // i32.add
    int i2 = ctx.OpStack.PopI32();     // local #47
    int i1 = ctx.OpStack.PopI32();     // local #48
    int r = Op_I32Add(i1, i2);         // local function call
    ctx.OpStack.PushI32(r);
    return true;
}
case 0x200000: {                       // local.get
    uint idx = StreamReader.ReadU32(code, ref pc);  // local #49
    LocalGet(ctx, idx);
    return true;
}
// ... 300+ more cases, each declaring its own locals
```

Two compounding effects:

1. **Frame bloat.** Each case declares its own set of named locals. Roslyn
   coalesces slots across non-overlapping scopes when it can prove
   non-overlap, but across 300+ cases with irregular types (int, uint, long,
   float, V128, Value) the JIT ends up reserving a very wide frame — measured
   at ~20 KiB/activation during `fac.wast` debugging. With the default 1 MiB
   thread stack, that caps WASM recursion at ~40 levels before hitting
   `StackOverflowException`. The current workaround: spawn a dedicated
   worker thread with 32 MiB of stack per top-level invoke. This adds
   ~50–200 μs per invocation — fine for long-running benchmarks, disastrous
   for spec-test-shaped workloads with thousands of sub-millisecond invokes.

2. **Per-case decode/dispatch overhead.** Every case locally declares its
   immediate reads and operand pops as fresh variables. The JIT's register
   allocator has to treat each as an independent lifetime, so even when the
   local function inlines, you pay for per-case stack-slot setup and
   teardown instead of reusing registers.

## Target shape

Declare a small fixed **register bank** at the top of `TryDispatch` — typed
slots reused by every case — plus a single **immediate union** covering the
widest immediate shape. Each case writes to the bank before reading, so
RyuJIT's def-use analysis treats every case as a fresh def and coalesces
slots across non-overlapping cases — ideally assigning them to physical
registers.

```csharp
public static bool TryDispatch(ExecContext ctx, ReadOnlySpan<byte> code, ref int pc, uint op)
{
    // -------- Register bank (typed temps, reused per case) ----------------
    int    a32,  b32;       // two i32 temps cover add/sub/mul/cmp/…
    long   a64,  b64;
    uint   ai32, bi32;      // unsigned variants
    ulong  au64;
    float  af32, bf32;
    double af64, bf64;
    V128   avec, bvec;
    Value  aref, bref;      // refs, exnrefs

    // -------- Immediate union (every op that takes immediates reuses this)
    // 16-byte explicit-layout struct. One address on the frame, reinterpreted
    // per case. Smaller than the sum of every case's private locals.
    ImmUnion imm;

    switch (op) {
        case 0x6A0000:                 // i32.add
            b32 = ctx.OpStack.PopI32();
            a32 = ctx.OpStack.PopI32();
            ctx.OpStack.PushI32(a32 + b32);
            return true;

        case 0x200000:                 // local.get
            imm.U32 = ReadU32(code, ref pc);
            ctx.OpStack.PushValue(ctx.Frame.Locals[(int)imm.U32]);
            return true;

        case 0x360000:                 // i32.store
            imm.Mem = ReadMemArg(code, ref pc);
            b32 = ctx.OpStack.PopI32();    // value
            a32 = ctx.OpStack.PopI32();    // addr
            Store32(ctx, imm.Mem, a32, b32);
            return true;

        case 0xFD006A:                 // f32x4.add
            bvec = ctx.OpStack.PopV128();
            avec = ctx.OpStack.PopV128();
            ctx.OpStack.PushV128(avec + bvec);
            return true;
    }
}

[StructLayout(LayoutKind.Explicit)]
public struct ImmUnion
{
    [FieldOffset(0)] public int    S32;
    [FieldOffset(0)] public uint   U32;
    [FieldOffset(0)] public long   S64;
    [FieldOffset(0)] public ulong  U64;
    [FieldOffset(0)] public float  F32;
    [FieldOffset(0)] public double F64;
    [FieldOffset(0)] public byte   Lane;     // single-byte lane index
    [FieldOffset(0)] public MemArg Mem;      // 16 bytes; overlaps V128
    [FieldOffset(0)] public V128   V128;
}
```

Key design rules:

1. **Every case writes before reading.** The emitter enforces this by
   ordering: immediate reads first (into `imm.*`), then operand pops
   (into `aXX`/`bXX`), then the computation.
2. **Fixed bank size.** Pick the minimum typed slot set that covers the
   WASM type grid: 2×i32, 2×u32, 2×i64, 2×f32, 2×f64, 2×V128, 2×Value.
   ~20 slots total, ~200 bytes frame. Any op needing more than two operands
   of the same type is rare (the 3-arg bitselect is the main exception —
   add a third `cv128`).
3. **Immediates via the union, never named locals.** Case bodies use
   `imm.U32`, `imm.Mem`, `imm.V128` instead of `uint idx`, `MemArg m`,
   `V128 value`. The `FieldOffset(0)` layout means one stack slot is
   reinterpreted across the whole dispatcher.
4. **Handler signatures stay the source of truth.** The generator still
   reads `[OpHandler]` methods, but instead of emitting
   `Op_Xxx(args); ctx.OpStack.Push(...)` it **inlines the body directly**
   into the case, rewriting the method's parameters as references to the
   bank/union slots.

## What the generator has to do

1. **Parse the handler's parameter list** (already done — `ParamInfo` / `ParamKind`).
2. **Bind each parameter to a bank slot or immediate union field:**
   - `[Imm] uint x` → `imm.U32`
   - `[Imm] byte lane` → `imm.Lane`
   - `[Imm] V128 v` → `imm.V128`
   - Stack `uint x` → `ai32` (first unsigned-int stack param) or `bi32`
     (second)
   - Stack `int x` → `a32` / `b32`
   - Stack `V128 x` → `avec` / `bvec`
3. **Emit the body** with identifier substitution:
   - In the handler source, `x + y` → `a32 + b32`
   - Handler calls / method bodies need the same rewrite.
4. **Emit the case in the fixed order:** immediate reads (`ReadU32` →
   `imm.U32 = …`), stack pops (LIFO — last param is top of stack), body,
   then push the return value (if any) using `imm` or a bank slot as the
   return register.

## Expected wins

- **Frame size collapse.** From ~20 KiB/activation to ~200 B. Recursion
  depth on default 1 MiB stack goes from ~40 to ~5000. The worker thread
  machinery (`Thread`, `SwitchRuntimeStackSize`, `Start`/`Join`) becomes
  unnecessary and can be removed.
- **Per-invoke overhead collapse.** Removing the worker thread eliminates
  ~100 μs/invoke. Spec-test wall time (currently 34s vs polymorphic's 7s)
  should approach parity or better.
- **Hot-loop throughput.** The per-op native code shrinks from "enter
  method frame, pop-to-local, call handler, return, push-from-local" to
  "read from pre-decoded slot, op, write back". Real measurement needed;
  CoreMark / fib are the target.
- **Simpler stack traces.** No layer of static local functions between the
  dispatcher and the actual per-op logic.

## Risks / open questions

- **Spill behavior.** If the JIT spills the whole bank to the frame, we're
  back to a fixed-size frame — still predictable, still smaller than
  current, but not the register-resident ideal. Verify with
  `dotnet-objdump` or similar post-landing.
- **`Value` locals on the bank are non-trivial** (they contain a union +
  GcRef). They still need to live on the managed stack for the GC's
  benefit. Probably fine, just worth spot-checking.
- **Union struct in a mono-method switch** — in the rare case where C#
  scoping rules force spilling, the FieldOffset overlay may force the JIT
  to memory-alias. Might be worth prototyping with a plain 16-byte `Span<byte>`
  stackalloc'd at method entry and reading via `MemoryMarshal.AsRef<T>` —
  compare generated asm.
- **Handler bodies that do more than a simple arithmetic/memory op** (GC
  alloc, exception raise, control flow) stay as separate method calls —
  the bank only buys us wins on the tight ops. That's fine: ~80% of
  opcodes fit the tight-op shape.

## Rollout plan

1. Prototype the register bank on a small slice — numeric ops only (~100
   ops) — measure frame size + fib throughput.
2. If the measurement confirms the win, extend to the full set: memory,
   table, reference, lane, SIMD.
3. Delete `SwitchRuntimeStackSize`, the worker `Thread` in
   `InvokeViaSwitch`, and any no-longer-needed depth tracking once the
   default-stack model works.
