# Phase 4 acceptance fixtures

## `cli-hello`

WASIp3 vertical-slice acceptance: a component that exports
`wasi:cli/run@0.3.0-rc-2026-03-15` and, when run, writes
`"hello, wasip3\n"` to `wasi:cli/stdout`.

### Files

- `cli-hello.rs` — Rust source (vendored copy of the source
  in `Spec.Test/wasi/tests/rust/wasm32-wasip3/src/bin/`).
- `cli-hello.json` — operations sidecar.
- `cli-hello.wasm` — compiled component (release build).

### Rebuild

```
rustup +nightly target add wasm32-wasip2
cargo install wasm-component-ld
cargo +nightly build --release \
    --manifest-path=Spec.Test/wasi/tests/rust/wasm32-wasip3/Cargo.toml \
    --target=wasm32-wasip2 \
    --bin=cli-hello
cp Spec.Test/wasi/tests/rust/wasm32-wasip3/target/wasm32-wasip2/release/cli-hello.wasm \
   Wacs.WASI/Wacs.WASI.Preview3/Wacs.WASI.Preview3.Test/Fixtures/cli-hello.wasm
```

`wasm32-wasip3` target triple doesn't exist yet; the toolchain
builds against `wasm32-wasip2` and `wasm-component-ld`
synthesizes the wasip3 component with embedded wasip2 facades.

### WIT shape

Verified via `wasm-tools component wit`:

```
world root {
  import wasi:cli/types@0.3.0-rc-2026-03-15;
  import wasi:cli/stdin@0.3.0-rc-2026-03-15;
  import wasi:cli/stdout@0.3.0-rc-2026-03-15;
  // ... wasip2 facade imports (auto-injected by
  //     wasm-component-ld for the wasm32-wasip2 build target)

  export wasi:cli/run@0.2.0;
  export wasi:cli/run@0.3.0-rc-2026-03-15;
}
```
