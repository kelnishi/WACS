# hello-world component-model reference fixture

**Pinned wit-bindgen-csharp output** for the Phase 1a.2 contract
slice. Ground truth that `CSharpEmitter` in `Wacs.ComponentModel`
must match byte-for-byte to preserve the roundtrip invariant with
`componentize-dotnet`.

## Contents

- `wit/hello.wit` — minimal WIT world exercising the reference
  slice: `wasi:cli/run` export, `wasi:cli/stdout` + `wasi:io/streams`
  imports. Matches the Phase 1a hello-world slice in the plan.
- `reference/*.cs` + `reference/*.wit` — wit-bindgen-csharp 0.30.0
  output, pinned. Bumping the pin requires regenerating these
  files from the matching tool version.

## Regenerating after a tool bump

```bash
# 1. Install the matching wit-bindgen CLI version.
cargo install wit-bindgen-cli --version 0.30.0 --locked

# 2. Populate the dep tree (deps/ is not committed — it mirrors
#    Spec.Test/components/wasi-cli/wit/deps plus wasi-cli's own
#    top-level .wit files under deps/cli/).
mkdir -p wit/deps/cli
cp Spec.Test/components/wasi-cli/wit/*.wit wit/deps/cli/
cp -r Spec.Test/components/wasi-cli/wit/deps/* wit/deps/

# 3. Regenerate the reference output.
rm -rf reference
wit-bindgen c-sharp -r native-aot -w hello --out-dir reference wit/

# 4. Bump EmitOptions.PinnedWitBindgenCSharpVersion in
#    Wacs.ComponentModel/CSharpEmit/EmitOptions.cs to match.

# 5. Clean up the dep tree (not committed).
rm -rf wit/deps
```
