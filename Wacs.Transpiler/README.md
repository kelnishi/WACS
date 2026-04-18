# WACS.Transpiler

An ahead-of-time transpiler that compiles WebAssembly modules to .NET
assemblies (`.dll`), built on top of [WACS](https://www.nuget.org/packages/WACS).
Ships as the `wasm-transpile` .NET CLI tool.

The generated assembly is spec-equivalent to the WACS interpreter (473/473
on the WebAssembly 3.0 spec test suite) but runs natively via the CLR's JIT
instead of the interpreter's expression-tree dispatch.

> **v0.1 preview**: the saved `.dll` currently depends on process-local
> init state and is intended for programmatic (same-process) use and
> inspection — cross-process standalone execution lands in v0.2. See
> [Known Limitations](#v01-preview-known-limitations).

## Installation

Install the CLI tool globally:

```bash
dotnet tool install -g WACS.Transpiler --prerelease
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

v0.1-preview constraints:
- Module must have no imports.
- The export named by `--entry-point` (default `_start`) must take scalar
  `i32`/`i64`/`f32`/`f64` params and return void or a single scalar.

## Library Usage

The tool is also a library — use `ModuleTranspiler` directly to drive
transpilation from your own code:

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

## v0.1-preview Known Limitations

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
