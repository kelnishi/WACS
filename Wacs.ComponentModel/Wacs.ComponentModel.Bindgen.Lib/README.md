# WACS.ComponentModel.Bindgen.Lib

Programmatic WIT ↔ C# binding generator for the
[WACS Component Model](https://www.nuget.org/packages/WACS.ComponentModel) — both
directions:

- **Forward**: `.wit` → C# typed interfaces (host- and guest-side), tagged with
  `[WitSource]` so `HostPackageResolver` can match imports at transpile time
- **Reverse**: transpiled `.dll` → regenerated `.wit` + bindings (round-trip / refactoring
  workflows)

This is the library API used by the `wacs bindgen` verb in
[`WACS.Cli`](https://www.nuget.org/packages/WACS.Cli). Reference it directly from custom
build steps, IDE integrations, or codegen pipelines that want full control over the
generation.

## Install

```bash
dotnet add package WACS.ComponentModel.Bindgen.Lib
```

## Forward — WIT to C#

```csharp
using Wacs.ComponentModel.Bindgen;

var generator = new ForwardBindgen();
var result = generator.Generate(
    witPath: "wit/cli/world.wit",
    options: new ForwardOptions
    {
        Namespace = "MyApp.Wasi",
        TargetSide = BindingSide.Host,   // or Guest
    });

foreach (var (path, source) in result.Files)
    File.WriteAllText(Path.Combine("./gen", path), source);
```

## Reverse — DLL to WIT

```csharp
var reverse = new ReverseBindgen();
var (witFiles, csFiles) = reverse.Regenerate("path/to/transpiled.dll");

File.WriteAllText("regen/world.wit", witFiles["world.wit"]);
```

## Build-time source generation

For projects that consume WIT at compile time (no separate `wacs bindgen` invocation), the
sibling source generator
[`WACS.ComponentModel.Bindgen.SourceGen`](https://github.com/kelnishi/WACS/tree/main/Wacs.ComponentModel/Wacs.ComponentModel.Bindgen.SourceGen)
runs inside the Roslyn pipeline. Both packages share the same emission core; this library
exists for the imperative case where the codegen needs to drive directly (e.g. tooling that
iterates on WIT during development).

## Documentation

- Top-level WACS [README](https://github.com/kelnishi/WACS#component-model--wasi-preview-2)
- [`docs/COMPONENT_CHAINING.md`](https://github.com/kelnishi/WACS/blob/main/docs/COMPONENT_CHAINING.md)
  for the runtime side

## License

Apache-2.0
