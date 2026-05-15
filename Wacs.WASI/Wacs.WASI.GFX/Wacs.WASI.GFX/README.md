# WACS.WASI.GFX

Host bindings for [wasi-gfx](https://github.com/WebAssembly/wasi-gfx) —
the WebAssembly System Interface proposal for graphics and GPU
compute (Phase 2). v0 ships three of the four wasi-gfx WIT packages —
`wasi:graphics-context@0.0.1`, `wasi:frame-buffer@0.0.1`,
`wasi:surface@0.0.1` — the CPU rendering path with windowing and
input. `wasi:webgpu@0.0.1` is a v1 target.

## Packages

| Package | Role |
|---|---|
| `WACS.WASI.GFX` | Core: `WasiGfxConfiguration`, `WasiGfxHost` (`IBindable`), `IBackend` SPI, vendored WIT + source-gen `[WitSource]` interfaces under `Wacs.WASI.GFX.{GraphicsContext, FrameBuffer, Surface}`. |
| `WACS.WASI.GFX.Silk` | Silk.NET/SDL backend. Owns the OS window, event pump, CPU pixel blit. Bundled with the CLI for `--wasi-gfx`. |
| `WACS.WASI.GFX.DependencyInjection` | DI bundle for the transpiler-direct-link path: `WasiGfxBundle`, `WasiPreview2GfxBundle` composite, `services.AddWasiGfx(...)` registration. |

## Threading model

Driving an OS window means owning the main thread on macOS
(AppKit hard requirement). v0 contract: the embedder runs wasm
on a worker thread and calls `host.Backend.RunMainLoop(ct)` on
the main thread to pump the OS event loop until `ct` fires.
`wacs run --windowed` does this automatically.

Surface events (resize / frame / pointer / key) reach the guest
through `wasi:io/poll.pollable` resources — the backend's
event pump calls `ManualResetPollable.Signal()` and the guest's
`pollable.block()` wakes. No changes to `WasmRuntime` or the
existing wasi-io infrastructure.

## Pinning

WIT files vendored verbatim from
`WebAssembly/wasi-gfx@03c3e95493` (the proposal's
HEAD as of v0). See [`wit/deps.lock`](wit/deps.lock) for the
exact commit and replay commands.

## Status

v0 is feature-complete on the CPU rendering path. Both the
interpreter component path (`--wasi-gfx`) and the transpiler
direct-link path (`--wasip2 --wasi-gfx`) render the parity
fixture end-to-end. Component-side parity is verified against
[`wasi-gfx/wasi-gfx-runtime`](https://github.com/wasi-gfx/wasi-gfx-runtime)'s
`rectangle_frame_buffer` example (plus a sibling `triangle`
fixture under `Spec.Test/components/fixtures/`).
