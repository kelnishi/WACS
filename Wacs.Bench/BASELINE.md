# Switch runtime — current-state baseline

Captured 2026-04-18, pre-register-bank refactor. macOS / arm64 / net8.0 Release.

## Dispatcher shape at this point
- `TryDispatch(ctx, code, ref pc, uint op)` — monolithic switch over ~300 cases.
- Each case declares its own named locals for stack pops + immediates, then
  calls a static local function containing the handler body.
- `SwitchRuntime.Run(ctx, func)` loops calling `TryDispatch` per op.
- Top-level `InvokeViaSwitch` spawns a dedicated `Thread(…, 32 MiB)` per
  invocation (workaround for the ~20 KiB/activation frame size of
  `TryDispatch`).

## fib.wat micro-benchmarks (5M iteration inner loops, 3-repeat sum)

| workload        | polymorphic | switch   | ratio   |
|-----------------|-------------|----------|---------|
| fib-iter(5M)    | 1080 ms     | 24419 ms | **22.6×** |
| fib-rec(25)     |  123 ms     |   548 ms | **4.5×**  |
| sum(5M)         |  878 ms     | 19726 ms | **22.5×** |

### Post register-bank refactor

Same benchmark after hoisting stack-operand + immediate locals out of
each case into a shared bank declared at `TryDispatch` entry (see
`README-RegisterBankDispatch.md`).

| workload        | polymorphic | switch   | ratio   | vs baseline |
|-----------------|-------------|----------|---------|-------------|
| fib-iter(5M)    | 1214 ms     | 11255 ms | **9.3×**  | 2.4× faster |
| fib-rec(25)     |  128 ms     |   349 ms | **2.7×**  | 1.7× faster |
| sum(5M)         |  964 ms     | 10695 ms | **11.1×** | 2.0× faster |

Spec-test suite wall time dropped 34s → 15s as well (2.3×).

### Post `[MethodImpl(AggressiveInlining)]` on OpStack Push/Pop

Inlined the `OpStack.PushI32`/`PopI32`/etc. hot-path methods. Medians
over 3 runs:

| workload        | polymorphic | switch   | ratio    |
|-----------------|-------------|----------|----------|
| fib-iter(5M)    | 1055 ms     | 12100 ms | **11.5×** |
| fib-rec(25)     |  119 ms     |   330 ms | **2.8×**  |
| sum(5M)         |  857 ms     | 13200 ms | **15.4×** |

**Mixed result.** Polymorphic gained ~13% (1214 → 1055 on fib-iter) —
the expected inlining win on small `Execute` methods. Switch runtime
*regressed* on fib-iter/sum (11255 → 12100, 10695 → 13200) while still
improving fib-rec slightly.

Hypothesis: inlining Push/Pop bloats the already-huge `TryDispatch`
method past a JIT size/budget threshold (register-allocation quality,
loop cloning, tiered-JIT behaviour) — net loss on the monolithic
switch path, net win on polymorphic's many-small-methods path.
Confirmed by the next step below.

### Post prefix-split of `TryDispatch`

The monolithic switch was split by prefix byte (0x00 / 0xFB / 0xFC /
0xFD — the primary-opcode families) into per-prefix `TryDispatch_XX`
methods, each with its own register-bank + immediate-union declaration.
The outer `TryDispatch` becomes a 5-way switch that tail-calls into one
of them. No change in semantics, same 237/237 tests, same dispatch-key
format.

Medians over 3 runs:

| workload        | polymorphic | switch  | ratio    |
|-----------------|-------------|---------|----------|
| fib-iter(5M)    | 1063 ms     | 1204 ms | **1.13×** |
| fib-rec(25)     |  117 ms     |  122 ms | **1.04×** |
| sum(5M)         |  869 ms     |  911 ms | **1.05×** |

**~10× speedup from this single change** on iterative workloads
(11.5× → 1.13×). Spec suite dropped from 15s to 9s in parallel.

Confirms the JIT-budget hypothesis: the 300+-case switch was past a
threshold that caused register-allocation / dispatch-table / loop-
opt decisions to fall off a cliff. Giving each prefix its own smaller
method (~170 cases for primary, ~140 for 0xFD SIMD, ~20 each for the
others) puts every sub-method back under the JIT's optimization budget.

The switch runtime is now effectively at parity with polymorphic on
tight loops (within 5-13%). Remaining gap is likely just the outer
`TryDispatch` prefix-switch call overhead — future work could inline
the prefix switch into `SwitchRuntime.Run` directly.

### Tried and reverted: void return from TryDispatch

Replaced `bool TryDispatch` (+ caller `if (!...) throw`) with
`void TryDispatch` (+ `default: throw new NotSupportedException(...)`).
Idea was to kill the per-op bool-check branch in the caller — the
default case is unreachable in practice (all opcodes covered).

Regressed by ~20%:

| workload        | with bool | with void |
|-----------------|-----------|-----------|
| fib-iter(5M)    | **1.13×** | 1.35×     |
| fib-rec(25)     | **1.04×** | 1.10×     |
| sum(5M)         | **1.05×** | 1.31×     |

Likely cause: the `throw new NotSupportedException($"...{op:X6}...")`
in `default:` brings an interpolated-string builder + exception ctor
into the method's CFG. RyuJIT adapts its code layout / register
allocation to accommodate the throwing path — even though it's never
taken, the analysis changes enough to hurt the non-throwing cases.

Reverted. The bool-check + per-op branch is demonstrably cheaper than
dragging exception machinery into the method body, at least with
RyuJIT's current heuristics.

Wall-clock comparisons only — not iterations/sec, but the ratios are what
matters.

### Reading the numbers

- **Tight hot loops (fib-iter, sum) are 22× slower.** ~30M ops took 24s =
  ~800 ns/op on the switch path vs ~30 ns/op polymorphic. That's the cost
  we most want to cut.
- **Recursion-dominated (fib-rec) is "only" 4.5× slower.** Call/frame
  setup is comparable across both runtimes — the hot-loop body's tax is
  diluted by the setup cost per call. Still measurable; still worth
  fixing.
- **Spec-test ratio was ~5×.** Spec tests are invoke-heavy (thousands of
  tiny functions), so the per-invoke worker-`Thread` overhead dominates
  instead of per-op dispatch. The register-bank refactor should remove
  the `Thread` along with shrinking the per-op cost.

### Hypothesized causes (to verify post-refactor)

1. **Per-op function call overhead.** `Run` calls `TryDispatch` once per
   op, each call goes through full managed-function prologue/epilogue.
   Polymorphic dispatches via virtual-method call into small per-op
   `Execute` methods — also a call, but the JIT can devirtualize and
   inline aggressively.
2. **Monolithic method frame.** `TryDispatch` has ~20 KiB of stack locals
   (the union of every case's named temps). Allocated once per `Run`
   entry, but every case writes to the frame — cold stack slots in L1.
3. **Local function marshalling.** Each case does `PopI32() → local → pass
   to __Op(...)`. The local-function parameter slots are distinct from the
   case's named pops; the JIT has to move values between them.

The register-bank refactor addresses (2) directly (small fixed frame) and
(3) by eliminating the local-function layer. (1) requires either threaded
dispatch or inlining `TryDispatch` into `Run`.
