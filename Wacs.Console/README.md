# WACS

The unified WebAssembly toolchain for .NET. One CLI — `wacs` — covers
running, compiling, and inspecting WebAssembly modules and components,
backed by the [WACS](https://www.nuget.org/packages/WACS) interpreter
and the
[WACS.Transpiler.Lib](https://www.nuget.org/packages/WACS.Transpiler.Lib)
AOT engine.

> **Note:** This tool supersedes
> [`wasm-transpile` (`WACS.Transpiler`)](https://www.nuget.org/packages/WACS.Transpiler).
> The legacy package is deprecated; install `WACS.Cli` instead. The
> package id is `WACS.Cli` (the bare `WACS` id is the runtime library,
> [`Wacs.Core`](https://www.nuget.org/packages/WACS)); the tool command
> users type is `wacs`.

## Installation

```bash
dotnet tool install -g WACS.Cli
wacs --help
```

## Verb structure

`wacs` uses a verb-based subcommand layout (matches `wasmtime` /
`wasmer` industry precedent — keeps "run" flags from cluttering the
"compile" surface):

| Verb | Purpose | Engine |
|---|---|---|
| `run` | Execute a `.wasm` module | interpreter (default) or transpiler |
| `build` | Transpile to a `.dll` | transpiler |
| `inspect` | Diagnostics: WAT dump, stats, exports/imports | parse-only |

### Direct-run shortcut

If the first argument is a `.wasm` / `.wat` file path that exists,
`wacs` defaults to the `run` verb:

```bash
wacs my.wasm                            # → wacs run my.wasm
wacs build app.wasm -o app.dll          # explicit verb
wacs inspect app.wasm --stats           # explicit verb
```

Verb keywords (`run` / `build` / `inspect` / `help` / `--help` / `-h`
/ `version` / `--version`) bypass the shortcut. Anything else that
isn't a verb keyword and doesn't look like a wasm path is treated as a
verb name (so a typo gives a parse error rather than running the
wrong file).

## Engine choice

`--engine interpreter` (default for `run`) parses + executes via the
WACS interpreter — the AOT-safe path with full instrumentation hooks
(gas counter, dotTrace bracket, per-instruction logging, stats).

`--engine transpiler` JITs the module to .NET IL via
`Reflection.Emit`, then runs through CLR-native dispatch with imports
proxied back to the interpreter (mixed-mode). Roughly **64×** the
interpreter's throughput on compute-bound workloads (CoreMark on M3
Max: 17 552 iter/s vs 274 for the polymorphic interpreter — see the
root [README](https://github.com/kelnishi/WACS#expected-runtime-performance)
for the full table).

`build` always uses the transpiler — its job is to produce a `.dll`.

`run` and `build` both auto-detect component vs core wasm via the
layer header byte; component-mode routing happens transparently
when you pass `.component.wasm` input.

## `wacs run` — execution

```
wacs run [files]... [options]              [-- argv...]
```

### Examples

**Run with `_start` (WASI command):**

```bash
wacs run app.wasm -e PATH=/usr/bin -d ./data
# WASI Preview 1: env vars, preopened directory, _start dispatch
```

**Invoke a specific export with arguments:**

```bash
wacs run module.wasm --call add -- 7 35
# → Result:[i32=42]
```

Trailing args after `--` are forwarded to the chosen export (parsed
as the function's wasm parameter types) or to WASI argv when
running `_start`.

**Run a component with WASI Preview 2 (direct-linked):**

```bash
wacs run app.component.wasm --wasip2
# auto-routes to transpiler engine + WasiPreview2Bundle when
# --wasip2 / --host-package is set (those flags only make sense
# with the bundle path)
```

**Multi-module composition via ModuleLinker:**

```bash
wacs run a.wasm b.wasm --call quadruple -- 7
# → 28   (B's quadruple → A's double, twice, via shared runtime)
```

Each input registers under its filename basename so cross-module
imports resolve through the runtime's binding table. The chosen
export runs on the **last** input.

**Through the AOT transpiler (mixed-mode):**

```bash
wacs run app.wasm --engine transpiler --wasi
# transpiles in-process via Reflection.Emit, then runs through
# CLR-native dispatch with WASI imports proxied back to the
# interpreter
```

**Profile a hot path:**

```bash
wacs run app.wasm --profile
# JetBrains dotTrace measure-profiler bracket; snapshot lands
# in the OS-default profiler temp dir
```

**Instrumented runs (interpreter only):**

```bash
wacs run app.wasm --gas-limit 1000000 --log-gas
# trap if total instructions exceed 1M; print final count

wacs run app.wasm --log-execution Calls --calculate-lines
# log every call instruction with its source line number

wacs run app.wasm --stats Function
# per-function instruction counts after the run
```

**Custom host bindings:**

```bash
wacs run app.wasm --bind ./MyGameHost.dll
# load + activate every IBindable in MyGameHost.dll, wire into runtime
```

### `run` flag reference

| Flag | Default | Notes |
|---|---|---|
| `--call <export>` | `_start` | Function to invoke. Args after `--` are parsed per its wasm signature. |
| `--engine` | `interpreter` | `interpreter` or `transpiler` (Reflection.Emit AOT, mixed-mode imports). |
| `-m, --module <name>` | `_` | Name to register the instantiated module under. |
| `-e, --env K=V` | — | WASI Preview 1 environ. Repeat or comma-separate. |
| `-d, --dir <path>` | — | WASI Preview 1 preopen. Repeat or comma-separate. |
| `--wasi` | off | Bind WASI Preview 1 host imports. |
| `--bind <asm>` | — | Load `IBindable` host packages. Repeat or comma-separate. |
| `--host-package <name>` | — | Component-mode `[WitSource]` host package(s). |
| `--wasip2` | off | Shorthand `--host-package Wacs.WASI.Preview2`. |
| `--profile` | off | JetBrains dotTrace measure-profiler session. |
| `--log-gas` | off | Print total instructions executed. |
| `--gas-limit <N>` | 0 (∞) | Trap if instructions exceed N. |
| `--log-progress <N>` | -1 (off) | Print `.` every N instructions. |
| `--log-execution <flags>` | None | `None\|Computes\|Calls\|Branches\|Memory\|All`. |
| `--calculate-lines` | off | Line-number mapping for instruction logs. |
| `--stats <detail>` | None | `None\|Total\|Instruction\|Function`. |
| `--super` | off | Super-instruction fusion (interpreter only). |
| `--switch` | off | Source-generated switch runtime (interpreter only). |
| `--simd` | scalar | `--engine transpiler` SIMD strategy: interpreter \| scalar \| intrinsics. |
| `--no-tail-calls` | off | `--engine transpiler` only. |
| `--max-fn-size <N>` | 0 | `--engine transpiler` only. Skip large fns. |
| `--data-storage` | compressed | `--engine transpiler` only: compressed \| raw \| static. |
| `--no-validate` | off | Skip module validation after parse. |
| `-v, --verbose` | off | Parser timing + diagnostics on stderr. |

## `wacs build` — transpile to `.dll`

```
wacs build [files]... -o <output> [options]
```

### Examples

**Single-file core wasm:**

```bash
wacs build app.wasm -o app.dll
```

**Multi-file linker composition:**

```bash
wacs build a.wasm b.wasm -o b.dll
# → wrote a.dll, b.dll  (siblings land at <basename>.dll alongside)
```

**Component with WASI Preview 2 + runnable Main:**

```bash
wacs build app.component.wasm --wasip2 --emit-main \
    --entry-point greet -o app.dll
# Component-mode: --wasip2 resolves WASI imports to inline IL
# (no delegate hop). --emit-main bakes Program.Main(string[])
# into the output that constructs the bundle, instantiates the
# module, and invokes greet.
```

**Tune the output:**

```bash
wacs build app.wasm -o app.dll \
    --simd intrinsics \
    --data-storage static \
    --namespace MyApp.Wasm
# SIMD via Vector128<T> hardware intrinsics; data segments as
# static byte[] fields; root namespace MyApp.Wasm
```

### `build` flag reference

| Flag | Default | Notes |
|---|---|---|
| `-o, --output <path>` | (required) | Output `.dll` path. With multi-input, names the LAST input; siblings → `<basename>.dll`. |
| `--namespace` | `CompiledWasm` | Root namespace for generated types. |
| `-m, --module <name>` | `WasmModule` | Generated Module class name. |
| `--wasi` | off | Bake WASI Preview 1 bindings into the build runtime. |
| `--bind <asm>` | — | Custom `IBindable` host libraries (build-time). |
| `--host-package <name>` | — | Component-mode `[WitSource]` packages. |
| `--wasip2` | off | Shorthand `--host-package Wacs.WASI.Preview2`. |
| `--emit-main` | off | Bake `Program.Main(string[])` into the output. |
| `--entry-point <export>` | `_start` | Export Main invokes. |
| `--main-class <name>` | `Program` | Generated Program class name. |
| `--simd` | scalar | `interpreter \| scalar \| intrinsics`. |
| `--no-tail-calls` | off | Disable CIL `tail.` prefix. |
| `--max-fn-size <N>` | 0 | Skip transpilation of large functions. |
| `--data-storage` | compressed | `compressed \| raw \| static`. |
| `--gc-checking <flags>` | None | Extra GC type-check layers. |
| `--no-validate` | off | Skip module validation. |
| `-v, --verbose` | off | Diagnostics + per-function counts. |

## `wacs inspect` — diagnostics

```
wacs inspect <file> [options]
```

Parse-only. No instantiation, no execution, no transpilation.

### Examples

**Stats summary (default behavior with no flags):**

```bash
$ wacs inspect module.wasm
file        module.wasm
kind        core wasm module
types       3
functions   12 (4 imported)
exports     5
memories    1
tables      1
globals     0
data        2 segment(s), 1024 bytes total
elements    1 segment(s)
```

**Component stats:**

```bash
$ wacs inspect app.component.wasm
file              app.component.wasm
kind              wasm component
core modules      3 (768 bytes total)
nested components 0
types             7
canons            4
exports           1
custom sections   1
raw sections      26
```

**List exports / imports:**

```bash
wacs inspect module.wasm --exports
wacs inspect module.wasm --imports
```

**Dump WAT (round-trips back through the text parser):**

```bash
wacs inspect module.wasm --dump-wat                 # to stdout
wacs inspect module.wasm --dump-wat --output-dir .  # writes module.wat
```

### `inspect` flag reference

| Flag | Notes |
|---|---|
| `--stats` | Default when no other flag is given. |
| `--exports` | List exports (kind + name). |
| `--imports` | List imports (kind + module.name). |
| `--dump-wat` | Render parser-friendly WAT (core only — components route to their embedded core modules). |
| `--output-dir <path>` | Write `<basename>.wat` here instead of stdout. |

## Migration from `wasm-transpile`

The legacy `wasm-transpile` (`WACS.Transpiler`) CLI keeps working
unchanged — it ships a stderr deprecation banner pointing at `wacs`
but every flag still functions. Concrete migrations:

| `wasm-transpile` | `wacs` |
|---|---|
| `wasm-transpile -i x.wasm -o x.dll` | `wacs build x.wasm -o x.dll` |
| `wasm-transpile -i x.wasm -o x.dll --run` | `wacs run x.wasm` |
| `wasm-transpile -i x.wasm -o x.dll --wasi --run` | `wacs run x.wasm --wasi` |
| `wasm-transpile -i x.wasm -o x.dll --wasip2 --emit-main` | `wacs build x.wasm --wasip2 --emit-main -o x.dll` |
| `wasm-transpile -i a.wasm,b.wasm -o b.dll` | `wacs build a.wasm b.wasm -o b.dll` |
| `wasm-transpile -i x.wasm -o x.dll --engine interpreter --run` | `wacs run x.wasm --engine interpreter` |

The `-i` short flag is retired (Console used it for `--invoke`,
Transpiler for `--input` — incompatible). Inputs are positional in
`wacs`; the `--call` long flag replaces `--invoke`.

## License

WACS is distributed under the
[Apache 2.0 License](https://github.com/kelnishi/WACS/blob/main/LICENSE).
