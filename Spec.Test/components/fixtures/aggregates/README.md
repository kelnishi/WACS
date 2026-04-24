# aggregates component-model reference fixture

**Pinned wit-bindgen-csharp output** for the aggregate type
emission slice. Covers the structural aggregate types in
component-model interface signatures: `list<T>`, `option<T>`,
`result<T, E>` (both return and param positions), `tuple<…>`,
and `string`.

Used by `CSharpEmitterTests.Aggregate_*` tests to lock down our
emitter's shape against wit-bindgen-csharp 0.30.0 so any drift
in either the tool or our implementation is a loud test failure.

## Contents

- `wit/agg.wit` — synthetic WIT exercising each aggregate in
  both return and parameter position.
- `reference/*.cs` (+ `_component_type.wit`) — wit-bindgen-csharp
  0.30.0 output, pinned.

## Regenerating after a tool bump

```bash
cargo install wit-bindgen-cli --version 0.30.0 --locked
rm -rf reference
wit-bindgen c-sharp -r native-aot -w agg-world --out-dir reference wit/
```
