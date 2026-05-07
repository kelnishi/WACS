# Wacs.ComponentModel

WebAssembly Component Model implementation — WIT parsing, canonical-ABI
lift/lower, runtime resource handles, and C#-emit / forward & reverse
bindgen for guest and host code generation.

## Contents

- **[Wacs.ComponentModel/](Wacs.ComponentModel/)** — runtime + types. WIT parser, canonical-ABI engine, `ComponentInstance`, resource tables, validation, `CSharpEmitter`. Pinned to `wit-bindgen-csharp` shape conventions for round-trip compatibility with upstream tooling.
- **[Wacs.ComponentModel.Bindgen.Lib/](Wacs.ComponentModel.Bindgen.Lib/)** — programmatic bindgen API: forward (`.wit` → C#) and reverse (`.dll` → WIT + C#). Wrapped by the `wit-bindgen-wacs` CLI tool.
- **[Wacs.ComponentModel.Bindgen.SourceGen/](Wacs.ComponentModel.Bindgen.SourceGen/)** — Roslyn `IIncrementalGenerator` that emits host interfaces from `<AdditionalFiles WitForHost="true">` `.wit` inputs at build time.
- **[Wacs.ComponentModel.Test/](Wacs.ComponentModel.Test/)** — runtime tests: lift/lower fixtures, dual-engine equivalence (interpreter vs. transpiler), 68+ component fixtures.
- **[Wacs.ComponentModel.Bindgen.Test/](Wacs.ComponentModel.Bindgen.Test/)** — bindgen forward/reverse round-trip tests.
