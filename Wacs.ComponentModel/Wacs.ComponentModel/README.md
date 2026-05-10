# WACS.ComponentModel

WebAssembly Component Model runtime for [WACS](https://github.com/kelnishi/WACS) — WIT parsing,
canonical-ABI lift/lower, resource handles, and component instantiation. Layered on the same
parse / validate / link pipeline WACS uses for core modules; works with both the interpreter
and the AOT transpiler engines.

## What's inside

- **Component runtime** — `ComponentInstance.Instantiate`, resource registries, variant /
  option / result / list / record / tuple / flags lift+lower
- **Canonical ABI** — full lift/lower including aggregate returns through retArea, UTF-8 /
  UTF-16 / Latin-1 string encodings, recursive variant arms, instance-method resource calls
- **WIT parser** — vendored upstream grammar, `WitLoader` resolves `use` chains across
  packages, embedded WIT files in assemblies via `WitContract.FromAssembly`
- **`ComponentBridge`** — cross-engine adapter that lets interpreted and transpiled
  components compose against the same typed contract
- **`Linker.Validate(WitContract)`** — link-time check that bound host implementations
  match the WIT contract (catches drift before the component runs)

## Install

```bash
dotnet add package WACS.ComponentModel
```

## Quick start

```csharp
using Wacs.ComponentModel.Runtime;

var bytes = File.ReadAllBytes("hello.component.wasm");
var ci = ComponentInstance.Instantiate(bytes,
    rt =>
    {
        // configure host bindings on the underlying WasmRuntime
        // — `--bind` shims, WASI Preview 2 hosts, custom IBindables
    });

var result = ci.Invoke("greet", new object?[] { "world" });
Console.WriteLine(result);
```

For the WASI Preview 2 host implementations (cli / clocks / filesystem / http / io /
random / sockets), pair this package with [`WACS.WASI.Preview2`](https://www.nuget.org/packages/WACS.WASI.Preview2)
or [`WACS.WASI.Preview2.DependencyInjection`](https://www.nuget.org/packages/WACS.WASI.Preview2.DependencyInjection).

For programmatic WIT ↔ C# binding generation, see
[`WACS.ComponentModel.Bindgen.Lib`](https://www.nuget.org/packages/WACS.ComponentModel.Bindgen.Lib).

## Engines

This package handles the component-model layer; the underlying core wasm executes through
either:

- **Interpreter** ([`WACS`](https://www.nuget.org/packages/WACS)) — AOT-safe, ~270–385 iter/s
  on CoreMark, suitable for Unity IL2CPP / `PublishAot`.
- **Transpiler** ([`WACS.Transpiler.Lib`](https://www.nuget.org/packages/WACS.Transpiler.Lib))
  — `Reflection.Emit` AOT, ~17 500 iter/s on CoreMark, ~50% of native C speed.

The `wacs run --wasip2` CLI verb auto-routes to the transpiler engine when WASI Preview 2
host packages are wired. See the top-level
[WACS README](https://github.com/kelnishi/WACS#component-model--wasi-preview-2) for the full
matrix.

## Documentation

- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
  — runtime requirements, host-package composition, end-to-end CLI / embedder examples
- [`Wacs.ComponentModel/Validation/README.md`](https://github.com/kelnishi/WACS/blob/main/Wacs.ComponentModel/Wacs.ComponentModel/Validation/README.md)
  — WIT contract validation deep-dive

## License

Apache-2.0
