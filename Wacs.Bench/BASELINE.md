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
