# WACS.ComponentModel.Parser

AOT-safe binary parser for WebAssembly Component Model
`.component.wasm` files. Pure byte walker — no reflection, no
`Reflection.Emit`, no analyzers required at consumer build time.
Multi-targets `net8.0` (AOT-marked) and `netstandard2.1`.

## Why it's a separate package

Split out from
[`WACS.ComponentModel`](https://www.nuget.org/packages/WACS.ComponentModel)
so harness / runtime consumers can load components under NativeAOT
or Unity IL2CPP without dragging in the full reflective
`ComponentInstance` + `ComponentBridge` surface.

If all you need is "give me a `.component.wasm` and tell me what's
inside" — types, imports, exports, embedded core modules, the
`wit-component` custom-section blob — this is the package. If you
need to actually instantiate and invoke a component, layer
`WACS.ComponentModel` on top.

## What's inside

```csharp
using Wacs.ComponentModel.Runtime;
using Wacs.ComponentModel.Runtime.Parser;

byte[] bytes = File.ReadAllBytes("hello.component.wasm");
using var ms = new MemoryStream(bytes);

ComponentModule cm = ComponentBinaryParser.Parse(ms);

// Top-level sections in file order
foreach (RawComponentSection s in cm.RawSections)
    Console.WriteLine($"{s.Id}: {s.Payload.Length} bytes");

// Embedded core-wasm binaries
foreach (byte[] coreBytes in cm.CoreModuleBinaries)
{
    // Feed to Wacs.Core's BinaryModuleParser, or to wasm-tools, etc.
}
```

The parser produces:

- **`ComponentModule`** — the parsed container; `RawSections`,
  `SectionsOf(id)`, `CoreModuleBinaries`, `NestedComponentBinaries`.
- **Structured section readers** (one per Component Model
  section ID): `TypeSectionReader`, `ImportSectionReader`,
  `ExportSectionReader`, `CanonSectionReader`,
  `InstanceSectionReader`, `CoreInstanceSectionReader`,
  `AliasSectionReader`, `CustomSectionReader`.
- **`ComponentBinaryReader`** — primitive byte walker; LEB128,
  preamble check, section framing.

## How it relates to the other WACS packages

| Package | Role |
|---|---|
| `WACS.ComponentModel.Parser` | **this** — pure byte walker → `ComponentModule` |
| [`WACS.ComponentModel`](https://www.nuget.org/packages/WACS.ComponentModel) | Builds on the parser; canonical-ABI lift/lower, `ComponentInstance.Instantiate`, resource handles, `ComponentBridge` |
| [`WACS.ComponentModel.Harness.Lib`](https://www.nuget.org/packages/WACS.ComponentModel.Harness.Lib) | Build-time IL emitter that consumes `ComponentModule` to emit typed `{World}Harness` assemblies |
| [`WACS.ComponentModel.Harness.Runtime`](https://www.nuget.org/packages/WACS.ComponentModel.Harness.Runtime) | AOT-safe runtime support emitted harnesses link against |
| [`WACS.Transpiler.Lib`](https://www.nuget.org/packages/WACS.Transpiler.Lib) | AOT WASM→IL transpiler; consumes the embedded core modules via this parser |

## AOT story

- `net8.0` target is annotated `IsAotCompatible=true`; the
  parser is verified by the WACS AOT acceptance gate
  (`AotAcceptanceTests`) — no new IL warnings.
- `netstandard2.1` target is offered for analyzers and other
  build-time tooling that can't take a net8.0 dependency.
- Zero allocations on the section-walk hot path beyond the
  `byte[]` payloads themselves; section payloads are sliced
  out of the input stream once, then handed to per-section
  readers on demand.

## License

Apache-2.0
