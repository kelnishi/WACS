# Wacs.Core

The interpreter core + the source generator that builds the Switch
Runtime's monolithic dispatcher. Everything in this folder is on the
build / runtime path of every WACS consumer.

## Contents

- **[Wacs.Core/](Wacs.Core/)** — WebAssembly 3.0 interpreter (`WasmRuntime`, `BinaryModuleParser`, `TextModuleParser`, polymorphic + switch runtimes, full op set inc. SIMD / GC / threads / branch-hints). Authoritative spec layer; all other Wacs.* packages depend on this.
- **[Wacs.Core.Test/](Wacs.Core.Test/)** — interpreter unit tests + WAT/WAST round-trip + non-spec integration tests (binding, tail-calls, atomics, threads). Sequential xunit (`xunit.runner.json`) for the pre-existing `WasmRuntime` static-state race.
- **[Wacs.Compilation/](Wacs.Compilation/)** — Roslyn source generator that emits `GeneratedDispatcher.Run` from `[OpSource]` / `[OpHandler]`-tagged methods. `IsRoslynComponent=true`; referenced as `OutputItemType=Analyzer`; only the Switch Runtime consumes its output.
- **[Wacs.Compilation.Test/](Wacs.Compilation.Test/)** — generator unit tests + Switch Runtime end-to-end smoke (BytecodeCompiler / SwitchRuntime / branch-hint emission).
