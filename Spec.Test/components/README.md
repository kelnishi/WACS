# Spec.Test/components

Component-model test fixtures + the `wasi-cli` submodule that supplies
WASI WIT for the hello-world reference output.

## Layout

- **`wasi-cli/`** — git submodule pinned at `v0.2.8` (currently).
  Tracks upstream `WebAssembly/wasi-cli` so the loader/emitter tests
  have a stable WASI 0.2.x WIT tree to exercise. The runtime-side
  `Wacs.WASI.Preview2/wit/` is a separate vendored copy that may be
  at a different version when the two coordinates intentionally
  diverge — see that package's CHANGELOG.
- **`fixtures/`** — 135 hand-crafted component fixtures exercising
  specific canonical-ABI shapes (primitives, records, variants, enums,
  resources, lists, options, results, every WASI subsystem).
  - `<fixture>/wit/<base>.wit` — world definition
  - `<fixture>/wit/<base>.wat` — wasm-text-format core module source
    (when the fixture compiles to wasm)
  - `<fixture>/wit/.encoding` — optional override (`utf16` or
    `compact-utf16`). Three fixtures use this; default is `utf8`.
  - `<fixture>/deps/<pkg>/*.wit` — optional vendored WASI deps
  - `<fixture>/wasm/<base>.component.wasm` — committed compiled output
    (when applicable)
  - `<fixture>/reference/*.cs` — pinned wit-bindgen-csharp output
    (hello-world only)
- **`build_fixtures.sh`** — regenerate every `<fixture>/wasm/*.component.wasm`
  from its sources. Use `--check` to verify drift in CI without
  modifying the working tree.
- **`build_hello_world_reference.sh`** — regenerate the hello-world
  fixture's `reference/*.cs` via `wit-bindgen-csharp`. Pin-locked to
  the version `EmitOptions.PinnedWitBindgenCSharpVersion` declares;
  bumping requires updating both in lockstep.

## Regeneration recipes

### After bumping `<fixture>/wit/<base>.wat` or `.wit`

```sh
# Verify the committed wasm still matches:
bash Spec.Test/components/build_fixtures.sh --check

# Regenerate (in-place):
bash Spec.Test/components/build_fixtures.sh

# Or just one fixture:
bash Spec.Test/components/build_fixtures.sh \
    Spec.Test/components/fixtures/wasi-environment-component
```

Requires `wasm-tools` 1.221+ (`cargo install wasm-tools`).

### After bumping the `wasi-cli` submodule

```sh
# Update the submodule pointer.
cd Spec.Test/components/wasi-cli
git fetch --tags
git checkout v0.2.X
cd ../../..
git add Spec.Test/components/wasi-cli

# Sed-replace `@0.2.<old>` → `@0.2.X` across every fixture's WIT +
# WAT + test assertions, then regenerate:
bash Spec.Test/components/build_fixtures.sh

# Regenerate the hello-world reference (separate tool dependency):
bash Spec.Test/components/build_hello_world_reference.sh
```

The 9 `v0_2_<old>`-baked filenames under
`fixtures/hello-world/reference/` change when wit-bindgen emits a new
namespace shape — `build_hello_world_reference.sh` deletes the old
output set and re-emits, so renames happen as deletions + creations.

### CI integration

Both scripts support `--check` mode that exits non-zero on drift:

```yaml
- name: Verify fixture binaries match sources
  run: bash Spec.Test/components/build_fixtures.sh --check

- name: Verify hello-world reference matches wit-bindgen output
  run: bash Spec.Test/components/build_hello_world_reference.sh --check
```

The hello-world check requires `wit-bindgen-cli 0.30.0` on the runner
— if not available, gate the step on a tool-availability conditional.

## Why the wasi-cli submodule and Wacs.WASI.Preview2/wit are decoupled

The two coordinates currently track the same WASI patch (0.2.8 each),
but they're decoupled by design — moving the submodule forces
regenerating the entire fixture set (including the
`v0_2_<version>`-baked reference filenames in
`fixtures/hello-world/reference/`), which is non-trivial. The
runtime-side `Wacs.WASI.Preview2/wit/` is what gets emitted as
`[WitSource]` interfaces and what `wacs inspect --imports` shows; it
can move forward independently because the wasm Component Model
treats minor revisions of WASI as ABI-stable, and the
`HostPackageResolver` / `WasmRuntime.GetBoundEntity` version-tolerant
fallback (in PRs #119 / #120) lets guests at any 0.2.x version bind
to host packages registered at any other 0.2.x version.
