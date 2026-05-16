# wasi-webgpu-game-of-life-windowed

Standalone windowed demo: Conway's Game of Life on the GPU,
rendered to a wasi:surface window via the wasi:webgpu swap-
chain path. Not for the test suite — it's a visual demo.

## Run

From the repo root:

```bash
dotnet run --project Wacs.Console/Wacs.Console -c Release -- \
  run --wasi-gfx --windowed --call start \
  Spec.Test/components/fixtures/wasi-webgpu-game-of-life-windowed/wasm/game-of-life-windowed.component.wasm
```

(`--call start` overrides the CLI's default `_start` entrypoint —
the WIT exports `start: func()`, no leading underscore.)

A 640×640 window opens with an R-pentomino seed. The compute
shader steps the simulation once per frame; the fragment shader
draws the grid. Hit **Escape** (or close the window) to quit.

`--wasi-gfx` wires the Silk SDL backend, which brings up BOTH
the CPU host (wasi:graphics-context / wasi:surface / wasi:frame-
buffer) AND the GPU host (wasi:webgpu) against the same SDL
window. `--windowed` reserves the main thread for the SDL event
pump (macOS AppKit requires this).

## What this exercises

- `wasi:surface` window + frame/key-down/resize events
- `wasi:graphics-context.context` ↔ `device.connect-graphics-context`
  swap-chain bridge (SDL → Metal layer → wgpu surface, configured
  per frame)
- `[static]gpu-texture.from-graphics-buffer` lift on each frame
- Compute pipeline + render pipeline sharing one shader module
- Ping-pong storage buffers across compute passes
- Render-pass with a single full-screen-triangle draw sampling
  the just-computed storage buffer

## Build (only needed if you modify the guest)

```bash
cd Spec.Test/components/fixtures/wasi-webgpu-game-of-life-windowed
cargo build --release --target wasm32-unknown-unknown
wasm-tools component new \
  target/wasm32-unknown-unknown/release/wasi_webgpu_game_of_life_windowed.wasm \
  -o wasm/game-of-life-windowed.component.wasm
```

## WIT pins

`wit/deps/` is vendored from `WebAssembly/wasi-gfx@03c3e95493`,
plus `wasi:io@0.2.0` to match the version the host binds. The
upstream `wasi:surface` WIT references `wasi:io/poll@0.2.8`;
the vendored copy here is pinned to `0.2.0` so it matches the
io package used by `wasi:webgpu` (the host doesn't currently
bind multiple io versions in the same instance).
