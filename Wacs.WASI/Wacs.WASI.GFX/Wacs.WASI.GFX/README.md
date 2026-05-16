# WACS.WASI.GFX

Host bindings for [wasi-gfx](https://github.com/WebAssembly/wasi-gfx) —
the WebAssembly System Interface proposal for graphics and GPU
compute (Phase 2). Ships all four wasi-gfx WIT packages —
`wasi:graphics-context@0.0.1`, `wasi:surface@0.0.1`,
`wasi:frame-buffer@0.0.1`, `wasi:webgpu@0.0.1` — through a single
Silk.NET/SDL + wgpu-native backend that drives the full family
against one SDL window.

## Packages

| Package | Role |
|---|---|
| `WACS.WASI.GFX` | Core: `WasiGfxConfiguration`, `WasiGfxHost` (`IBindable`), `IBackend` SPI, vendored WIT + source-gen `[WitSource]` interfaces under `Wacs.WASI.GFX.{GraphicsContext, FrameBuffer, Surface}`. |
| `WACS.WASI.GFX.Webgpu` | webgpu contract: source-gen `[WitSource]` interfaces under `Wacs.WASI.GFX.Webgpu`, canonical-ABI hand-written bindings, `IGpuBackend` SPI. |
| `WACS.WASI.GFX.Silk` | Silk.NET/SDL + wgpu-native backend. Owns the OS window, SDL event pump, CPU pixel blit path, and the wgpu adapter/device/swap-chain bridge. Bundled with the CLI for `--wasi-gfx`. |
| `WACS.WASI.GFX.DependencyInjection` | DI bundles for the transpiler-direct-link path: `WasiGfxBundle`, `WasiWebgpuBundle`, `WasiPreview2GfxBundle` composite, `services.AddWasiGfx(...)` / `.AddWasiWebgpu(...)` / `.AddWasiPreview2GfxBundle()` registration. |

## Threading model

Driving an OS window means owning the main thread on macOS
(AppKit hard requirement). Contract: the embedder runs wasm on a
worker thread and calls `host.Backend.RunMainLoop(ct)` on the
main thread to pump the OS event loop until `ct` fires.
`wacs run --windowed` does this automatically.

Surface events (resize / frame / pointer / key) reach the guest
through `wasi:io/poll.pollable` resources — the backend's event
pump calls `ManualResetPollable.Signal()` and the guest's
`pollable.block()` wakes. The wgpu adapter / device / queue /
pipeline / `queue.submit` / `surface.configure` /
`get-current-texture` / `present` calls are internally
thread-safe and run from the worker; the lone exception is
`SDL_Metal_CreateView` (NSView creation, macOS), which the
GPU connect path dispatches through `MainThreadDispatcher`
automatically.

## Pinning

WIT files vendored verbatim from
[`WebAssembly/wasi-gfx`](https://github.com/WebAssembly/wasi-gfx).
See [`wit/deps.lock`](wit/deps.lock) for the exact commit and
replay commands.

## Status

Feature-complete across CPU and GPU paths. Both the interpreter
component path (`--wasi-gfx`) and the transpiler direct-link path
(`--wasip2 --wasi-gfx`) render the parity fixtures end-to-end.

Parity fixtures under `Spec.Test/components/fixtures/`:

| Fixture | Path | Shape |
|---|---|---|
| `wasi-gfx-rectangle` / `wasi-gfx-triangle` | `wasi:frame-buffer` | CPU pixel blit (RGBA8) |
| `wasi-webgpu-hello-compute` | `wasi:webgpu` | compute pipeline + map-async readback |
| `wasi-webgpu-hello-render` | `wasi:webgpu` | render pipeline + copy-texture-to-buffer (offscreen) |
| `wasi-webgpu-game-of-life` | `wasi:webgpu` | bind-group ping-pong + multi-pass compute |
| `wasi-webgpu-game-of-life-windowed` | full family | surface + graphics-context + webgpu swap-chain (windowed demo) |

See [`docs/WASI_GFX_USAGE.md`](../../../docs/WASI_GFX_USAGE.md)
for the full usage guide and
[`docs/COMPONENT_CHAINING.md`](../../../docs/COMPONENT_CHAINING.md#wasi-gfx-chaining)
for the multi-host chaining details.
