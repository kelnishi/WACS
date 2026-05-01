# Cold-start latency across WACS runtimes

"Cold start" is the wall time from a process launching to the first wasm
function returning a value. It's the latency a CLI tool, a serverless
worker, or a game-engine plug-in pays before doing useful work — and
it's distinct from steady-state throughput.

WACS ships four execution paths. They all parse the same wasm and
implement the same semantics, but they make very different cold-start
trade-offs. A fifth path — statically linking a transpiled module's
.dll into a NativeAOT-published consumer — is achievable today with
hand-wired csproj plumbing and is the likely target of a future single-
shot `wacs aot` workflow.

The numbers below come from `Wacs.Bench/Coldstart.cs` (and a since-
removed `Wacs.Bench.Aot.Saved` experiment for the AOT-linked row),
running on macOS / arm64 / net8.0 Release, 2026-05-01. The
[Methodology](#methodology) section shows exactly how to reproduce each
column.

---

## The execution paths

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

### `transpiler-aot-linked` — statically link the transpiled .dll into a NativeAOT host

The pre-built .dll is referenced as a regular assembly from a NativeAOT-
publishing consumer csproj. NativeAOT's ILC pulls the transpiled wasm's
IL into the same single native binary as the host, so the wasm methods
become direct typed CLR calls in the final native code. No JIT, no
Reflection.Emit, no Assembly.Load, no `MethodInfo.Invoke`.

Cold-start path: `new Module()` (ctor — direct C# `new`) → typed
virtual call into the wasm function.

---

## Cold-start numbers

`fib(100)` — trivial body, exposes the per-runtime startup floor.

| runtime                  | JIT-only cold (µs) | R2R cold (µs) | NativeAOT cold (µs) |
|--------------------------|-------------------:|--------------:|--------------------:|
| interp-poly              |             35,676 |        24,336 |                 988 |
| interp-switch            |             45,230 |        16,396 |             **319** |
| transpiler               |             54,737 |        29,983 |       not supported |
| transpiler-saved         |              1,715 |           998 |       not supported |
| **transpiler-aot-linked** |              n/a |           n/a |             **501** |

NativeAOT only applies to runtimes without runtime IL emission/loading
— see [Build-time configuration](#build-time-configuration-what-users-can-opt-into)
below for why.

`fib(5,000,000)` — long inner loop, exposes per-op execution cost on
first call. Build configuration barely matters here because the inner
loop runs in already-tier-1 code regardless: R2R precompiles, JIT tiers
up almost immediately, AOT compiles ahead of time.

| runtime                  | JIT-only cold (µs) | R2R cold (µs) | NativeAOT cold (µs) |
|--------------------------|-------------------:|--------------:|--------------------:|
| interp-poly              |            453,920 |       492,535 |             459,964 |
| interp-switch            |            262,543 |       257,656 |             260,830 |
| transpiler               |              3,274 |         3,348 |       not supported |
| transpiler-saved         |              2,922 |         3,202 |       not supported |
| **transpiler-aot-linked** |              n/a |           n/a |           **2,628** |

Build-time cost for the saved-DLL path (paid once per module ever, not
at startup): transpile + Lokad.ILPack save = ~100 ms. The saved .dll is
4,608 bytes for a 242-byte wasm input — typical PE-format overhead.

Native binary sizes (single self-contained executable, macOS arm64):

| binary                   | size  |
|--------------------------|------:|
| `Wacs.Bench.Aot` (interpreter-only)              | 9.8 MB |
| `Wacs.Bench.Aot.Saved` (transpiler-aot-linked)   | 4.3 MB |
| `Wacs.Bench` self-contained R2R                  |  ~70 MB |
| Default JIT publish (managed assemblies + apphost) |  ~30 MB |

Smaller for the AOT-linked variant because trimming proves the
interpreter, parser, and transpiler emitter are all dead code — only
the specific transpiled functions plus the runtime support types
(`ThinContext`, `InitializationHelper`) survive.

---

## Where the time goes

Per-phase breakdown of trial-0 cold start, fib(100), JIT-only:

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

For `transpiler-aot-linked`, fib(100) cold trial 0 is just:

| activate (Module ctor) | first invoke | total |
|-----------------------:|-------------:|------:|
|                    501 |          0.1 |   501 |

The first-invoke time is genuinely sub-microsecond — no JIT, no
indirection, just the AOT-compiled fib loop running on bare arm64.
Verified with a result sink in the bench so the optimizer can't
elide the call.

What stands out:

- **The interpreters' "parse" and "inst" first-call numbers are 14–17 ms**
  not because parsing 242 bytes intrinsically takes that long, but because
  the parser + instantiator code is paying its own .NET tier-1 JIT cost
  the first time it runs in the process. Subsequent trials in the same
  process do parse + inst in 30–100 µs.

- **The switch interpreter's first invoke is ~14 ms** under JIT — that's
  RyuJIT promoting the prefix-split `TryDispatch_XX` methods to tier 1.
  After that, they execute at ~6 µs per fib(100) call, beating
  polymorphic's 50 µs by 8×. Under R2R or NativeAOT the tier-1 cost
  vanishes (R2R: 977 µs first invoke; AOT: 26 µs).

- **The in-process transpiler pays an extra ~25 ms for `xpile`** —
  Reflection.Emit warming up plus IL emission for fib's small functions.
  Once that's done, every invoke is sub-millisecond.

- **`transpiler-saved` skips parse, inst, and xpile entirely.** What
  remains is `AssemblyLoadContext.LoadFromStream` (41 µs), type
  resolution (44 µs), the module's init ctor (1.5 ms), and the first
  JITted invoke (136 µs).

- **`transpiler-aot-linked` is `transpiler-saved` minus the JIT and the
  load.** All the fib code is in the host process's native image at
  build time. Cold start is just the Module ctor's first-time class
  initialization (~500 µs trial 0, ~50 µs subsequent) plus an actual
  function call (~100 ns).

---

## Build-time configuration: what users can opt into

The "JIT-only" column above is what you get from a default
`dotnet publish -c Release` (or `dotnet run`). Every row improves under
**ReadyToRun (R2R)**, which precompiles the WACS managed code to native
during publish. The cost is binary size (`Wacs.Core.dll` grows from
1.5 MB to 2.9 MB) and platform-specific publish artifacts. For a
serverless or game-engine embedder where startup matters, the trade is
near-always worth it.

Per-phase, R2R changes:

| phase                                | improvement   | why |
|--------------------------------------|---------------|-----|
| interpreter `parse` first call       | ~2× faster    | no tier-1 JIT for the parser |
| interpreter `inst` first call        | ~2× faster    | same — instantiator is precompiled |
| `interp-switch` first invoke         | **~14× faster** | the giant `TryDispatch_XX` methods stop paying tier-1 JIT (this is the headline win) |
| `transpiler` `xpile` first call      | ~1.8× faster  | Reflection.Emit warmup |
| `transpiler-saved` cold total        | ~1.7× faster  | loader code precompiled; the loaded .dll is still pure IL though |
| any `subsequent` median              | unchanged     | already tier-1 in steady state |

**NativeAOT** (`Wacs.Bench.Aot/`, demonstrating the path) works today
for interpreter-only embedders and delivers the largest cold-start
improvement of all: no JIT in the process, no .NET runtime startup, a
single self-contained native binary (~10 MB on macOS arm64 vs ~30 MB
of managed assemblies + framework for a JIT publish).

The blast radius of dynamic codegen is genuinely contained:

- `Wacs.Compilation` is a Roslyn source generator
  (`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`).
  It runs at compile time inside csc.exe and is **not in the runtime
  output** — confirm with `ls Wacs.Bench/bin/Release/net8.0/ | grep
  Wacs.Compilation` (empty). Its netstandard2.0 target framework
  doesn't matter for runtime AOT.
- `Wacs.Core` itself is `IsAotCompatible=true` and uses dynamic APIs
  in exactly one place (`Activator.CreateInstance(hostType)` in
  `Runtime/Types/HostFunction.cs`, for binding host-function adapter
  classes — easy to make AOT-safe with a
  `[DynamicallyAccessedMembers]` annotation if needed).
- `Wacs.Transpiler.Lib` uses Reflection.Emit at *transpile* time, but
  its *runtime* support types (`ThinContext`,
  `InitializationHelper`) are AOT-compatible — that's why
  `transpiler-aot-linked` works.
- `Wacs.ComponentModel` uses `MakeGenericMethod` + `DispatchProxy`.
  AOT-incompatible.
- The `transpiler-saved` runtime path uses `AssemblyLoadContext
  .LoadFromStream` to load IL that needs a JIT — also AOT-
  incompatible (NativeAOT processes have no JIT). The `transpiler-
  aot-linked` path avoids this by linking the .dll at build time.

So an AOT WACS embedding includes the parser + both interpreters +
WASIp1 + host-binding APIs, plus optionally a build-time-transpiled
wasm linked statically. It excludes the in-process transpiler,
component-model, and runtime saved-DLL loading.

The MSBuild trick to make AOT publish work: put `<PublishAot>true
</PublishAot>` **inside the consumer's csproj**, not on the CLI
(`-p:PublishAot=true`). As a CLI global property, AOT propagates into
every referenced project's MSBuild evaluation, which trips
`NETSDK1207` on the source generator's netstandard2.0 leg. As a
csproj-local property, MSBuild scopes it correctly. See
`Wacs.Bench.Aot/Wacs.Bench.Aot.csproj` for the working incantation.

**IL trimming** (`-p:PublishTrimmed=true`) is automatically enabled by
NativeAOT — the published interpreter-only binary above is also
trimmed. Standalone trimming without AOT works for the same
interpreter-only subset; the transpiler and component-model paths
need per-call-site `[DynamicallyAccessedMembers]` annotations to be
trim-safe (tracked as future work).

For "what users can ship today":

- **R2R** for any embedding (works with all four runtimes).
- **NativeAOT** for interpreter-only embeddings (component-model and
  in-process-transpiler users stay on R2R or JIT).
- **NativeAOT + linked transpiled .dll** for the absolute floor on
  cold-start *and* steady-state, when the wasm is known at build
  time. Manual plumbing today; tracked for `wacs aot` automation.

---

## Picking a runtime

| Embedding                                           | Recommendation        | Build flag |
|-----------------------------------------------------|-----------------------|------------|
| Short-lived CLI invocation, ad-hoc wasm             | `interp-poly`         | NativeAOT  |
| Long-running host, hot loops in wasm                | `interp-switch`       | NativeAOT  |
| Build pipeline can run wacs-transpile               | **`transpiler-saved`** | R2R       |
| Test/dev loop, no .dll in source control            | `transpiler` (in-process) | R2R    |
| Serverless / edge / cold-boot-sensitive (no transpiler) | `interp-switch`   | **NativeAOT** |
| Serverless / edge / cold-boot-sensitive (transpiler ok) | **`transpiler-saved`** | R2R |
| Game engine, plug-ins shipped pre-transpiled        | **`transpiler-saved`** | R2R       |
| Game engine, IL2CPP-style AOT (iOS/console)         | `interp-switch`       | NativeAOT  |
| Embedded wasm known at host build time, every µs counts | **`transpiler-aot-linked`** | **NativeAOT** |

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

## Methodology

Each cell in the tables above was measured with a separate command.
Reproduce in this order on macOS arm64 / .NET 8 (Linux x64 and Windows
x64 differ in absolute terms but the per-runtime ratios hold).

### JIT-only baseline (all four runtimes)

```bash
dotnet run -c Release --project Wacs.Bench -- coldstart
```

Spawns one fresh child process per runtime via
`Process.Start(Environment.ProcessPath)`. Each child runs 7 trials of
its assigned runtime: trial 0 is reported as "first," the median of
trials 1–6 as "subsequent." Workload is `Wacs.Bench/fib.wasm` (242
bytes), called with `fib(100)` (startup-dominated) and
`fib(5_000_000)` (execution-dominated).

A `coldstart in-process` mode runs everything in one CLR (cheaper but
shared-JIT-warmup makes only the first runtime's "first" column honest).

### R2R (all four runtimes)

```bash
dotnet publish Wacs.Bench -c Release -r osx-arm64 \
    --self-contained -p:PublishReadyToRun=true -o /tmp/wacs-r2r
/tmp/wacs-r2r/Wacs.Bench coldstart
```

`-r osx-arm64` is platform-specific — substitute `linux-x64` or
`win-x64` as needed. `--self-contained` is required so the published
output ships its own R2R-precompiled framework runtime; without it,
only the WACS code is R2R-precompiled while the BCL falls back to its
upstream R2R image (which is the JIT-only baseline already).

### NativeAOT (interpreter-only)

```bash
dotnet publish Wacs.Bench.Aot -c Release -r osx-arm64 -o /tmp/wacs-aot
/tmp/wacs-aot/Wacs.Bench.Aot
```

`Wacs.Bench.Aot/` is a separate consumer that references only
`Wacs.Core` (no transpiler, no component-model) and sets
`<PublishAot>true</PublishAot>` inside its csproj. Output is a
self-contained ~10 MB native binary. Measures the same fib(100) +
fib(5M) workload across `interp-poly` and `interp-switch`.

### NativeAOT + linked transpiled .dll (`transpiler-aot-linked`)

This isn't yet a one-shot user workflow. The numbers in the tables come
from a manually-wired experiment that has since been removed from the
repo. To replicate:

```bash
# 1) Produce the saved .dll. The transpiler currently appends a
#    _<uniqueId> suffix to the assembly name, which makes static
#    references brittle. Capture the actual name from the output.
dotnet run -c Release --project Wacs.Console -- build app.wasm --out app.dll

# 2) Inspect the assembly's logical name (e.g. "Wacs.Transpiled.App_1")
#    and rename the file on disk to match — ILC's resolver requires
#    file name == assembly name.
mv app.dll Wacs.Transpiled.App_1.dll

# 3) Create a tiny consumer csproj like Wacs.Bench.Aot's, with these
#    additions:
#
#      <ProjectReference Include="..\Wacs.Transpiler.Lib\..."
#                        UndefineProperties="PublishAot;PublishTrimmed;..." />
#      <Reference Include="Wacs.Transpiled.App_1">
#        <HintPath>$(MSBuildThisFileDirectory)Wacs.Transpiled.App_1.dll</HintPath>
#        <Private>true</Private>
#      </Reference>
#
# 4) In Program.cs:
#      using Wacs.Transpiled.App;
#      var m = new Module();
#      m.SomeExport(arg);
#
# 5) Publish.
dotnet publish MyHost -c Release -r osx-arm64 -o /tmp/myhost
```

The plumbing pain (steps 1–3) is exactly what a future `wacs aot`
subcommand should automate — see the followup tracked in this
project's notes for the three concrete blockers
(`ModuleTranspiler` stable assembly naming, filename = assembly name
emit convention, MSBuild target).

### Bench code locations

- Top-level dispatcher: `Wacs.Bench/Program.cs`
- Coldstart logic and per-runtime drivers: `Wacs.Bench/Coldstart.cs`
- AOT bench (interpreter-only): `Wacs.Bench.Aot/`
- Steady-state dispatch microbench (orthogonal to coldstart):
  `Wacs.Bench/BASELINE.md`

---

## Caveats

- These are wall-clock numbers from a single machine on a single day.
  Treat the *ratios* as durable; treat the absolute microseconds as
  approximate.
- The bench's R2R numbers come from `dotnet publish -r osx-arm64
  --self-contained -p:PublishReadyToRun=true`. R2R is platform-
  specific — Linux x64 and Windows x64 numbers will differ in absolute
  terms but the per-runtime story should hold. The framework BCL
  always ships R2R'd, so even the "JIT-only" rows already benefit
  from precompiled `System.*`.
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
- `transpiler-aot-linked` numbers were measured with a result sink
  (`Sink.V += instance.fib(arg);`) so NativeAOT's optimizer couldn't
  elide the call as dead code. The sink showed real fib values
  (e.g. -13,721,502,550 summed across trials of fib(100), which
  overflows i32 as wasm fib does after ~46 iterations).
