# Register-Program (`WacsCode.RegProg`) — POC Design Notes

## What this is

A prototype super-op that encodes an arbitrary-depth pure-arith
expression subtree as inline register-machine bytecode, executed by a
single handler (`RegProgHandler.Execute`). Intent: close the gap
between `StreamFusePass`'s fixed-depth fusion (bounded to hand-coded
2-3 op patterns like `I32LLAdd`) and WacsCode-on-the-polymorphic-side,
which rolls arbitrary expression trees into `InstAggregate` wrappers.

## Status: **skeleton only, not wired into compilation**

- `Wacs.Core.Instructions.RegProgHandler` — the runtime handler. Reads
  the microop bytecode, executes it against an 8-slot `stackalloc`
  register file, pushes outputs to the OpStack.
- `Wacs.Core.OpCodes.WacsCode.RegProg = 0x80` — the opcode assignment.
- `Wacs.Compilation.DispatchGenerator` — emits a case in the 0xFF
  prefix sub-dispatcher that calls `RegProgHandler.Execute`.
- **NOT built**: the transpile-time auto-detector that would walk the
  annotated bytecode stream, identify maximal pure-arith subtrees,
  register-allocate to the 8-slot window, and emit `RegProg` super-ops
  in place of the matched sequence.

A transpiled module today never produces `RegProg` bytecode, so the
handler is dead code. It's committed as infrastructure — if a future
workload motivates building the auto-detector, the runtime plumbing is
already in place.

## Microop ISA

All microops are byte-packed, variable length. Register indices are
u8, range 0–7. Immediates are LE as per the rest of the annotated
stream format.

| Tag | Microop | Encoding (following tag byte) |
|---|---|---|
| 0x01 | `const.i32` | `dst:u8, imm:s32` |
| 0x02 | `local.get` | `dst:u8, idx:u32` |
| 0x10 | `i32.add` | `dst:u8, a:u8, b:u8` |
| 0x11 | `i32.sub` | same |
| 0x12 | `i32.mul` | same |
| 0x13 | `i32.and` | same |
| 0x14 | `i32.or`  | same |
| 0x15 | `i32.xor` | same |
| 0x16 | `i32.shl` | same |
| 0x17 | `i32.shr_s` | same |
| 0x18 | `i32.shr_u` | same |
| 0x20 | `i32.eq`  | same |
| 0x21 | `i32.ne`  | same |
| 0x22–0x29 | `i32.lt_s..ge_u` | same |

Extension sketch: i64 variants at 0x30–0x49, local.set at 0x03, f32/f64
at 0x50+, memory loads/stores at 0x60+. The microop alphabet is
intentionally small to keep the inner switch's jump table cache-hot
and the JIT's code-gen tractable.

## Stream format (after the outer `0xFF 0x80` prefix + secondary)

```
u8  nInputs           # values popped off OpStack into regs[0..nInputs-1]
u8  nOutputs          # values pushed from regs[outputRegs[i]] in order
u16 microByteCount    # length of the inner bytecode
u8[nOutputs] outputRegs
u8[microByteCount] microBytecode
```

Lifted onto `stackalloc Span<ulong> regs` in the handler; no per-call
allocation.

## POC benchmark (Wacs.Bench/RegProgBench)

Measures an 11-op register program vs an equivalent 17-op stack
program running the same arithmetic (`((a+b)*(c-d)) + a*c + b - d`),
10M iterations, M3 Max .NET 8:

| Program | Outer (ms) | RegProg (ms) | Ratio |
|---|---:|---:|---:|
| 7 steps  | 282 | 262 | 0.93× |
| 21 steps (3×) | 263 | 247 | 0.94× |
| 35 steps (5×) | 306 | 262 | **0.86×** |

The "outer" side is a 5-case switch, not the real 172-case WACS
dispatcher. In the real dispatcher — higher register pressure, bigger
jump table, documented per-op spills of `_pc` / `_stackCount` — the
gap should widen. Plausible real-world: 1.5–2× at depth ≥ 5.

## Design pitfalls tested

### Named-local register file + switch-over-index reads — fails.

First POC tried `ulong r0..r7` as method locals + a 9-case switch
inside `ReadReg`/`WriteReg` helpers to select by u8 register index.
**1.69× slower** than the outer dispatch on the same program. The
per-register-access switch-branch tree dominated, and the C# JIT
couldn't pin the named locals to CPU registers anyway because the
switch-based reads touch them by reference.

### `stackalloc Span<ulong>` register file — works.

Second POC uses `Span<ulong> regs = stackalloc ulong[8]`. Indexed
access compiles to a single bounds-checked `mov [rsp+idx*8]`. 0.86×
(14% faster) at depth 35.

Register file stays memory-backed (not pinned to CPU registers), but
it's in L1 and adjacent to other hot locals. The advantage over the
outer OpStack is that:

- The register file is **8 slots** — smaller, denser, tighter cache
  footprint than the 16 KB `_registers` OpStack array.
- Encoding is **byte-packed with immediate operand registers** —
  shorter stream, fewer branches to decode immediates.
- **No `_stackCount` bookkeeping** per op — the microop body just
  indexes registers directly.

## Why the auto-detector isn't built yet

1. The POC win is modest — 6–14% on pure-arith sequences, 3–7%
   estimated on full CoreMark. Not enough to justify ~1000 lines of
   stream-rewriter + register allocator + integration test surface.
2. `StreamFusePass` already collapses the common 2–3-op patterns
   (most of the arith fusions we see in compiled wasm).
3. The real interpreter-tier users (IL2CPP / PublishAot targets that
   can't use `-t`) are already on `--switch --super` accepting
   "interpreter speed" as the AOT-safe tradeoff.

If a future workload shows up where `-t` isn't an option and `--switch
--super` is the bottleneck, the infrastructure is in place to build
the auto-detector on top.

## If building the auto-detector later

Rough plan:

1. **Walk the post-fusion annotated stream.** Skip branches, calls,
   traps, memory ops, GC ops — these terminate a subtree.
2. **Maximal pure-arith subtree identification.** Track the WASM stack
   depth symbolically as we walk. Candidate subtree starts at a
   "pressure 0" point and extends while:
   - Each op is in the RegProg-supported subset.
   - Peak stack pressure ≤ 8 (fits the register window).
   - No branch target lands inside the subtree.
3. **Register allocation.** Simple: stack depth → register index.
   Allocate regs as the walker pushes, free as it pops. Abort the
   subtree if depth exceeds 8.
4. **Emit.** Write `0xFF 0x80` + header + microops into a new stream
   buffer; remap branch targets per the existing `StreamFusePass`
   discipline.
5. **Threshold.** Only activate the rewrite if the matched subtree is
   ≥ 4 ops, otherwise leave the sequence as-is — short subtrees are
   already served by `StreamFusePass` at similar or better efficiency.

Any integration needs full spec-suite coverage before shipping — the
handler has had no real-workload validation.
