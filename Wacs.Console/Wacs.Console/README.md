# WACS

The unified WebAssembly toolchain for .NET. One CLI — `wacs` — covers
running, compiling, inspecting, and generating bindings for
WebAssembly modules and components, backed by the
[WACS](https://www.nuget.org/packages/WACS) interpreter, the
[WACS.Transpiler.Lib](https://www.nuget.org/packages/WACS.Transpiler.Lib)
AOT engine, and the
[WACS.ComponentModel.Bindgen.Lib](https://www.nuget.org/packages/WACS.ComponentModel.Bindgen.Lib)
binding generator.

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
| `aot` | Build a self-contained NativeAOT native binary (transpile + scaffold + `dotnet publish`) | transpiler + ILC |
| `inspect` | Diagnostics: WAT dump, stats, exports/imports | parse-only |
| `bindgen` | Generate C# bindings from WIT (forward) or regenerate WIT + bindings from a transpiled `.dll` (reverse) | parse-only |

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

**Mount a host directory into a wasip2 component:**

```bash
wacs run app.component.wasm --wasip2 -d ./models::/models
# `host::guest` mount-pair syntax — the host side is `./models`,
# the guest sees `/models`. Bare `-d models` mounts at `/models`
# (Preview1's rooting convention so the same flag form works on
# both engines). Both sides reach the guest through the
# transpiler's direct-link emit for
# wasi:filesystem/preopens.get-directories — no shim needed.
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
# with the bundle path).
#
# Command components don't need --call — `wacs run --wasip2` looks
# for the canonical wasi:cli/run@<version>#run export and dispatches
# it automatically. Falls back to _start, then to a helpful error
# listing the available exports. Pass --call <export> (or
# --invoke <export>) to override.
```

**Run a component that imports `wasi:nn` (ONNX inference):**

```bash
wacs run my.component.wasm --wasip2 --wasi-nn -d ./models
# --wasi-nn loads Wacs.WASI.NN.OnnxRuntime (bundled with the CLI)
# and wires its IBindable adapter into the runtime. For other
# wasi-nn backends (ML.NET, LlamaSharp), pass the package name
# through --bind directly.
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
| `--call <export>` | auto | Function to invoke. Args after `--` are parsed per its wasm signature. Auto: components dispatch `wasi:cli/run@<v>#run` if present; cores dispatch `_start`. `--invoke` is an alias. |
| `--engine` | `interpreter` | `interpreter` or `transpiler` (Reflection.Emit AOT, mixed-mode imports). |
| `-m, --module <name>` | `_` | Name to register the instantiated module under. |
| `-e, --env K=V` | — | WASI Preview 1 environ. Repeat or comma-separate. |
| `-d, --dir <path>` | — | Preopen directory. Bare path mounts at `/<basename>`; `host::guest` syntax (matches wasmtime) sets the guest mount explicitly. Honored on both `--wasi` (Preview 1) and `--wasip2` (Preview 2). Repeat or comma-separate. |
| `--wasi` | off | Bind WASI Preview 1 host imports. |
| `--bind <asm>` | — | Load `IBindable` host packages. Accepts a file path (`Assembly.LoadFrom`) or assembly name (`Assembly.Load`). Repeat or comma-separate. |
| `--host-package <name>` | — | Component-mode `[WitSource]` host package(s). Accepts assembly name or file path. |
| `--wasip2` | off | Shorthand: `--host-package Wacs.WASI.Preview2 + Wacs.WASI.Preview2.DependencyInjection`. The DI sibling carries the SourceGen-shape impl classes the transpiler instantiates for `[constructor]X`; both halves are needed for direct-link emit. See [`docs/COMPONENT_CHAINING.md`](../../docs/COMPONENT_CHAINING.md). |
| `--wasi-nn` | off | Shorthand: adds `Wacs.WASI.NN + Wacs.WASI.NN.DependencyInjection + Wacs.WASI.NN.OnnxRuntime`. ONNX is the default (bundled) backend. For non-ONNX backends — ML.NET, LlamaSharp (GGUF), TorchSharp (PyTorch / TorchScript) — use `--bind <path-to-backend.dll>` directly; `--bind` auto-pulls `Wacs.WASI.NN + .DependencyInjection` when the bound assembly's identity starts with `Wacs.WASI.NN.` (gap 24a). The composite `WasiPreview2NNBundle` is auto-discovered when both `--wasip2` and a wasi-nn backend are wired. |
| `--wasi-threads` | off | Shorthand `--bind Wacs.WASI.Threads`. Wires `wasi:thread-spawn`; module must declare/import shared memory and export `wasi_thread_start (param i32 i32)`. |
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
| `--wasip2` | off | Shorthand: adds `Wacs.WASI.Preview2 + Wacs.WASI.Preview2.DependencyInjection`. See [`docs/COMPONENT_CHAINING.md`](../../docs/COMPONENT_CHAINING.md) for which packages each capability needs on the load path. |
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

## `wacs aot` — wasm → NativeAOT native binary

```
wacs aot <input.wasm> [-o <output>] [options] [-- argv...]
```

End-to-end: transpile the input wasm to a stable-named .dll,
scaffold a throwaway consumer csproj that statically references it
plus the WACS runtime support assemblies, and run `dotnet publish
-p:PublishAot=true -r <rid>`. The resulting native binary is copied
to the output path; the temp build dir is removed unless
`--keep-temp`.

Cold-start equivalent of `transpiler-aot-linked` from
[`docs/COLDSTART.md`](../docs/COLDSTART.md): `new Module()` → typed
direct call into the wasm function. No JIT, no Reflection.Emit, no
Assembly.Load, no MethodInfo.Invoke at run time.

### Examples

**Compute-only module to native binary:**

```bash
wacs aot fib.wasm -o fib
./fib                 # native exe — no .NET runtime required
```

**WASI Preview 1 (the wasi-libc / wasi-sdk world):**

```bash
wacs aot coremark.wasm --wasi -o coremark
./coremark 1 1 1 1    # trailing argv forwarded to the guest
```

`--wasi` references `WACS.WASI.Preview1` from the scaffolded
consumer; the source generator in `WACS.HostBindings.SourceGen` emits
an `IImports` adapter that wires the wasm's
`wasi_snapshot_preview1.*` imports straight to the
`[WacsImport]`-annotated statics. No reflection, no `DispatchProxy`,
fully NativeAOT-trim-safe.

**Component with WASI Preview 2:**

```bash
wacs aot app.component.wasm --wasip2 --entry-point greet -o app
./app
```

The component's `wasi:*` imports are direct-linked at transpile time
against the typed C# host interfaces in `WACS.WASI.Preview2`; the
consumer constructs the `WasiPreview2Bundle` via
`Microsoft.Extensions.DependencyInjection` before invoking the
named export.

**Pick the AotLinked emission target (smaller binary):**

```bash
wacs aot fib.wasm --aot-linked -o fib
# Skips the codec wrapper. Default emission keeps it; AotLinked
# inlines memory/data/globals/tables straight into the Module ctor
# and lets the trimmer dead-strip the codec machinery.
```

### `aot` flag reference

| Flag | Default | Notes |
|---|---|---|
| `-o, --output <path>` | `<inputBasename>` in cwd | Path for the produced native binary (no `.exe` suffix by default — set explicitly on Windows). |
| `--rid <rid>` | host RID | Target .NET runtime identifier (`osx-arm64`, `linux-x64`, `win-x64`). |
| `--entry-point <export>` | `_start` | WASM export the emitted `Program.Main` invokes (scalar args only). |
| `--namespace <name>` | `WacsAot` | Root namespace for generated types in the transpiled .dll. |
| `--simd` | `scalar` | `interpreter \| scalar \| intrinsics`. |
| `--aot-linked` | off | Use the `EmissionTarget.AotLinked` emission target — skips the codec wrapper for a smaller binary. Covers memory / data / globals / tables / element segments. |
| `--wasi` | off | Bake `WACS.WASI.Preview1` bindings into the produced binary. |
| `--wasip2` | off | Component-mode counterpart to `--wasi` — direct-links `wasi:*` imports against `WACS.WASI.Preview2 + .DependencyInjection`. See [`docs/COMPONENT_CHAINING.md`](../../docs/COMPONENT_CHAINING.md). |
| `--preopen <H::G>` | — | WASI directory preopen `<host-path>::<guest-path>`. Repeat for multiple. Only with `--wasi`. |
| `--keep-temp` | off | Don't delete the scaffolded build dir (useful for inspecting the generated csproj / Program.cs). |
| `-v, --verbose` | off | Print each step (transpile, scaffold, publish, copy). |

Trailing positional args after the input file are forwarded to the
guest as `argv` when `--wasi` is set.

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

For components, `--imports` enumerates each top-level instance import
with its package + version — useful for predicting which host packages
to wire before running:

```bash
$ wacs inspect my.component.wasm --imports
=== component-level imports ===
  Instance  wasi:nn/inference@0.2.0-rc-2024-10-28
  Instance  wasi:nn/graph@0.2.0-rc-2024-10-28
  Instance  wasi:cli/stdout@0.2.9
  Instance  wasi:cli/exit@0.2.9
  …
```

A component with `wasi:nn/*` imports needs `--wasi-nn` (or
`--bind Wacs.WASI.NN.<backend>` for non-ONNX backends). `wasi:cli/*`
needs `--wasip2`. Version mismatches (e.g. component compiled against
`wasi:cli/stdout@0.2.9` but `Wacs.WASI.Preview2` ships `0.2.0`) surface
as direct-link resolution failures at instantiation.

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

## `wacs bindgen` — WIT ↔ C# bindings

```
wacs bindgen <input> -o <output-dir> [options]
```

Two directions on one verb, auto-detected by input shape:

- **Forward** — a `.wit` file or a directory tree of WIT files
  (recurses into `deps/`) → `[WitSource]`-tagged C# interfaces.
  Use this when authoring components: pre-generate the host
  binding surface for offline AOT targets that can't use the
  runtime transpiler.
- **Reverse** — a transpiled `.dll` carrying embedded
  component-type metadata → regenerated WIT + C# bindings. Use
  this when the original `.wit` is gone but the shipped `.dll` is
  still on hand.

### Examples

**Forward, single WIT file:**

```bash
wacs bindgen ./wit/hello.wit -o ./Generated/
```

**Forward, WIT directory tree (with `deps/`):**

```bash
wacs bindgen ./wit -o ./Generated/
# Recurses into deps/ subdirectories. Headerless files attribute
# to the parent package per the wit-bindgen-csharp convention.
```

**Reverse, regenerate from a transpiled `.dll`:**

```bash
wacs bindgen ./app.dll -o ./regenerated/
# Extracts the embedded component-type metadata, decodes it to
# WIT, and regenerates the [WitSource]-tagged C# binding surface.
# Errors with exit code 3 if the .dll has no embedded WIT.
```

**Reverse with raw bytes preserved:**

```bash
wacs bindgen ./app.dll -o ./regen --write-wit
# Also writes <basename>.componenttype.bin alongside the .cs files
# (useful for `wasm-tools component wit` round-trip inspection).
```

### `bindgen` flag reference

| Flag | Notes |
|---|---|
| `<input>` (positional) | `.wit` / WIT directory / `.dll`. Direction inferred from extension. |
| `-o, --output <dir>` (required) | Output directory. One `.cs` file per emitted interface / world / package. |
| `--namespace <name>` | Root C# namespace override. Currently a warning — Phase 1d uses pinned `wit-bindgen-csharp` conventions; explicit override is a follow-up. |
| `--write-wit` | Reverse mode only. Persist the raw extracted component-type bytes alongside the `.cs` files. |
| `-v, --verbose` | Per-file emission progress. |

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

> The standalone `WACS.ComponentModel.Bindgen` package
> (`wit-bindgen-wacs` CLI) was rolled into the `bindgen` verb
> here before its first NuGet release; users only ever see
> `wacs bindgen`. The `WACS.ComponentModel.Bindgen.Lib` package
> (programmatic surface) is unaffected — source generators and
> build-time integrations should keep referencing it directly.

The `-i` short flag is retired (Console used it for `--invoke`,
Transpiler for `--input` — incompatible). Inputs are positional in
`wacs`; the `--call` long flag replaces `--invoke`.

## License

WACS is distributed under the
[Apache 2.0 License](https://github.com/kelnishi/WACS/blob/main/LICENSE).
