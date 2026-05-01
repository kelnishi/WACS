# Cold-start latency across WACS runtimes

"Cold start" is the wall time from a process launching to the first wasm
function returning a value. It's the latency a CLI tool, a serverless
worker, or a game-engine plug-in pays before doing useful work — and
it's distinct from steady-state throughput.

WACS ships four execution paths. They all parse the same wasm and
implement the same semantics, but they make very different cold-start
trade-offs. This doc walks through where the time goes in each, and
how to pick one for a given embedding.

The numbers below come from `Wacs.Bench/Coldstart.cs` running on
macOS / arm64 / net8.0 Release, 2026-05-01. Reproduce with:

```bash
dotnet run -c Release --project Wacs.Bench -- coldstart
```

The bench spawns one fresh child process per runtime (so the .NET CLR +
shared parser/runtime JIT cost is paid honestly by each), then runs 7
trials of each: trial 0 is reported as "first," the median of trials 1–6
as "subsequent." Workload is `fib.wasm` (242 bytes, no imports), called
with `fib(100)` (startup-dominated) and `fib(5_000_000)` (execution-
dominated).

---

## The four runtimes

### `interp-poly` — the default polymorphic interpreter

`WasmRuntime.InstantiateModule` walks the parsed module, allocates
memories/tables/globals, and produces an executable `ModuleInstance`.
Each opcode dispatches through a virtual call into a small per-op
`InstructionBase.Execute` method. The CLR JIT inlines these
aggressively after a few invocations.

Cold-start path: `ParseWasm` → `InstantiateModule` →
`CreateStackInvoker` → invoke.

### `interp-switch` — the monolithic switch interpreter

Same parse + instantiate, but execution dispatches through a single
`TryDispatch` switch over the opcode space, prefix-split into per-prefix
methods (`TryDispatch_00`, `TryDispatch_FB`, `TryDispatch_FC`,
`TryDispatch_FD`). Hot loops run faster steady-state because all
operands live in a single register-bank frame, but each per-prefix
method is huge — first invocation pays a noticeable JIT-tier-1 cost.

### `transpiler` — in-process AOT to a dynamic .NET assembly

`Wacs.Transpiler.Lib`'s `ModuleTranspiler.Transpile` walks each function
and emits CIL via `System.Reflection.Emit` into an
`AssemblyBuilder(Run)`. The result is a real .NET type with one method
per wasm function. From there it's a regular CLR object — `Activator
.CreateInstance(ModuleClass)` runs the module's init, and `MethodInfo
.Invoke` calls into JITted IL.

Cold-start path: `ParseWasm` → `InstantiateModule` → `Transpile` →
`Activator.CreateInstance` → invoke.

### `transpiler-saved` — load a pre-built .dll

The transpiler can persist its dynamic assembly to a portable PE file
via `result.SaveAssembly(path)` (Lokad.ILPack under the hood). At run
time, the embedder loads that .dll into an `AssemblyLoadContext`, finds
the `Module` class, instantiates it, and invokes — no parse, no
instantiate, no IL emission.

Cold-start path: `LoadFromStream` → resolve types → `Activator
.CreateInstance` → invoke.

---

## Cold-start numbers

`fib(100)` — trivial body, exposes the per-runtime startup floor.

| runtime            | cold (µs) | subsequent median (µs) |
|--------------------|----------:|----------------------:|
| interp-poly        |    35,676 |                   155 |
| interp-switch      |    45,230 |                   129 |
| transpiler         |    54,737 |                   572 |
| **transpiler-saved** |  **1,715** |                  **458** |

`fib(5,000,000)` — long inner loop, exposes per-op execution cost on
first call.

| runtime            | cold (µs) | subsequent median (µs) |
|--------------------|----------:|----------------------:|
| interp-poly        |   453,920 |               356,601 |
| interp-switch      |   262,543 |               261,326 |
| transpiler         |     3,274 |                 3,254 |
| **transpiler-saved** |  **2,922** |                **3,006** |

Build-time cost (paid once per module ever, not at startup): transpile +
Lokad.ILPack save = ~100 ms. The saved .dll is 4,608 bytes for a
242-byte wasm input — typical PE-format overhead.

---

## Where the time goes

Per-phase breakdown of trial-0 cold start, fib(100):

| runtime          | parse | inst  | xpile  | activate | first invoke | total  |
|------------------|------:|------:|-------:|---------:|-------------:|-------:|
| interp-poly      | 16,269 | 17,114 |     — |       — |        2,293 | 35,676 |
| interp-switch    | 14,383 | 16,922 |     — |       — |       13,926 | 45,230 |
| transpiler       | 14,062 | 14,541 | 24,766 |    1,275 |           93 | 54,737 |
| transpiler-saved |     41 |     44 |     — |    1,494 |          136 |  1,715 |

(For `transpiler-saved` the columns map to: load = `Assembly
.LoadFromStream`, resolve = type/method lookup, activate = `Module`
ctor + `InitializationHelper`. There is no parse or xpile because the
build did them already.)

What stands out:

- **The interpreters' "parse" and "inst" first-call numbers are 14–17 ms,
  not because parsing 242 bytes intrinsically takes that long, but because
  the parser + instantiator code is paying its own .NET tier-1 JIT cost
  the first time it runs in the process.** Subsequent trials in the same
  process do parse + inst in 30–100 µs.

- **The switch interpreter's first invoke is ~14 ms** — that's RyuJIT
  promoting the prefix-split `TryDispatch_XX` methods to tier 1. After
  that, they execute at ~6 µs per fib(100) call, beating polymorphic's
  50 µs by 8×. This is the trade: cheap steady state, expensive first
  call.

- **The in-process transpiler pays an extra ~25 ms for `xpile`** —
  Reflection.Emit warming up plus IL emission for fib's small functions.
  Once that's done, every invoke is sub-millisecond. The trade-off only
  pays back if you're doing real work (fib(5M) goes from 454 ms cold on
  poly to 3.3 ms cold on the transpiler — **138× faster**).

- **`transpiler-saved` skips parse, inst, and xpile entirely.** What
  remains is `AssemblyLoadContext.LoadFromStream` (41 µs), type
  resolution (44 µs), the module's init ctor (1.5 ms), and the first
  JITted invoke (136 µs). Total cold: 1.7 ms — **20× faster than the
  fastest interpreter**, **32× faster than in-process transpile**.

---

## Picking a runtime

| Embedding                                           | Recommendation        |
|-----------------------------------------------------|-----------------------|
| Short-lived CLI invocation, ad-hoc wasm             | `interp-poly`         |
| Long-running host, hot loops in wasm                | `interp-switch`       |
| Build pipeline can run wacs-transpile               | **`transpiler-saved`** |
| Test/dev loop, no .dll in source control            | `transpiler` (in-process) |
| Serverless / edge / cold-boot-sensitive             | **`transpiler-saved`** |
| Game engine, plug-ins shipped pre-transpiled        | **`transpiler-saved`** |

The two interpreters have effectively the same cold-start floor (~35–45
ms on a tiny module, mostly .NET JIT warmup); pick between them based
on steady-state hotness, not cold start. Once you decide you can absorb
~25 ms of in-process xpile or move it to build time, the transpiler is
the right answer for anything that runs more than trivial work.

For the "I can transpile at build time" path, the workflow is:

```bash
# Build-time: produce the .dll once.
wacs transpile --input app.wasm --output app.dll

# Runtime: load + invoke. ~1.7 ms cold to first call.
var alc = new AssemblyLoadContext("app", isCollectible: true);
var asm = alc.LoadFromAssemblyPath("app.dll");
var module = TranspiledModuleLoader.Load(asm);
module.InvokeExport("_start", Array.Empty<Value>());
```

`TranspiledModuleLoader` (in `Wacs.Transpiler.Lib/Hosting/`) handles
finding the `Module` type, wiring imports, and turning exports into
typed delegates.

---

## Caveats

- These are wall-clock numbers from a single machine on a single day.
  Treat the *ratios* as durable; treat the absolute microseconds as
  approximate.
- "Cold" here means a fresh OS process, but a warm OS file cache. If
  your wasm or .dll isn't already in cache, add disk read time.
- The bench uses fib's 242-byte module. Larger modules shift the
  balance: parse and instantiate scale with module size, while the
  one-time JIT-warmup floor stays fixed. For big real-world modules
  (multi-MB), parse + inst can run into hundreds of milliseconds on
  the interpreters, making the transpile-once-load-many path even
  more attractive.
- `transpiler-saved`'s "subsequent" numbers in the bench use a fresh
  `AssemblyLoadContext` per trial so the load isn't cached, but they
  still benefit from in-process JIT warmup of the loader code itself.
  True per-process cold start in the bench is the trial-0 column.
