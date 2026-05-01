# WACS

The unified WebAssembly toolchain for .NET. One CLI — `wacs` — covers
running, compiling, and inspecting WebAssembly modules and components.

> **Note:** This tool supersedes
> [`wasm-transpile` (`WACS.Transpiler`)](https://www.nuget.org/packages/WACS.Transpiler).
> The legacy package is deprecated; install `WACS.Cli` instead.
> The package id is `WACS.Cli` (the bare `WACS` id is the runtime
> library, [`Wacs.Core`](https://www.nuget.org/packages/WACS)); the
> tool command users type is `wacs`.

## Installation

```bash
dotnet tool install -g WACS.Cli
```

Verify:

```bash
wacs --help
```

## Usage

`wacs` uses a verb-based subcommand layout (matches `wasmtime` /
`wasmer` industry precedent):

| Verb | Purpose |
|---|---|
| `run` | Execute a `.wasm` module (interpreter or transpiler engine) |
| `build` | Transpile to a .NET assembly (`.dll`) |
| `inspect` | Diagnostics: WAT dump, stats, exports/imports |

### Direct-run shortcut

If the first argument is a `.wasm` / `.wat` file, `wacs` defaults to
the `run` verb:

```bash
wacs my.wasm                            # → wacs run my.wasm
wacs build app.wasm -o app.dll          # explicit verb
```

### Examples

**Run with WASI preopens + environment:**

```bash
wacs run app.wasm -e PATH=/usr/bin -d ./data
```

**Run a component with WASI Preview 2 (direct-linked):**

```bash
wacs run app.component.wasm --wasip2
```

**Run a multi-module composition through the interpreter:**

```bash
wacs run a.wasm b.wasm --call quadruple 7
# → 28
```

**Build a component to a runnable .dll:**

```bash
wacs build app.component.wasm --wasip2 --emit-main \
    --entry-point greet -o app.dll
```

**Inspect a module:**

```bash
wacs inspect module.wasm --stats          # function/export counts
wacs inspect module.wasm --dump-wat       # writes module.wat
```

**Profile a hot path:**

```bash
wacs run app.wasm --profile               # JetBrains dotTrace bracket
```

## Engine choice

`--engine interpreter` (default for `run`) parses + executes via the
WACS interpreter. `--engine transpiler` JITs the module to .NET IL via
Reflection.Emit, then runs through CLR-native dispatch. The transpiler
engine is also what `build` uses to produce `.dll` output.

## License

WACS is distributed under the
[Apache 2.0 License](https://github.com/kelnishi/WACS/blob/main/LICENSE).
