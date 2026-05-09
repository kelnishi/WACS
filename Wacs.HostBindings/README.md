# Wacs.HostBindings

Attribute + source-generator infrastructure for binding host C# methods
to WASM imports on the transpiler's NativeAOT path. Tagged classes get
their dispatch glue emitted at compile time — no runtime reflection in
the AOT-published binary.

## Contents

- **[Wacs.HostBindings.Abstractions/](Wacs.HostBindings.Abstractions/)** — public attributes (`[WacsImport]`, `[WacsImportName]`, `[WacsTranspiledImports]`, `[WasmName]` for round-tripping wasm names through CLR-identifier sanitization, `[assembly: WasiHostPackage]` for `runtime.AutoDiscoverHostPackages()`) + runtime types (`WacsHostMemory`, `WacsHostFault`). Referenced by `Wacs.Transpiler.Lib`, `Wacs.WASI.Preview1`, `Wacs.WASI.NN`, `Wacs.WASI.Threads`, and any third-party host-binding package.
- **[Wacs.HostBindings.SourceGen/](Wacs.HostBindings.SourceGen/)** — Roslyn `IIncrementalGenerator` that scans `[WacsImport]`-tagged host statics and emits the dispatch shim consumed by transpiled assemblies. Single consumer: `Wacs.Transpiler.Lib`.
- **[Wacs.HostBindings.Test/](Wacs.HostBindings.Test/)** — bounds-check + accessor coverage for `WacsHostMemory` (the only non-trivial Abstractions type).
