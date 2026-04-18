# Doc 6 — `wasm-transpile` CLI Walkthrough

**Audience:** end users of the `WACS.Transpiler` NuGet package.
Internal design docs live in `01`–`05`; this is the first user-facing
doc in the series.

This is a step-by-step walkthrough of installing the `wasm-transpile`
CLI, transpiling a `.wasm` module into a .NET assembly, invoking it
programmatically, and (within v0.1's limits) using
`--emit-main` to bundle a host entry point into the output.

---

## 1. Install

```bash
dotnet tool install -g WACS.Transpiler
```

Confirm the command is on PATH:

```bash
wasm-transpile --help
```

If `dotnet tool install -g` reports a success but the shell can't find
`wasm-transpile`, add `~/.dotnet/tools` to your PATH.

## 2. Transpile a `.wasm`

A minimal invocation:

```bash
wasm-transpile -i add.wasm -o add.dll
```

With diagnostics:

```bash
wasm-transpile -i add.wasm -o add.dll -v
# input         /.../add.wasm
# output        /.../add.dll
# namespace     CompiledWasm
# module        WasmModule
# simd          ScalarReference
# tail-calls    True
# max-fn-size   0
# data-storage  CompressedResource
# gc-checking   None
#
# transpiled 30 functions in 141ms
# 1 diagnostic(s):
#   [Info] Analyzed 1 memories, 1 data segments (26 bytes total)
```

### Tuning transpilation

The CLI's options mirror `TranspilerOptions` (see `01-wasm-to-cil-mapping.md`
for what each affects):

```bash
# Use CLR SIMD intrinsics instead of the scalar reference path
wasm-transpile -i mod.wasm -o mod.dll --simd intrinsics

# Emit data segments as plain static arrays rather than Brotli resources
wasm-transpile -i mod.wasm -o mod.dll --data-storage static

# Disable the CIL tail. prefix (diagnostic / workaround)
wasm-transpile -i mod.wasm -o mod.dll --no-tail-calls

# Skip functions with > 5000 WASM instructions (rare, for very large modules)
wasm-transpile -i mod.wasm -o mod.dll --max-fn-size 5000
```

## 3. Use the transpiled assembly programmatically

In this release, the transpiled `.dll` is intended for in-process use
alongside the WACS runtime. The pattern is: your app constructs a
`WasmRuntime`, calls `ModuleTranspiler.Transpile`, then invokes the
result directly — without going through disk.

```csharp
using System.IO;
using Wacs.Core;
using Wacs.Core.Runtime;
using Wacs.Transpiler.AOT;

var runtime = new WasmRuntime();
using var stream = new FileStream("add.wasm", FileMode.Open);
var wasm = BinaryModuleParser.ParseWasm(stream);
var moduleInst = runtime.InstantiateModule(wasm);

var result = new ModuleTranspiler("MyApp", new TranspilerOptions())
    .Transpile(moduleInst, runtime, "WasmModule");

dynamic module = Activator.CreateInstance(result.ModuleClass!)!;
int sum = module.add(2, 3);  // 5
```

The inspection-oriented "save to disk" flow also works in the same
process (useful for looking at the emitted IL in ilspy/dnSpy):

```csharp
result.SaveAssembly("/tmp/add.dll");
// ...use the in-memory `result` as above; the on-disk copy is a
// snapshot of the dynamic assembly for tooling, not a standalone target.
```

## 4. `--emit-main`: bundle a host `Main`

If the module has no imports, `wasm-transpile` can generate a
`Program.Main(string[] args)` inside the output assembly that
constructs the module, parses argv, and invokes a named export:

```bash
wasm-transpile -i add.wasm -o add.dll --emit-main --entry-point add
```

The emitted `Main`:

1. Reads argv — one entry per export param.
2. Parses each argv entry via `int.Parse` / `long.Parse` / `float.Parse`
   / `double.Parse` using `CultureInfo.InvariantCulture`.
3. Constructs the module (`new WasmModule()`).
4. Calls the export through the generated `IExports` interface.
5. Prints any scalar result via `Console.WriteLine` and returns 0.

You can invoke Main reflectively in the same process that produced the
.dll:

```csharp
var asm = System.Reflection.Assembly.LoadFrom("/tmp/add.dll");
var main = asm.GetType("Program")!.GetMethod("Main")!;
main.Invoke(null, new object?[] { new[] { "2", "3" } }); // prints 5
```

## 5. Known limitations (v0.1)

See the package README's *Known Limitations* section for the full list.
The two that matter most for this walkthrough:

- The saved `.dll` is **not yet standalone across processes**. Running
  `dotnet add.dll 2 3` in a fresh process raises
  `ArgumentOutOfRangeException` from `InitRegistry.Get` because the
  per-process init data (segment bytes, type hashes, tag tables) isn't
  embedded into the assembly yet. v0.2 will serialize init data into an
  assembly resource so the .dll becomes self-contained.
- `--emit-main` currently supports **zero-import modules** with
  **scalar i32/i64/f32/f64** params and a void or scalar result. WASI
  modules, ref-typed params, and v128 params are follow-ups.

If either of these blocks your use case, please open an issue at
<https://github.com/kelnishi/WACS>.
