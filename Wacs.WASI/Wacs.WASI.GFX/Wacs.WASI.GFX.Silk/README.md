# WACS.WASI.GFX.Silk

Silk.NET/SDL + wgpu-native backend for
[WACS.WASI.GFX](https://www.nuget.org/packages/WACS.WASI.GFX).
A single backend drives all four wasi-gfx WIT packages
(`wasi:graphics-context`, `wasi:surface`, `wasi:frame-buffer`,
`wasi:webgpu`) against one SDL window, mixing
[Silk.NET](https://github.com/dotnet/Silk.NET) (SDL2 +
WebGPU.NET) and [wgpu-native](https://github.com/gfx-rs/wgpu-native).

## Status

Feature-complete across CPU and GPU paths. Verified through
both the interpreter component path and the transpiler
direct-link path against the parity fixtures shipped in
`Spec.Test/components/fixtures/` (CPU `wasi-gfx-rectangle` /
`-triangle`; GPU `wasi-webgpu-hello-compute` /
`-hello-render` / `-game-of-life`; full swap-chain
`wasi-webgpu-game-of-life-windowed`).

## Paths

### CPU path (graphics-context + surface + frame-buffer)

- Opens an SDL window, dispatches OS events (resize / frame /
  pointer / key) into `wasi:io/poll.pollable`s on the surface
  via `ManualResetPollable.Signal()`.
- `frame-buffer.buffer.set(RGBA8 bytes)` → `SDL_UpdateTexture`
  → `SDL_RenderCopy` → `SDL_RenderPresent`.
- `SilkContext.GetCurrentBuffer()` returns a per-frame
  `SilkAbstractBuffer` (a pooled `byte[]` sized to
  `width × height × 4`) that the guest fills and `Present()`
  blits.

### GPU path (webgpu + swap-chain bridge)

- `SilkGpuBackend` wraps wgpu-native through `Silk.NET.WebGPU`.
  All of the headless surface (adapter / device / buffer /
  pipeline / queue / compute pass / render pass /
  copy-texture-to-buffer / map-async readback) is covered.
- `SilkGpuDevice.ConnectGraphicsContext(ctx)` is the
  swap-chain bridge: it reaches into the wasi-gfx-side
  `SilkSurface`, drops the SDL renderer's hold on the window
  (so the wgpu Metal layer can claim the `CAMetalLayer`),
  dispatches `SDL_Metal_CreateView` + `MetalGetLayer` through
  `MainThreadDispatcher` (AppKit requires NSView creation on
  the main thread), and configures the wgpu surface against
  the device.
- `context.get-current-buffer()` returns the swap-chain
  texture; `texture.from-graphics-buffer(buf)` resolves it
  back to a wgpu `GPUTexture` for the render pass; `present()`
  calls `wgpuSurfacePresent`.

## Usage

```sh
# Interpreter component path:
wacs run --wasi-gfx --windowed --call start my.component.wasm

# Transpiler direct-link path (canonical wasip2 workflow):
wacs run --wasip2 --wasi-gfx --windowed --call start my.component.wasm
```

`--wasi-gfx` loads `Wacs.WASI.GFX.Silk` and registers it for
both the CPU host and the GPU host. `--windowed` reserves the
calling (main) thread for the SDL event loop and runs the guest
on a worker. `--call start` selects the wasi-gfx fixtures'
exported `start: func()` entrypoint (the cargo-component
convention, distinct from the WASI-cli `_start` the CLI
defaults to).

Or programmatically:

```csharp
using var host = runtime.UseWasiGfx(b =>
    b.WithBackend(new SilkGfxBackend()));
host.Backend!.RunMainLoop(ct);   // call from main thread
```

See [`docs/WASI_GFX_USAGE.md`](../../../docs/WASI_GFX_USAGE.md)
for programmatic-embedding details (interpreter + transpiler
paths), the
[`Wacs.WASI.GFX.Webgpu` README](../Wacs.WASI.GFX.Webgpu/README.md)
for the webgpu contract assembly, and
[`docs/COMPONENT_CHAINING.md`](../../../docs/COMPONENT_CHAINING.md#wasi-gfx-chaining)
for multi-host chaining.

## Threading model

Driving an OS window means owning the main thread on macOS
(AppKit hard requirement). Contract: embedder runs wasm on a
worker thread and calls `host.Backend.RunMainLoop(ct)` on the
main thread to pump the SDL event loop until `ct` fires.
`wacs run --windowed` does this automatically.

wgpu-native adapter / device / queue / pipeline / `queue.submit`
/ `surface.configure` / `get-current-texture` / `present` are
internally thread-safe and run from the worker. The exceptions
that route through `MainThreadDispatcher` are SDL window
creation (`SilkSurface`'s ctor) and `SDL_Metal_CreateView`
(`SilkGpuDevice.ConnectGraphicsContext`).

## Platform support

| Platform | CPU path | GPU headless | GPU swap-chain |
|---|---|---|---|
| macOS arm64 (Metal) | ✅ | ✅ | ✅ |
| Windows / Linux | ✅ | ✅ | ❌ throws `PlatformNotSupportedException` |

Headless GPU works everywhere wgpu-native works; only the
SDL→wgpu swap-chain bridge is gated to macOS today. Wiring
`SurfaceDescriptorFromWindowsHwnd` /
`SurfaceDescriptorFromXlibWindow` /
`SurfaceDescriptorFromWaylandSurface` mirrors the macOS path;
the wgpu-native and Silk APIs are present, just not yet wired.
