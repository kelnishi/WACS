# wasi-gfx-rectangle — WACS.WASI.GFX v0 parity fixture

CPU-path parity guest for `WACS.WASI.GFX`. Opens a
surface, subscribes to resize / pointer-up / frame
events, and draws a 100×100 rectangle in red (toggles
to green on pointer-up) on a gray background each
frame.

Mirrors the upstream
[`wasi-gfx-runtime/examples/apps/rectangle_frame_buffer`](https://github.com/wasi-gfx/wasi-gfx-runtime/tree/main/examples/apps/rectangle_frame_buffer)
example, except the v0-compatible `rectangle` world in
`wit/world.wit` drops the `wasi:webgpu` inclusion (WACS
v0 covers `graphics-context` + `surface` +
`frame-buffer` only).

## WIT pins

WIT files vendored from `WebAssembly/wasi-gfx@03c3e95493`
(matches the `Wacs.WASI.GFX/wit/deps.lock` pin in this
repo). Re-vendor in sync if either side updates.

## Build

```sh
cd Spec.Test/components/fixtures/wasi-gfx-rectangle
cargo build --release --target wasm32-unknown-unknown
wasm-tools component new \
    target/wasm32-unknown-unknown/release/wasi_gfx_rectangle.wasm \
    -o wasm/rectangle.component.wasm
```

The component artifact at `wasm/rectangle.component.wasm`
is what WACS runs. v0 does not commit the built `.wasm`
to git — built lazily on demand.

## Run under WACS

```sh
wacs run --wasip2 --wasi-gfx --windowed \
    Spec.Test/components/fixtures/wasi-gfx-rectangle/wasm/rectangle.component.wasm
```

Expected visual: a 640×480 window opens; a 100×100 red
rectangle outline draws on a gray background. Click the
window → rectangle turns green. Resize the window →
rectangle re-renders against the new dimensions. Close
the window to exit.

## Run under the reference (parity check)

```sh
cd <somewhere>
git clone https://github.com/wasi-gfx/wasi-gfx-runtime
cd wasi-gfx-runtime
cargo xtask run-demo --name rectangle_frame_buffer
```

The reference uses the upstream example (which includes
the `wasi:webgpu` world but doesn't actually call any
of its imports — so the rendered output is the same).
Visual diff should match WACS's output.

## v0 known limitations

- **Pollable handle space:** WACS.WASI.GFX mints
  pollables into its own table; Preview2's
  `wasi:io/poll.poll` looks up handles in Preview2's
  table. This guest WILL hit that mismatch at runtime
  on `wasi::io::poll::poll(&pollables)`. The phase-3
  follow-up plan in `Wacs.WASI.GFX/WitBindings.cs` (top-
  of-file comment) is to share Preview2's table via
  the existing `ResourceContext` surface.
- **Multi-device:** `frame-buffer.Device::new()` +
  `from-graphics-buffer` work because v0's static-method
  binding resolves "the single device in the table".
- **wasm-tools component new** may need an adapter
  (`-r wasi_snapshot_preview1.command-component.wasm`)
  if `cargo`'s default cdylib build leaves any wasi
  preview1 imports in the produced module. Check the
  `wasm-tools` docs version for current syntax.
