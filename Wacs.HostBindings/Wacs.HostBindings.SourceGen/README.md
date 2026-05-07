# WACS.HostBindings.SourceGen

Roslyn source generator that auto-generates the `IImports` adapter for a
transpiled wasm module's host functions, using `[WacsImport]`-annotated
static methods discovered in the consumer's compilation references.

## What it does

For every `[assembly: WacsTranspiledImports(typeof(T))]` attribute the
generator finds across the compilation:

1. Scans referenced assemblies for `[WacsImport(module, name)]` static
   methods.
2. Pairs each method on the `IImports` interface with its matching binding
   (by wasm import name).
3. Emits a `GeneratedHostImports : IImports` partial class whose method
   bodies forward to the matched bindings.
4. Threads any binding-required shared-state parameters
   (e.g. `Wacs.WASI.Preview1.State`) through a constructor.

If no binding is found for an import, the generator emits a `WACS001` error
diagnostic so the compile fails with a clear message rather than at runtime.

## Diagnostics

- `WACS001` (error): no binding found for `<module>.<name>`
- `WACS002` (error): binding signature mismatch
- `WACS003` (error): multiple bindings registered (ambiguous)
- `WACS004` (info): matched binding (verbose; off by default)

## Consumer usage

Add the generator alongside your binding package(s):

```xml
<ItemGroup>
  <Reference Include="MyTranspiledApp" HintPath="MyTranspiledApp.dll" />
  <PackageReference Include="WACS.WASI.Preview1" />
  <PackageReference Include="WACS.HostBindings.SourceGen" />
</ItemGroup>
```

Then in your code:

```csharp
var state = new Wacs.WASI.Preview1.State { /* ... */ };
var imports = new MyTranspiledApp.WacsGenerated.GeneratedHostImports(state)
{
    MemoryProvider = () => GetWasmMemoryView(),  // wire to your runtime
};
var module = new MyTranspiledApp.Module(imports);
module._start();
```

`wacs aot` wires the package reference + memory provider for you; the
manual setup above is for embedders who construct the host process by hand.
