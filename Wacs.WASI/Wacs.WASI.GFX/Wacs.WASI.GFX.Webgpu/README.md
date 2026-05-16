# WACS.WASI.GFX.Webgpu

WASI-GFX `wasi:webgpu@0.0.1` host bindings for WACS. The fourth
wasi-gfx WIT package, mirroring the browser WebGPU spec verbatim.

This is the **contract assembly** — `[WitSource]`-tagged
interfaces (generated from `wit/webgpu.wit`) plus the
`WitBindings` dispatcher that ties them into the WACS runtime.
The actual GPU backend lives in `WACS.WASI.GFX.Silk` and wraps
`Silk.NET.WebGPU` /
[wgpu-native](https://github.com/gfx-rs/wgpu-native).

## Surface covered

- `gpu.request-adapter` / `adapter.request-device` / `device.queue`.
- Buffers: `device.create-buffer`, `buffer.{map-async, get-mapped-range-get-with-copy, get-mapped-range-set, unmap, destroy}`.
- Textures + views: `device.create-texture`, `texture.{create-view, destroy}`.
- Shaders + pipelines: `device.create-shader-module`,
  `device.{create-bind-group-layout, create-pipeline-layout, create-bind-group}`,
  `device.{create-compute-pipeline, create-render-pipeline}`.
- Command encoding: `device.create-command-encoder`,
  `command-encoder.{begin-compute-pass, begin-render-pass, copy-buffer-to-buffer, copy-texture-to-buffer, finish}`,
  `compute-pass-encoder` + `render-pass-encoder` operations,
  `queue.{submit, write-buffer, on-submitted-work-done}`.
- Swap-chain bridge: `device.connect-graphics-context(ctx)` —
  the wgpu-side hook that fuses an OS surface (held by
  `wasi:surface`) to a wgpu device for `get-current-buffer` +
  `present` (driven by `wasi:graphics-context.context`).
- Async work: `gpu-future` (`request-device`, `map-async`,
  `on-submitted-work-done`) signals through
  `ManualResetPollable` so the guest's
  `wasi:io/poll.poll(...)` wakes on completion.

## Status

Feature-complete for the parity fixtures shipped in this repo.
Verified end-to-end through both the interpreter and transpiler
paths against `Spec.Test/components/fixtures/`:

- `wasi-webgpu-hello-compute` — compute pipeline + map-async readback.
- `wasi-webgpu-hello-render` — render pipeline + copy-texture-to-buffer.
- `wasi-webgpu-game-of-life` — bind-group ping-pong + multi-pass compute.
- `wasi-webgpu-game-of-life-windowed` — full swap-chain through `surface` + `graphics-context` + `webgpu`.

See [`docs/WASI_GFX_USAGE.md`](../../../docs/WASI_GFX_USAGE.md)
for the usage guide and the
[`Wacs.WASI.GFX.Silk` README](../Wacs.WASI.GFX.Silk/README.md)
for backend internals (wgpu-native binding, swap-chain bridge,
threading model).
