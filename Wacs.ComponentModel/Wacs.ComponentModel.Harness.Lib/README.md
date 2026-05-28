# WACS.ComponentModel.Harness.Lib

Build-time IL emitter for typed WIT harnesses. Takes a WIT
contract (either a directory of `.wit` files or a pre-parsed
`CtPackage` set) and emits a .NET assembly containing the
typed `{World}Harness` class with `LoadFrom(byte[])` + per-export
typed methods.

Targets `net9.0` to use
[`PersistedAssemblyBuilder`](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.persistedassemblybuilder)
for the AOT-clean save+load round-trip.

## Two ergonomic surfaces over one emit core

```csharp
using Wacs.ComponentModel.Harness.Lib;

// Persisted (the `wacs harness gen` flow) — emit a .dll on disk
HarnessEmitter.EmitToFile(
    witDirectory: "./wit",
    outputPath:   "./HelloHarness.dll",
    options:      new HarnessOptions { Namespace = "MyApp.Generated" });

// In-memory (the `wacs run --wit-dir` flow) — load + use immediately
Assembly asm = HarnessEmitter.EmitInMemory("./wit");
Type harness = asm.GetType("Wacs.ComponentModel.Harness.Generated.HelloHarness");
var instance = harness.GetMethod("LoadFrom")!
    .Invoke(null, new object[] { File.ReadAllBytes("hello.component.wasm") });

// Lowest-level: bring your own Stream
using var ms = new MemoryStream();
HarnessEmitter.EmitToStream("./wit", ms);
```

Both shapes funnel through one `EmitToStream` so the emit path
stays single-source. Mirrors the way `Wacs.Transpiler.Lib`
exposes both `Bake`-to-memory and `SaveAssembly`-to-disk paths.

## What the emitted harness looks like

For a world like:

```wit
package example:hello@0.1.0;
world hello {
    export greet: func(name: string) -> string;
}
```

The emitter produces:

```csharp
namespace Wacs.ComponentModel.Harness.Generated
{
    public sealed class HelloHarness
    {
        public static HelloHarness LoadFrom(byte[] componentBytes) { … }

        public string Greet(string name) { … }
    }
}
```

…where the body of `Greet` is canonical-ABI IL: lower the
`string` into wasm linear memory via `cabi_realloc`, call the
core export, lift the return string back out. The IL only calls
into
[`WACS.ComponentModel.Harness.Runtime`](https://www.nuget.org/packages/WACS.ComponentModel.Harness.Runtime)
and `WACS` — no reflection at the call site.

## Cross-engine symmetry

The interpreter-side `{World}Harness` (this package) and the
transpiler-side `{World}HarnessImpl : I{World}` (emitted by
`Wacs.Transpiler.Lib`) both implement the same `I{World}`
contract. The engine choice becomes a deployment choice, not
an API choice — embedders bind once and swap the underlying
runtime.

See
[`docs/WIT_HARNESS_APPROACH.md`](https://github.com/kelnishi/WACS/blob/main/docs/WIT_HARNESS_APPROACH.md)
for the full cross-engine story.

## How it relates to the other WACS packages

```
.wit files
   │
   ▼  (build time, via WACS.Cli or programmatically)
WACS.ComponentModel.Harness.Lib   ──► {World}Harness.dll
   │ references at build time              │
   ▼                                       ▼ links against at runtime
WACS.ComponentModel.Parser           WACS.ComponentModel.Harness.Runtime
WACS.ComponentModel                  WACS  (interpreter)
WACS.Core
```

| Package | Role |
|---|---|
| `WACS.ComponentModel.Harness.Lib` | **this** — the emitter; build-time tool |
| [`WACS.ComponentModel.Harness.Runtime`](https://www.nuget.org/packages/WACS.ComponentModel.Harness.Runtime) | The runtime primitives emitted harness DLLs link against |
| [`WACS.ComponentModel.Parser`](https://www.nuget.org/packages/WACS.ComponentModel.Parser) | Read the `.component.wasm` shape |
| [`WACS.ComponentModel`](https://www.nuget.org/packages/WACS.ComponentModel) | WIT parsing + canonical-ABI infrastructure the emitter consumes at build time |
| [`WACS.Cli`](https://www.nuget.org/packages/WACS.Cli) | `wacs harness gen` verb wraps `EmitToFile`; `wacs run --wit-dir` wraps `EmitInMemory` |
| [`WACS.Transpiler.Lib`](https://www.nuget.org/packages/WACS.Transpiler.Lib) | Alternative engine — emits a `{World}HarnessImpl : I{World}` against the same `I{World}` contract |

## When to use this

- You have a `.component.wasm` shipped with a known WIT world
  and want a typed C# wrapper for it.
- You want the emitted harness as a build artifact — checked
  in, shipped alongside your app, callable without the emitter
  in the runtime image.
- You want to keep the runtime image AOT-safe (NativeAOT or
  Unity IL2CPP). The emitted IL only references the AOT-safe
  `Harness.Runtime` package.

For the **dependency-injection** consumer shape (the
`AddWacs*`-style fluent extension methods), pair with the
host-package DI siblings (`WACS.WASI.Preview2.DependencyInjection`,
etc.) — the harness sits underneath those layers.

## License

Apache-2.0
