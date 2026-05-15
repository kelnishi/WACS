# wasi-webgpu-hello — v1 phase 3 session 10 parity fixture

Minimal smoke fixture for `WACS.WASI.GFX.Webgpu`. Exercises the
wired bindings that work against the host's stub backend today:

- `get-gpu()` — allocates a fresh gpu handle each call
- `gpu.get-preferred-canvas-format() -> gpu-texture-format`
  (primitive enum return, single i32 wire result)
- `gpu.wgsl-language-features() -> own<wgsl-language-features>`
  (resource-handle return)
- `wgsl-language-features.has(value: string) -> bool`
  (string-param decode at the host side)
- The matching `[resource-drop]` entries fire when the handles
  go out of scope.

Returns from `start()` on success; traps via `unreachable!` on
expectation mismatch — the WACS test runner reads the trap as a
failure signal.

## What this fixture does NOT exercise (yet)

The wasi:webgpu request-adapter / request-device chain and
everything downstream of it (buffer / shader / pipeline /
encoder / pass / queue.submit) needs the Silk-backed wgpu
dispatcher shipping in v1 phase 3 session 11. The host-side
bindings for those resources ARE wired (sessions 4-7) — only
the backend implementation is gated.

`hello_compute` and `skybox` parity fixtures (full pipeline
dispatch through wgpu-native) land alongside session 11 when
the actual GPU code path is in place.

## WIT pins

WIT files vendored from `WebAssembly/wasi-gfx@03c3e95493` —
the same pin `Wacs.WASI.GFX.Webgpu/wit/deps.lock` carries.
Re-vendor in sync if either side updates.

## Build

```
cargo build --release --target wasm32-unknown-unknown
wasm-tools component new \
    target/wasm32-unknown-unknown/release/wasi_webgpu_hello.wasm \
    -o wasm/hello.component.wasm
```

The resulting `wasm/hello.component.wasm` (~16 KB) is checked
in so consumers don't need the Rust toolchain to run the
fixture.
