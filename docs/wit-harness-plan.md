# WIT-shaped harness for AOT (Unity IL2CPP) Component Model

## Problem

`Wacs.Core` (the interpreter, parser, runtime) is AOT-safe and has shipped under Unity IL2CPP since the project's first release. The newer `Wacs.ComponentModel` runtime is **not** AOT-safe: its canonical-ABI lift/lower in `ComponentInstance` uses `Type.MakeGenericType` / `MethodInfo.MakeGenericMethod` / `Activator.CreateInstance(Type)` at every call boundary to materialize `Option<T>` / `Result<T,E>` / `list<T>` / variant arms from runtime WIT type info. IL2CPP rejects these at build time (or worse, throws `ExecutionEngineException` at runtime) because the generic specializations aren't statically rooted.

Today the public entry points (`ComponentInstance.Instantiate(bytes)`, `ComponentBridge.AsTypedInterface<T>`, `WitContract.FromAssembly`, `HostInterfaceRuntime.InvokeStaticFactoryReflective`) carry `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]` so AOT builds fail at compile time with a clear message rather than silently. That's the honest annotation — but it doesn't actually let Unity users load wasm components.

## The harness design

**At build time** (dev machine, Reflection.Emit / SourceGen allowed):
- A new source generator (`WACS.ComponentModel.Harness.SourceGen`) takes a WIT contract — either a `.wit` file in the user's project or the embedded WIT section of a reference `.component.wasm` — and emits typed C# code:
  - Per WIT type: a concrete CLR record (`MyRecord`, `Frame`, `Pixel`, …).
  - Per WIT enum/variant: a CLR enum or sealed class hierarchy.
  - Per lift point (component import/export): a non-generic typed method that reads/writes the WIT shape against `MemoryInstance` directly. All generic specializations are baked in by the SourceGen at user-build time — IL2CPP sees the closed types.
  - A typed wrapper class (`MyComponentHarness`) exposing the contract's exports as ordinary C# methods.
- The harness is shipped as part of the user's assembly. IL2CPP transpiles it normally.

**At runtime** (Unity device):
- User loads arbitrary `.component.wasm` bytes (network, disk, asset bundle, …).
- Calls `MyComponentHarness.LoadFrom(bytes)`.
- The harness:
  1. Walks the component binary using `Wacs.Core`'s parser to extract the embedded WIT section and the core wasm module(s).
  2. Validates the binary's WIT contract against the harness's compile-time-known contract (structural match — see "Validation" below). Mismatch → typed `WitContractMismatchException`, never an `ExecutionEngineException`.
  3. Instantiates the core wasm via `Wacs.Core` (already AOT-clean).
  4. Wires the typed lift/lower paths the SourceGen baked in.
- User calls `harness.Run(input)` against the typed surface; no reflection at runtime.

## Why this works under IL2CPP

- The harness's generic specializations (`Option<MyRecord>`, `List<Frame>`, …) are materialized in source code at user-build time, not at runtime.
- IL2CPP sees them all when transpiling the user's assembly → C++ → native. No "missing generic type" errors.
- `Wacs.Core` is the only thing executing at runtime besides the typed harness code. It's AOT-clean today.
- `Wacs.ComponentModel.Runtime.ComponentInstance` is **not on the runtime path** for harness consumers — they don't reference it from their app code.

## Validation contract

The harness has compile-time-known WIT shape. The loaded `.component.wasm` carries its WIT in a `component-type` custom section emitted by `wit-component`. Validation walks both and confirms structural compatibility:

- **Export set match**: every method the harness exposes must have a matching component export.
- **Import set subset**: the component's imports must be a subset of what the harness wires (a component asking for an import the harness doesn't supply → reject).
- **Type shape match per export/import**: parameter and return WIT types must match the harness's expected shape. Fields, variant arms, list elements all checked recursively.

The validation is the same shape as `Linker.Validate(WitContract.FromAssembly(...))` today, but extracts the contract from the loaded `.component.wasm` (not the bindings assembly) and compares against the SourceGen-emitted contract metadata. A new `WitContractDiff` result type lists every mismatch for diagnostic purposes.

## Industry alignment

This is essentially the same model as **componentize-dotnet** + **wit-bindgen-csharp** — both projects already generate typed C# wrappers from WIT. The difference is the runtime target:

| | componentize-dotnet | WACS harness (this plan) |
|---|---|---|
| Runtime target | Wasmtime (native) | `Wacs.Core` interpreter (pure C#) |
| AOT target | NativeAOT | NativeAOT **and** Unity IL2CPP |
| Generated code consumes | `System.Runtime.InteropServices.JavaScript` | `Wacs.Core` primitives (`MemoryInstance`, `Store`, `Frame`) |
| Lift/lower codegen shape | Wasmtime hosted-call FFI | Typed C# against `Wacs.Core`'s memory + invocation API |

So the broader pattern is well-precedented. The Wacs-specific work is the generator's emit shape against `Wacs.Core`'s primitives.

## Work packages

### Package 1 — `WACS.ComponentModel.Harness.SourceGen`
A new Roslyn source generator. Inputs (compile-time):
- A `.wit` file under the user's project (`<AdditionalFiles Include="my.wit" />`), OR
- An assembly attribute pointing at a `.component.wasm`'s WIT custom section (`[ComponentContract("my.component.wasm")]`).

Outputs:
- One harness class per WIT world (`MyComponentHarness`).
- Per-WIT-type records / enums / variant hierarchies.
- Per-import: an interface the user implements (or a `partial` method the user fills in).
- Per-export: a typed method on the harness.
- A `_WitContract` static field carrying the compile-time-known WIT shape for runtime validation.

Estimated scope: ~3-5k LOC. Builds on `Wacs.ComponentModel.Bindgen.Lib` for WIT parsing.

### Package 2 — `WACS.ComponentModel.Harness.Runtime`
A small runtime that the generated harness code calls into. AOT-clean — no reflection. Surface:
- `MemoryReader` / `MemoryWriter` extension methods over `MemoryInstance` for canonical-ABI primitives (u8/u16/u32/u64/s8…/f32/f64/utf8/utf16/latin1/list-pointer-pair).
- `ComponentLoader.Load(bytes)` → returns the core wasm module + extracted WIT custom section.
- `WitContractCompare.Match(expected, actual)` → `WitContractDiff` (typed mismatches).
- `WitContractMismatchException` for the validation failure case.

Estimated scope: ~1-2k LOC. Could live as a subnamespace inside `Wacs.ComponentModel` rather than a new package — dependency-light.

### Package 3 — Unity IL2CPP spike
Before any of the above ships:
1. Pick the smallest existing parity fixture (`wasi-webgpu-hello-compute` or `wasi-gfx-rectangle`).
2. Generate a typed harness for its WIT contract **by hand** (no SourceGen yet — just to validate the runtime shape).
3. Drop the hand-written harness + `Wacs.Core` into a minimal Unity project. Build with IL2CPP. Run on device.
4. Document every IL2CPP error encountered; iterate the harness shape until it runs cleanly.
5. Use the verified shape as the spec for what the SourceGen needs to emit.

Estimated scope: 1-2 day spike + iteration. Output: `docs/wit-harness-unity-spike.md` capturing what works and what doesn't.

### Package 4 — Documentation + samples
- `docs/WIT_HARNESS_USAGE.md` — embedder guide. How to add a `[ComponentContract]` to your project, how to call into the harness, how to handle validation failures.
- `Spec.Test/components/fixtures/unity-harness-demo/` — a complete demo project that builds for Unity IL2CPP.

## Out of scope (for the harness PR)

- Multi-version WIT support: each harness binds to one WIT version. Loading a component with a different (but compatible) version requires either (a) a separate harness or (b) a compatibility-layer WIT generator. Defer.
- Dynamic-shape components (where the WIT contract isn't known at build time). Fundamentally incompatible with AOT — no path planned. Consumers stuck on dynamic shapes use `ComponentInstance.Instantiate(bytes)` under JIT, with the existing `[RequiresDynamicCode]` annotation.
- Cross-engine bridges (`ComponentBridge.AsTypedInterface<T>`). The harness IS the typed interface natively — bridges are unnecessary in the AOT model.
- Component-model resource handles with cross-language ownership semantics. Land as a follow-up if the spike surfaces non-trivial complications.

## Migration story

For consumers already using `ComponentInstance.Instantiate(bytes)`:
- **JIT (desktop, dev)**: keep using it. The annotation is informational, doesn't break anything.
- **NativeAOT**: switch to the harness model. The generated typed surface is more ergonomic anyway (typed methods + typed types) compared to the `object[]`-based `Invoke()` API.
- **Unity IL2CPP**: must use the harness model. The existing path was never supported under IL2CPP — annotation makes that explicit.

## Open questions for the harness PR

1. **Where does the SourceGen pull its WIT input from?** Two paths in tension:
   - `.wit` file as `AdditionalFiles` — clean, but requires the user to manage `.wit` files alongside their `.component.wasm`.
   - Embedded WIT in `.component.wasm` via `[ComponentContract("path.wasm")]` — ergonomic, but the SourceGen needs to parse the wasm at compile time.
   Probably **support both** — `.wit` is the canonical input for new projects, the embedded path is the migration on-ramp.
2. **Should the harness be a standalone package or live inside `WACS.ComponentModel`?** Standalone is cleaner from a dependency-graph perspective; living inside `WACS.ComponentModel` reduces NuGet metadata sprawl. Lean towards standalone — keeps the existing `WACS.ComponentModel` runtime separate from the harness's AOT-clean surface.
3. **How does the harness handle WASI imports?** wasi-cli / wasi-clocks / wasi-fs / wasi-io / wasi-random are all "host-implemented" — the harness shouldn't try to wrap them, the existing `WACS.WASI.Preview2` bundle stays the implementation. The harness's job is to wire the application's WIT, not WASI's.
4. **What's the version-number story for the harness packages?** Probably starts at 0.1.0; tracks the WIT spec version it codegens against (currently `wasi:io@0.2.x`, component-model spec freeze).

## Triggering event

The plan was sketched during the wasi-gfx-v1 warning hygiene pass, after the user pointed out that the AOT-safety concern for `Wacs.Core` is real (Unity IL2CPP target) and Component Model is untested under those constraints. The pass closed by annotating the existing `ComponentInstance` / `ComponentBridge` / `WitContract.FromAssembly` / `HostInterfaceRuntime` public entries with `[RequiresDynamicCode]` + `[RequiresUnreferencedCode]` (PR #N — `warnings-nullable-fixes` branch). The harness is the forward path that makes the Component Model AOT story real.
