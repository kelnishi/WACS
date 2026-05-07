# Wacs.Transpiler

Ahead-of-time WebAssembly → .NET IL transpiler. Lifts a wasm module
into a regular .NET assembly via `PersistedAssemblyBuilder` (.NET 9+) so
the result publishes through `dotnet publish /p:PublishAot=true` with
no Reflection.Emit at runtime.

## Contents

- **[Wacs.Transpiler.Lib/](Wacs.Transpiler.Lib/)** — programmatic API. The actual transpiler — `ModuleTranspiler`, IL emitters, AOT host-binding integration, RVA-mapped data segments, EmissionTarget.{Auto, Standard, AotLinked}. NuGet `WACS.Transpiler.Lib`; consumers embedding the transpiler in their own pipeline reference this.
- **[Wacs.Transpiler/](Wacs.Transpiler/)** — DEPRECATED CLI tool (`wasm-transpile`); superseded by `wacs build` / `wacs aot` in `Wacs.Console`. Kept only so the legacy NuGet `WACS.Transpiler` install path doesn't break for existing users.
- **[Wacs.Transpiler.Test/](Wacs.Transpiler.Test/)** — wast-spec equivalence (interpreter ↔ transpiled), AOT cross-process round-trip, branch-hint emission, init-data codec round-trip. Pulls fixtures from the spec submodule via `Spec.Test`.
