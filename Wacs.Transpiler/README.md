# WACS.Transpiler

An ahead-of-time transpiler that compiles WebAssembly modules to .NET
assemblies (`.dll`), built on top of [WACS](https://www.nuget.org/packages/WACS).
Ships as the `wasm-transpile` .NET CLI tool.

The generated assembly is spec-equivalent to the WACS interpreter —
473/473 on the WebAssembly 3.0 spec test suite, verified on both macOS
ARM64 and Linux x64 — but runs natively via the CLR's JIT instead of
the interpreter's expression-tree dispatch.

> **v0.2**: saved `.dll`s now work cross-process — every transpiled
> assembly embeds a codec-encoded init-data resource that the generated
> Module ctor decodes on first use. See
> [Loading a Transpiled Assembly](#loading-a-transpiled-assembly) for
> the seamless `TranspiledModuleLoader` API.
>
> **For library use, reference `WACS.Transpiler.Lib`** (new in 0.2).
> The `WACS.Transpiler` package stays as the `wasm-transpile`
> dotnet-tool CLI.

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

### Multiple inputs: cross-module composition

Pass `-i` multiple times (or comma-separate) to transpile/interpret
N modules in one shot. Each input's exports become discoverable
under the file basename so cross-module imports resolve through
the shared `WasmRuntime`'s binding table — the same pattern the
spec test runner uses for multi-module fixtures.

```bash
# Two modules: b.wasm imports "a.double" from a.wasm.
# Transpile both, save sibling .dlls in the output directory.
# The last input drives the -o filename; siblings land at <basename>.dll.
wasm-transpile -i a.wasm,b.wasm -o b.dll
# wrote a.dll, b.dll

# Or run through the interpreter (no .dll emitted), invoking the
# last module's named export with positional args.
wasm-transpile -i a.wasm,b.wasm -o b.dll \
  --engine interpreter --run --entry-point quadruple 7
# 28
```

The transpiler emits one `.dll` per input. To compose them at load
time, register each module under its basename so cross-module
import lookups succeed — see the
[Library Usage](#library-usage) section for the
`ImportDispatcher.Create` pattern that wires `b.IImports` to A's
exported methods.

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

Core-WASM `--emit-main` constraints:
- Module must have no imports. (Component binaries with imports
  use the `--wasip2` / `--host-package` route — see Component
  Model mode below.)
- The export named by `--entry-point` (default `_start`) must take scalar
  `i32`/`i64`/`f32`/`f64` params and return void or a single scalar.

Component-mode `--emit-main` is wider — argv parsing covers
primitives, `bool`, `string`, and `byte[]`; multi-core
wit-component output is handled by routing through the primary
user core. See [Component Model mode](#component-model-mode).

### `--bind`: link against any WACS host library

The transpiler can link against any assembly that exposes host
bindings through the `Wacs.Core.Runtime.IBindable` interface:

```csharp
// In your library:
public class MyGameHost : IBindable {
    public void BindToRuntime(WasmRuntime runtime) {
        runtime.BindHostFunction<Action<int>>(("env", "play_sound"), id => /*…*/);
        runtime.BindHostFunction<Func<int,int>>(("env", "random_up_to"), n => /*…*/);
    }
}
```

Build the library (a standard .NET assembly), then pass it to
`wasm-transpile` with `--bind`:

```bash
wasm-transpile -i game.wasm \
  -o game.dll \
  --bind ./MyGameHost.dll \
  --entry-point _start \
  --run
```

The transpiler loads the assembly, reflects every concrete `IBindable`
with a parameterless constructor, activates each one, and wires them
into the runtime before instantiating the module. Repeat or
comma-separate `--bind` for multiple assemblies — WASI, a scripting
shim, and a telemetry sink can all be linked in at once.

Constraints for auto-discovery:
- Each binding class needs a **parameterless constructor**. Bindings
  that require configuration should either provide sensible defaults
  in the parameterless ctor (`Wacs.WASIp1.Wasi()` does this) or be
  driven from the library API below.
- `IBindable`s that also implement `IDisposable` are disposed after
  the run. Good for file handles, network sockets, etc.
- The transpiler itself doesn't care which CLR-side types the host
  uses — match what `runtime.BindHostFunction<TDelegate>` accepts.

### `--wasi`: shortcut for WASI preview1

For modules that import `wasi_snapshot_preview1` (anything compiled
against a C/Rust/Go/Zig `wasi-libc` / `wasi` target), `--wasi` is a
shortcut that's equivalent to `--bind <path-to-Wacs.WASIp1.dll>` with
the CLI's trailing positional args exposed as WASI `argv`.

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
   `Directory.GetCurrentDirectory()` as the root, argv =
   `[wasm-filename, …trailing-args]`) and bound to the runtime.
2. The module is transpiled with its WASI imports resolved.
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

`--wasi` and `--bind` can be combined (e.g. WASI + a custom
telemetry host), and both work with or without `--emit-main`. When
`--emit-main` is also set, the emitted `Program.Main` is built but
`--run` still goes through the hosted path.

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

### Seamless: `TranspiledModuleLoader` (recommended)

New in v0.2 — reference **WACS.Transpiler.Lib** and use the built-in
loader. It discovers the generated types, wires imports, and returns a
handle you can invoke directly:

```csharp
using Wacs.Transpiler.Hosting;

// No-import module:
var loaded = TranspiledModuleLoader.Load("add.dll");
int sum = (int)loaded.Invoke("add", 2, 3)!;  // 5

// Or a typed delegate for hot paths:
var add = loaded.GetExport<Func<int, int, int>>("add");
int sum2 = add(2, 3);
```

Modules with imports: either pass an object implementing the generated
`IImports` interface (AOT-safe, full `AssemblyLoadContext` isolation),
or a by-name delegate dictionary (loader auto-downgrades isolation —
`DispatchProxy` can't reference collectible assemblies):

```csharp
var loaded = TranspiledModuleLoader.Load("game.dll", imports: new Dictionary<string, Delegate>
{
    ["env_play_sound"] = (Action<int>)(id => AudioEngine.Play(id)),
    ["env_random_up_to"] = (Func<int, int>)(n => Random.Shared.Next(n)),
});
loaded.Invoke("tick");
```

The `LoadedModule` handle also exposes `ExportsInterface` / `ImportsInterface`
as `Type` so tools can enumerate methods, inspect signatures, and
generate wrappers reflectively.

### Manual

Raw `Assembly.LoadFrom` still works if you need full control:

```csharp
using System.Reflection;

var asm = Assembly.LoadFrom("module.dll");
var moduleType = asm.GetType("MyNamespace.WasmModule.Module")!;
var module = Activator.CreateInstance(moduleType);
var addMethod = moduleType.GetMethod("add");
var result = addMethod!.Invoke(module, new object[] { 2, 3 });
```

## Component Model mode

For WebAssembly **components** (the `.component.wasm` shape that wraps
core modules with a WIT-described interface), the transpiler emits a
fully-linked `.dll` whose guest→host imports lower to inline IL —
direct `callvirt` into typed `I*` interfaces, no delegate boxing, no
runtime dictionary hop. Missing or mismatched imports fail at
**transpile time** with the same `Detail` text the runtime validator
would produce.

A transpiled component `.dll` carries:
- `ComponentExports` — a static class (or set of classes per
  interface) with one method per WIT export, signatures already
  shaped to the component's WIT.
- `[WitSource]`-tagged `I{Iface}` interfaces — one per export
  interface plus a synthesized `I{World}World` for freestanding
  world exports. These are the same shape `wit-bindgen-csharp`
  emits, so a transpiled `.dll` is interchangeable with a
  source-generated one as a host package.
- `ComponentMetadata.EmbeddedWitBytes` — the original
  `component-type:*` blob, so the WIT round-trips out of the
  assembly without the source `.component.wasm`.

### CLI

Resolve a component's imports against the bundled WASI Preview 2
host package:

```bash
wasm-transpile -i my-component.wasm -o my-component.dll --wasip2
```

Or against any host-package assembly that exposes
`[WitSource]`-tagged interfaces (matches what
`Wacs.ComponentModel.Bindgen.SourceGen` and
`ExportInterfaceEmit` both produce):

```bash
wasm-transpile -i app.wasm -o app.dll \
  --host-package Wacs.WASI.Preview2 \
  --host-package ./MyHost.dll
```

`--host-package` is repeatable (or comma-separated). Each value is
either an assembly **name** (`Assembly.Load`) or a **file path**
(`Assembly.LoadFrom`). `--wasip2` is a shorthand for
`--host-package Wacs.WASI.Preview2`.

Verify direct linking actually replaced the delegate hop:

```bash
ildasm app.dll | grep -i BindHostFunction
# (no output — every import dispatches inline)
```

#### `--emit-main` for components

Same flag as the core-WASM path; the CLI auto-detects component
binaries via the layer byte and routes accordingly. Emits a
`Program.Main(string[] args)` that constructs the host bundle
(when `--wasip2` is set), instantiates the module, parses argv
into the export's CLR param types, and invokes the chosen export:

```bash
# add: func(x: u32, y: u32) -> u32 — argv → typed params, return → exit code.
wasm-transpile -i ad.component.wasm -o ad.dll \
  --emit-main --entry-point add --run 7 35
# exit code: 42

# Multi-core WASI components (typical wit-component output) work too.
# The transpiler picks the primary user core via the first canon-lift,
# matching how ComponentInstance.InstantiateMultiCore selects it.
wasm-transpile -i hello.component.wasm -o hello.dll \
  --wasip2 --emit-main --entry-point greet --run
```

v0 argv parsing covers primitives (`i32`/`u32`/`i64`/`u64`/`f32`/
`f64` plus narrow ints), `bool`, `string` (verbatim), and `byte[]`
(UTF-8). Aggregate component-shape params (`Option<T>`, `list<T>`,
records, variants) ride later — the run surfaces a clean
`Could not parse argv[N]` error pointing at the offending param.
Scalar return types route through the int exit code; `string` /
`byte[]` returns print to stdout and return 0.

### Programmatic transpile

```csharp
using System.IO;
using System.Reflection;
using Wacs.Transpiler.AOT;
using Wacs.Transpiler.AOT.Component;

var options = new TranspilerOptions
{
    HostPackages = new[]
    {
        Assembly.Load("Wacs.WASI.Preview2"),
        // …additional host packages
    },
};

using var fs = new FileStream("my-component.wasm", FileMode.Open);
var result = ComponentTranspiler.TranspileSingleModule(
    fs,
    assemblyNamespace: "MyApp.Component",
    moduleName: "MyComponent",
    options: options);

result.SaveAssembly("my-component.dll");
```

`TranspileSingleModule` builds a `HostPackageResolver` from
`options.HostPackages` automatically (or accepts a pre-built one
via `options.Resolver`). For every guest import it can resolve, the
call site emits inline IL into the typed interface; everything else
falls back to the legacy delegate-table dispatch — so partial host
packages work without ceremony.

When the runtime needs stub bindings to satisfy unresolved imports
(for instance, a no-op `IImports` shim during transpile so
`InstantiateModule` doesn't throw), pass a `configureImports`
callback:

```csharp
ComponentTranspiler.TranspileSingleModule(
    fs, options: options,
    configureImports: runtime =>
    {
        // register throwing stubs for any unresolved imports
    });
```

### Running a transpiled component

The generated module class's constructor takes — in order — any
imports the underlying core module declared (an `IImports`
interface) followed by the host bundle (`object`) and, if any
resource methods are direct-linked, a resources object. The
`(IImports)`-only shape stays binary-compatible for non-direct-
linked assemblies.

For a `--wasip2`-transpiled assembly the bundle is
`Wacs.WASI.Preview2.DependencyInjection.WasiPreview2Bundle`. The
unresolved-import surface is satisfied by a throwing stub — every
WASI import lowered to inline IL bypasses it:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Wacs.Transpiler.Cli;             // ImportDispatcher
using Wacs.WASI.Preview2.DependencyInjection;

var bundle = new ServiceCollection()
    .AddWasiPreview2()           // registers WasiPreview2Bundle as singleton
    .BuildServiceProvider()
    .GetRequiredService<WasiPreview2Bundle>();

var asm = Assembly.LoadFrom("my-component.dll");
var moduleType = asm.GetType("MyApp.Component.MyComponent.Module")!;

// First ctor param is the generated IImports interface (a stub
// when every import is direct-linked — the inline IL bypasses it,
// but the core runtime's instantiation validation still wants
// the slot populated). Build a no-op via the bundled
// ImportDispatcher with an empty handler set.
var ctorParams = moduleType.GetConstructors()[0].GetParameters();
var importsInterface = ctorParams[0].ParameterType;
var importsStub = ImportDispatcher.Create(
    importsInterface,
    new Dictionary<string, System.Func<object?[], object?>>());

var module = Activator.CreateInstance(
    moduleType, importsStub, bundle)!;

// Exports surface, two paths:
//   1. The IExports interface — raw core-WASM signatures
//      (i32/i64/etc.). Implemented directly by the Module class.
//   2. The ComponentExports static class — wit-shaped signatures
//      (string, T[], Option<T>, etc.). Only emitted when the
//      core module's ctor takes no args (no imports). For
//      direct-linked WASI components use path 1.

var iExports = moduleType.GetInterfaces()
    .First(i => i.Name.StartsWith("IExports"));
var run = iExports.GetMethod("run")!;
run.Invoke(module, System.Array.Empty<object>());
```

For components compiled without imports (uncommon for real-world
WASI guests, common for self-contained fixtures and tests),
`ComponentExports` is emitted with public static methods you can
call directly:

```csharp
var exports = asm.GetType("MyApp.Component.ComponentExports")!;
var greet = exports.GetMethod("Greet")!;       // greet: func(name: string) -> string
var hello = (string)greet.Invoke(null, new object[] { "world" })!;
```

`WasiPreview2Bundle` packs all 22 WASI Preview 2 typed interfaces
(`IRandom`, `IStdout`, `IMonotonicClock`, `IOutgoingHandler`, etc.)
into a single object. `AddWasiPreview2()` registers it as a
singleton along with each interface's default implementation; see
`Wacs.WASI.Preview2.DependencyInjection.WasiPreview2ServiceCollectionExtensions`
for the canonical wiring. Override individual services
(`services.Replace(ServiceDescriptor.Singleton<IStdout, SilentStdout>())`)
to swap behavior — e.g. a silent stdout for tests, or an in-memory
filesystem for sandboxed runs — then resolve `WasiPreview2Bundle`
and the override flows through.

### Interrogating a transpiled assembly

A transpiled component `.dll` is fully self-describing — load it
and walk the surface with reflection, the same way you'd walk a
source-generated host package.

**Recover the WIT contract:**

```csharp
using System.Reflection;
using Wacs.ComponentModel.Validation;

var asm = Assembly.LoadFrom("my-component.dll");
var contract = WitContract.FromAssembly(asm);

foreach (var import in contract.Imports)
    Console.WriteLine($"{import.Module}/{import.Entity} " +
        $"(params={import.ExpectedParamCount}, " +
        $"results={import.ExpectedReturnCount})");
```

`FromAssembly` tries the embedded-WIT path first
(`source-gen`/`componentize-dotnet` style with `wit/*.wit` resources)
and falls back to walking `[WitSource]`-tagged interfaces — so a
WACS-transpiled `.dll` and a `wit-bindgen-csharp` host package both
work identically.

**Enumerate the [WitSource]-tagged interfaces:**

```csharp
using Wacs.ComponentModel.Runtime;

foreach (var t in asm.GetExportedTypes())
{
    if (!t.IsInterface) continue;
    var ws = t.GetCustomAttribute<WitSourceAttribute>();
    if (ws == null) continue;

    Console.WriteLine($"{t.FullName}");
    Console.WriteLine($"  Package:   {ws.Package}");
    Console.WriteLine($"  Interface: {ws.Interface}");

    foreach (var m in t.GetMethods())
    {
        var mws = m.GetCustomAttribute<WitSourceAttribute>();
        if (mws == null) continue;
        var ps = string.Join(", ", m.GetParameters()
            .Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  - {m.Name}({ps}) -> {m.ReturnType.Name}");
        Console.WriteLine($"      Item: {mws.Item}");
    }
}
```

**Recover the original WIT text:**

```csharp
var packages = WitContract.LoadAssemblyPackages(asm);
// each CtPackage carries Interfaces / Worlds / etc.

// Or, if the assembly preserved the component-type blob:
var metadataType = asm.GetType("MyApp.Component.ComponentMetadata");
var witBytes = (byte[])metadataType!
    .GetField("EmbeddedWitBytes",
        BindingFlags.Public | BindingFlags.Static)!
    .GetValue(null)!;
```

**Enumerate exports:**

The `ComponentExports` class holds one static method per WIT export
function, shaped to the canonical-ABI lift/lower of the signature
(strings as `string`, lists as `T[]`, options as `Option<T>`,
records as POCOs, etc.):

```csharp
var exports = asm.GetType("MyApp.Component.ComponentExports")!;
foreach (var m in exports.GetMethods(BindingFlags.Public | BindingFlags.Static))
    Console.WriteLine($"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
```

### Chain mode: transpiled `.dll` as host package

Because every transpiled component carries `[WitSource]`-tagged
exports, a transpiled `.dll` itself satisfies the host-package
contract. The chain `B.wasm → B.dll → A.wasm uses --host-package
B.dll` works end-to-end:

```bash
# 1. Transpile B (the producer) into a .dll, resolving its own
#    imports against WASI:
wasm-transpile -i B.wasm -o B.dll --wasip2

# 2. Transpile A (the consumer), resolving its imports against B's
#    transpiled exports — plus any system imports against WASI:
wasm-transpile -i A.wasm -o A.dll \
  --wasip2 \
  --host-package ./B.dll
```

Programmatically:

```csharp
// Build B first.
using (var bs = File.OpenRead("B.wasm"))
    ComponentTranspiler.TranspileSingleModule(bs,
        assemblyNamespace: "Composed.B",
        moduleName: "B",
        options: new TranspilerOptions
        {
            HostPackages = new[] { Assembly.Load("Wacs.WASI.Preview2") },
        }).SaveAssembly("B.dll");

// Then build A using B as a host package.
var bAsm = Assembly.LoadFrom("./B.dll");
using (var fs = File.OpenRead("A.wasm"))
    ComponentTranspiler.TranspileSingleModule(fs,
        assemblyNamespace: "Composed.A",
        moduleName: "A",
        options: new TranspilerOptions
        {
            HostPackages = new[]
            {
                Assembly.Load("Wacs.WASI.Preview2"),
                bAsm,
            },
        }).SaveAssembly("A.dll");
```

At runtime, instantiate B and pass it as the host bundle for A:

```csharp
var aAsm = Assembly.LoadFrom("A.dll");
var bAsm = Assembly.LoadFrom("B.dll");

// B needs the WASI bundle.
var wasi = new ServiceCollection().AddWasiPreview2()
    .BuildServiceProvider().GetRequiredService<WasiPreview2Bundle>();
var bModuleType = bAsm.GetType("Composed.B.B.Module")!;
var bModule = Activator.CreateInstance(bModuleType, wasi)!;

// A needs a bundle whose properties match B's exported [WitSource]
// interfaces. Build a small adapter exposing each as a getter that
// returns the relevant ComponentExports facade on bModule. (Or
// generate this glue from the [WitSource] surface.)
var aBundle = new BAdapterBundle(bModule);

var aModuleType = aAsm.GetType("Composed.A.A.Module")!;
var aModule = Activator.CreateInstance(aModuleType, aBundle)!;
```

The [WitSource] attributes on B's interfaces are the contract the
adapter satisfies — `HostPackageResolver` indexes them by
`(Package, Interface, Item)`, which is exactly what the wasm
component's import strings encode.

### Mixed-engine composition

Either side of a component-to-component edge can be interpreter-
backed or transpiler-backed. The seam is `[WitSource]`-tagged C#
interfaces (the same shape `wit-bindgen-csharp` and
`ExportInterfaceEmit` produce); `Wacs.ComponentModel.Runtime.ComponentBridge`
provides the cross-engine adapters.

**Transpiled A consumes interpreted B** — wrap the interpreter
component as a host bundle satisfying A's `[WitSource]` interface
contract. No per-import glue:

```csharp
using Wacs.ComponentModel.Runtime;

// B is an interpreter component instance.
var b = ComponentInstance.Instantiate(File.ReadAllBytes("B.component.wasm"));

// Build the typed bundle A's transpiled .dll expects. Each
// constructor parameter on `ABundleType` is a [WitSource]-tagged
// interface; AsHostBundle wires each one to a DispatchProxy that
// routes calls through b.Invoke(...).
var aBundle = ComponentBridge.AsHostBundle(b, typeof(ABundleType));

// Drop into A's generated module ctor as the host bundle slot.
var aModule = Activator.CreateInstance(
    aModuleType, importsStub, aBundle)!;
```

Or grab a single typed interface (useful when A's bundle takes
multiple sources — some interpreter, some real impls):

```csharp
var iStdout = ComponentBridge.AsTypedInterface<IStdout>(b);
var bundle = new MyMixedBundle(iStdout, /* …other deps */);
```

By default the proxy uses each method's `[WitSource].Item` as the
export name passed to `b.Invoke(...)`. Override via the
`exportNameMapper` parameter when the wasm-side path doesn't match
(typical for nested-interface exports).

**Interpreted A imports from transpiled B** — bind B's typed
exports as host functions on A's interpreter runtime:

```csharp
// B is a transpiled .dll instance.
var bModule = Activator.CreateInstance(bModuleType, importsStub, wasi)!;

// Bind B's IExports methods as host functions, addressable by
// their wit-qualified (module, entity) name. ComponentBridge walks
// the [WitSource] attributes to derive each binding key.
var runtime = new WasmRuntime();
ComponentBridge.BindAsImports(runtime, bModule, typeof(IBExports));

// Then instantiate A through the interpreter as usual — its
// imports now resolve through B.
var a = ComponentInstance.Instantiate(aBytes,
    rt => { /* runtime is already pre-bound; merge here */ });
```

The contract is symmetric: a transpiled component is a host
package by reflection over its `[WitSource]`-tagged interfaces, an
interpreter component is one through `ComponentBridge.AsHostBundle`.
The same code consumes either.

> **v0 scope:** `ComponentBridge` covers the freestanding-export
> shape (each interface method's `[WitSource].Item` is the wasm
> export name `ComponentInstance.Invoke` accepts). Nested-interface
> exports — where the wasm-side path is the qualified
> `<package>/<interface>.<item>` — work via the explicit
> `exportNameMapper` callback. Resource handles, async, and
> non-primitive arg marshaling on the `BindAsImports` direction
> ride incrementally.

### Building a custom host package

Drop `[WitSource]` on a plain C# interface and the resolver picks
it up — no source generator required:

```csharp
using Wacs.ComponentModel.Runtime;

[WitSource("interface env",
    Package = "local:demo@1.0.0", Interface = "env")]
public interface IEnv
{
    [WitSource("get-config: func() -> string",
        Package = "local:demo@1.0.0", Interface = "env",
        Item = "get-config")]
    string GetConfig();

    [WitSource("log: func(msg: string)",
        Package = "local:demo@1.0.0", Interface = "env",
        Item = "log")]
    void Log(string msg);
}

public sealed class MyHostBundle
{
    public IEnv Env { get; }
    public MyHostBundle(IEnv env) { Env = env; }
}
```

Pass the assembly via `--host-package ./MyHost.dll` (or by
reference at the API level). The resolver discovers `MyHostBundle`
automatically when a public read-only property's type matches the
`[WitSource]`-tagged interface; otherwise, point at it explicitly:

```csharp
var resolver = HostPackageResolver.FromAssemblies(
    assemblies: new[] { typeof(IEnv).Assembly },
    bundleType: typeof(MyHostBundle));
options.Resolver = resolver;
```

The generated module class's constructor takes
`object hostBundle` — pass an instance whose property types
satisfy the `[WitSource]` interface set, and the inline IL
dispatches through it.

## Remaining limitations (tracked for v0.3)

- **`--emit-main` on core-WASM modules rejects imports.** Use the
  component-mode path with `--wasip2` / `--host-package` instead;
  it threads imports through the typed bundle. A `--wasi-host` flag
  backed by `WACS.WASIp1` for the core-WASM path is still planned.
- **Scalar argv only** for core-WASM `--emit-main`. Ref-typed and
  `v128` params aren't parsed from argv. Component-mode `--emit-main`
  parses primitives, `bool`, `string`, and `byte[]`; aggregates
  (`Option<T>`, `list<T>`, records, variants) ride later.
- **Rare GC init patterns** (struct/array values in active element
  segments with non-i31 payloads) throw `NotSupportedException` from
  the codec at transpile time. Most modules — including CoreMark,
  typical emscripten/rustc output, and the spec suite — don't hit this.
- **Direct-linked import shapes outside the recognized matrix
  fall back to the delegate-table dispatch.** `CanEmitDirect`
  rejects unsupported shapes silently — by design, so a single
  unsupported import in a host package doesn't bring the whole
  transpile down. The call site keeps the legacy
  `ImportDelegates[i].Invoke` path for those bindings.
  When the emit IS attempted on an unsupported shape (e.g. a
  recognition-vs-emit mismatch in the resolver), the unsupported-
  pattern paths throw `InvalidOperationException` at IL-emit time
  with a message naming the offending `(module, entity)` and CLR
  method — never `InvalidProgramException` at runtime.
  Concrete shapes that still take the delegate path:
  - Resource **constructors** wrapping their handle in `Result<,>`
    (canon-ABI lowers `[constructor]X` to a single i32 handle —
    fallible construction is expressed as `[static]X.try-make`
    returning `result<own<X>, err>`, which DOES direct-link).
  - Variant case payloads of arbitrarily-nested aggregates
    (`variant<X(list<option<Y>>)>`) — the per-shape branch in
    `EmitVariantStoreAt` covers the common cases (primitive,
    string, byte[], primitive[], string[], tuple/record of
    primitives, `Option<X>`, `Result<X,Y>`, resource handle) but
    extreme nesting falls back.

## License

WACS.Transpiler is distributed under the
[Apache 2.0 License](https://github.com/kelnishi/WACS/blob/main/LICENSE).
