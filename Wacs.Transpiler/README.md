# WACS.Transpiler

An ahead-of-time transpiler that compiles WebAssembly modules to .NET
assemblies (`.dll`), built on top of [WACS](https://www.nuget.org/packages/WACS).
Ships as the `wasm-transpile` .NET CLI tool.

The generated assembly is spec-equivalent to the WACS interpreter —
473/473 on the WebAssembly 3.0 spec test suite, verified on both macOS
ARM64 and Linux x64 — but runs natively via the CLR's JIT instead of
the interpreter's expression-tree dispatch.

> **v0.1 known limitation**: the saved `.dll` currently depends on
> process-local init state and is intended for programmatic
> (same-process) use and inspection — cross-process standalone
> execution lands in v0.2. See
> [Known Limitations](#v01-known-limitations).

## Installation

Install the CLI tool globally:

```bash
dotnet tool install -g WACS.Transpiler
```

Verify:

```bash
wasm-transpile --help
```

## CLI Usage

Transpile a `.wasm` to a .NET assembly:

```bash
wasm-transpile -i module.wasm -o module.dll
```

With verbose output, showing the flags in effect, function counts, and
diagnostics:

```bash
wasm-transpile -i module.wasm -o module.dll -v
```

### Options that map to `TranspilerOptions`

| Flag | Values | Default | Purpose |
|---|---|---|---|
| `--simd` | `interpreter` / `scalar` / `intrinsics` | `scalar` | SIMD implementation strategy. |
| `--no-tail-calls` | — | off (tail calls on) | Disable the CIL `tail.` prefix for `return_call*`. |
| `--max-fn-size N` | int | `0` (unlimited) | Skip functions larger than N instructions. |
| `--data-storage` | `compressed` / `raw` / `static` | `compressed` | How WASM data segments are stored in the assembly. |
| `--gc-checking FLAGS` | comma-separated capability names | `None` | Enable additional GC type-check layers. |

### `--emit-main`: produce a runnable host

The transpiler can bake a `Program.Main(string[] args)` into the output
assembly that constructs the module, parses argv, and invokes a named
export:

```bash
wasm-transpile -i add.wasm -o add.dll --emit-main --entry-point add
# now load + call Program.Main reflectively, or wrap in a dotnet host
```

v0.1 `--emit-main` constraints:
- Module must have no imports.
- The export named by `--entry-point` (default `_start`) must take scalar
  `i32`/`i64`/`f32`/`f64` params and return void or a single scalar.

### `--wasi`: transpile and run WASI preview1 modules

For modules that import `wasi_snapshot_preview1` (anything compiled
against a C/Rust/Go/Zig `wasi-libc` / `wasi` target), use `--wasi` to
bind `WACS.WASIp1` before transpilation and invoke an entry-point
export in-process. Trailing positional args become WASI `argv`.

```bash
wasm-transpile -i coremark.wasm \
  -o coremark.dll \
  --wasi \
  --entry-point _start \
  --run 1 1 1 1
```

What happens:
1. Before instantiation, `Wacs.WASIp1.Wasi` is constructed with a
   default configuration (stdio attached, host env inherited,
   `Directory.GetCurrentDirectory()` as the root) and bound to the
   interpreter's `WasmRuntime`.
2. The module is transpiled with its WASI imports resolved against
   those bindings.
3. A `DispatchProxy` implementing the generated `IImports` interface
   forwards every import method call through the interpreter's
   `runtime.CreateStackInvoker`, so WASI syscalls go through the
   exact same `wasi-libc`-compatible handlers used by `Wacs.Console`.
4. The transpiled module's `ctx.Memories[0]` is swapped for the
   interpreter's `MemoryInstance` so `fd_write` / `args_get` /
   `clock_time_get` see the same bytes the AOT code is reading and
   writing.
5. The named export (default `_start`) is invoked directly on the
   generated Module class.

`--wasi` can be used with or without `--emit-main`. Without it, the
module executes immediately in-process. With it, the emitted
`Program.Main` is built too — but `--run` still goes through the
WASI path.

### Other framework libraries

`--wasi` is a special case of a more general pattern: bind host
functions to the runtime before transpilation. For custom host
bindings (not WASI), use the library path below with your own
`BindHostFunction` calls and build an `ImportDispatcher` proxy the
same way `Wacs.Transpiler.Cli.WasiRunner` does. The CLI's `--wasi`
flag covers the WASI-only case; anything richer is a library
integration.

## Library Usage

The tool is also a library — use `ModuleTranspiler` directly to drive
transpilation from your own code.

### No imports

```csharp
using System.IO;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;

var runtime = new WasmRuntime();
using var fileStream = new FileStream("module.wasm", FileMode.Open);
var module = BinaryModuleParser.ParseWasm(fileStream);
var moduleInst = runtime.InstantiateModule(module);

var options = new TranspilerOptions { Simd = SimdStrategy.HardwareIntrinsics };
var transpiler = new ModuleTranspiler("MyNamespace", options);
var result = transpiler.Transpile(moduleInst, runtime, "WasmModule");

// Use in-process:
var moduleType = result.ModuleClass!;
dynamic instance = System.Activator.CreateInstance(moduleType)!;

// …or persist to disk:
result.SaveAssembly("module.dll");
```

### Custom host imports (`env.sayc`, game bindings, etc.)

Host imports are bound to the runtime *before* `InstantiateModule`
exactly like the interpreter path. The transpiler's generated Module
class takes an `IImports` proxy in its constructor; `DispatchProxy`
is the cleanest way to build one that forwards to your bound hosts:

```csharp
using System.Reflection;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;

var runtime = new WasmRuntime();

// 1. Bind your host functions (same API as Wacs.Core interpreter use).
runtime.BindHostFunction<System.Action<char>>(("env", "sayc"), ch =>
    System.Console.Write(ch));

// 2. Instantiate through the interpreter so imports resolve.
using var stream = new FileStream("hello.wasm", FileMode.Open);
var wasm = BinaryModuleParser.ParseWasm(stream);
var moduleInst = runtime.InstantiateModule(wasm);

// 3. Transpile.
var result = new ModuleTranspiler("MyApp", new TranspilerOptions())
    .Transpile(moduleInst, runtime);

// 4. Build an IImports proxy that forwards each method call to the
// corresponding runtime.CreateStackInvoker. See the Wacs.Transpiler
// source for the full ImportDispatcher / WasiRunner pattern — it's
// ~100 lines and fully reusable for non-WASI hosts.
object importsProxy = BuildImportsProxy(result, runtime, moduleInst);

// 5. Instantiate the Module class. The generated ctor takes IImports.
dynamic instance = System.Activator.CreateInstance(
    result.ModuleClass!, importsProxy)!;

// 6. Invoke an export (name = WASM export name, sanitized to a CLR
// identifier).
instance.main();
```

See [`Wacs.Transpiler/Cli/WasiRunner.cs`](Cli/WasiRunner.cs) for the
full proxy implementation, including the memory-sharing hack
(`ctx.Memories[i] = runtime.RuntimeStore[moduleInst.MemAddrs[i]]`) that
hosts which read linear memory need.

### WASI modules

Skip the custom proxy work when the imports are pure WASI preview1 —
`Wacs.Transpiler.Cli.WasiRunner` already does the binding + proxy +
memory-sharing + entry-point dispatch end-to-end:

```csharp
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.Cli;
using Wacs.WASIp1;

var runtime = new WasmRuntime();
var argv = new[] { "coremark.wasm", "1", "1", "1", "1" };
using var wasi = new Wasi(WasiRunner.BuildDefaultConfiguration(argv));
wasi.BindToRuntime(runtime);

using var fs = new FileStream("coremark.wasm", FileMode.Open);
var wasm = BinaryModuleParser.ParseWasm(fs);
var moduleInst = runtime.InstantiateModule(wasm);

var result = new ModuleTranspiler().Transpile(moduleInst, runtime);

int exit = WasiRunner.Run(result, runtime, moduleInst,
    exportName: "_start", verbose: false);
```

The `TranspilationResult` also exposes `ExportMethods`, `ImportMethods`,
`Manifest` (transpiled vs fallback function counts), and `Diagnostics`.

## Loading a Transpiled Assembly

Once a `.dll` has been written, load it like any other .NET assembly:

```csharp
using System.Reflection;

var asm = Assembly.LoadFrom("module.dll");
var moduleType = asm.GetType("MyNamespace.WasmModule.Module")
    ?? throw new InvalidOperationException("module class not found");

// Instantiate (zero-arg ctor when the wasm has no imports):
var module = Activator.CreateInstance(moduleType);

// Invoke an exported function via the generated IExports interface:
var addMethod = moduleType.GetMethod("add");
var result = addMethod!.Invoke(module, new object[] { 2, 3 });
Console.WriteLine(result);  // 5
```

## v0.1 Known Limitations

- **Cross-process execution of the saved `.dll` is not yet supported.** The
  emitted assembly depends on runtime state (a per-process
  `InitRegistry` carrying data segments, type hashes, tag tables, etc.)
  that's populated during transpilation and not yet persisted into the
  assembly. The saved `.dll` is usable in the same process that produced
  it (programmatic API path) but loading it in a fresh process throws
  `ArgumentOutOfRangeException` from `InitRegistry.Get`. Full standalone
  AOT (init-data embedded as assembly resources) is the v0.2 milestone.
- **`--emit-main` shares the same limitation**: the generated
  `Program.Main` is reachable reflectively but can't be run with
  `dotnet path/to.dll` in a fresh process until init-data embedding
  lands.
- **`--emit-main` rejects modules with imports.** v0.2 will add a
  `--wasi-host` flag backed by `WACS.WASIp1` and an
  `--allow-missing-imports` escape hatch that emits throwing stubs.
- **Scalar args only** for `--emit-main` — `i32`/`i64`/`f32`/`f64`.
  Ref-typed and `v128` params aren't parsed from argv yet.

## License

WACS.Transpiler is distributed under the
[Apache 2.0 License](https://github.com/kelnishi/WACS/blob/main/LICENSE).
