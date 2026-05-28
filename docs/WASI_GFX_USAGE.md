# WASI-GFX on WACS — usage guide

How to wire a wasi-gfx-aware component onto a WACS host. Three audiences:

- **CLI users** running stock `wasm32-wasip2` components — read [Quick start](#quick-start) + [CLI invocation](#cli-invocation).
- **Library embedders** adding wasi-gfx to a `WasmRuntime` they own — read [Programmatic embedding](#programmatic-embedding).
- **Backend authors / contributors** — see [`Wacs.WASI.GFX/README.md`](../Wacs.WASI/Wacs.WASI.GFX/Wacs.WASI.GFX/README.md) and the per-package READMEs.

The `wasi-gfx` family covers four WIT packages:

| WIT package | Purpose | WACS contract |
|---|---|---|
| `wasi:graphics-context@0.0.1` | Abstract context + buffer bridging CPU ↔ GPU | `WACS.WASI.GFX` |
| `wasi:surface@0.0.1` | Windowed surface + input events (resize / pointer / key) | `WACS.WASI.GFX` |
| `wasi:frame-buffer@0.0.1` | RGBA8 CPU pixel blit path | `WACS.WASI.GFX` |
| `wasi:webgpu@0.0.1` | GPU compute + render pipelines, swap-chain present | `WACS.WASI.GFX.Webgpu` |

One Silk.NET/SDL + wgpu-native backend (`WACS.WASI.GFX.Silk`) drives all four against a single SDL window.

---

## Backend matrix

| Backend | Packages | Verified | Notes |
|---|---|---|---|
| [`WACS.WASI.GFX.Silk`](../Wacs.WASI/Wacs.WASI.GFX/Wacs.WASI.GFX.Silk/) | all four wasi-gfx WITs | macOS arm64 (Metal-backed wgpu surface) | Bundled with the CLI behind `--wasi-gfx` |

The Silk backend bundles **both** the CPU host (graphics-context / surface / frame-buffer) **and** the GPU host (webgpu) — one `--wasi-gfx` flag enables the entire family.

---

## Quick start

```sh
# Headless: webgpu compute / render fixtures (no window, asserts via trap)
dotnet test Wacs.WASI/Wacs.WASI.GFX/Wacs.WASI.GFX.Silk.Test

# Windowed: open a 640×640 SDL window with GPU-rendered Conway's Game of Life
dotnet run --project Wacs.Console/Wacs.Console -c Release -- \
  run --wasi-gfx --windowed --call start \
  Spec.Test/components/fixtures/wasi-webgpu-game-of-life-windowed/wasm/game-of-life-windowed.component.wasm
```

The windowed demo exercises every wasi-gfx WIT package end-to-end.

---

## CLI invocation

### `--wasi-gfx` (shorthand for the Silk backend)

Adds `WACS.WASI.GFX + .DependencyInjection + .Silk + .Webgpu` to the host-package list. The Silk backend ships with the CLI; no `--bind` needed:

```sh
wacs run my.component.wasm --wasip2 --wasi-gfx --windowed --call start
```

`--wasi-gfx` brings up **both** the CPU host (wasi:graphics-context / wasi:surface / wasi:frame-buffer) **and** the GPU host (wasi:webgpu) against the same SDL window. The bound assembly is `Wacs.WASI.GFX.Silk` (a parameterless `IBindable`); reflection-driven discovery wires the implementations into the runtime.

### `--windowed` (required when the guest opens a surface)

```sh
wacs run my.wasm --wasip2 --wasi-gfx --windowed --call start
```

Reserves the calling (main) thread for the SDL event pump and runs the guest on a worker thread. **Required** on macOS — AppKit hard-requires window creation + Metal-view creation on the main thread; without `--windowed` the guest's `wasi:surface.constructor` will fail or the wgpu surface won't appear on screen.

If the guest doesn't open a window (e.g. headless render-to-texture), `--windowed` is harmless but emits a "no effect" warning at startup.

### `--call <export>` (entrypoint override)

The CLI's `run` verb defaults to the WASI-cli `_start` entrypoint. Components whose WIT world exports `start: func()` (no leading underscore — the convention used by the wasi-gfx parity fixtures) need:

```sh
wacs run my.component.wasm --wasi-gfx --windowed --call start
```

---

## Programmatic embedding

### Interpreter path

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.GFX.Silk;
using Wacs.WASI.GFX.Silk; // SilkGfxBackend (CPU) + SilkGpuBackend (GPU)

var runtime = new WasmRuntime();

// CPU side (wasi:graphics-context / wasi:surface / wasi:frame-buffer)
var backend = new SilkGfxBackend();
var host = runtime.UseWasiGfx(b => b
    .WithBackend(backend)
    .WithSharedResources(resources));

// GPU side (wasi:webgpu) — pairs with the CPU backend through the
// graphics-context bridge.
var gpuBackend = new SilkGpuBackend();
var webgpuHost = runtime.UseWasiWebgpu(b => b
    .WithBackend(gpuBackend)
    .WithSharedResources(resources)
    .WithAbstractBufferResolver(handle =>
        host.AbstractBuffers.Get(handle) as IAbstractBuffer)
    .WithGraphicsContextResolver(handle =>
        // unwrap whatever IContext shape lives in host.Contexts
        host.Contexts.Get(handle) as Wacs.WASI.GFX.GraphicsContext.IContext));
```

The `AbstractBufferResolver` + `GraphicsContextResolver` closures bridge the two sibling hosts — when a guest calls `gpu-texture.from-graphics-buffer(buf)` the webgpu side reaches into the wasi-gfx-side `AbstractBuffers` table.

The CLI's `WasiGfxSilkBindable` wires both sides automatically; embedders can use it directly:

```csharp
new Wacs.WASI.GFX.Silk.WasiGfxSilkBindable().BindToRuntime(runtime);
```

### Transpiler / DI path

```csharp
services
    .AddWasiPreview2()
    .AddWasiGfx(b => b.WithBackend(new SilkGfxBackend()))
    .AddWasiPreview2GfxBundle();   // composite for the single hostBundle slot
```

The composite is auto-discovered by `HostPackageResolver` when the component imports both `wasi:cli/*` and `wasi:graphics-context/*`. The webgpu host registers analogously via `AddWasiWebgpu`.

### Threading

```csharp
// Main thread: SDL event pump (drives surface events into pollables)
backend.RunMainLoop(cts.Token);

// Worker thread: wasm guest
Task.Run(() => ci.Invoke("start"));
```

`--windowed` in the CLI is exactly this — the SDL backend's `RunMainLoop` blocks the main thread pumping events; the wasm guest runs on a `Task.Run` worker. The wgpu-native API is thread-safe for almost all operations; the one exception is `SDL_Metal_CreateView` (the wgpu surface's backing Metal layer), which the GPU connect path dispatches through `MainThreadDispatcher` automatically.

---

## Parity fixtures

Three headless fixtures + one windowed demo live under `Spec.Test/components/fixtures/`:

| Fixture | Path exercised | Run via |
|---|---|---|
| `wasi-webgpu-hello-compute` | compute pipeline + map-async readback (add-one kernel) | Silk.Test suite |
| `wasi-webgpu-hello-render` | render pipeline + render pass + copy-texture-to-buffer (triangle to offscreen texture) | Silk.Test suite |
| `wasi-webgpu-game-of-life` | bind-group ping-pong + multi-pass compute (Conway blinker period verification) | Silk.Test suite |
| `wasi-webgpu-game-of-life-windowed` | full swap-chain: window + surface + graphics-context → GPU device → compute + render → present | CLI `wacs run --wasi-gfx --windowed --call start` |

The headless three run on any machine with wgpu-native (no display required). The windowed demo needs a visible display + macOS for the swap-chain path (see [Platform notes](#platform-notes)).

---

## Threading model

Driving an OS window means owning the main thread on macOS (AppKit's hard requirement). The contract:

1. Embedder calls `runtime.UseWasiGfx(...)` from the main thread.
2. Embedder kicks the wasm entrypoint onto a background thread.
3. Embedder calls `backend.RunMainLoop(ct)` on the main thread; this blocks pumping SDL events until `ct` fires.
4. Wasm-side surface events go through `ManualResetPollable` — backend's event-pump signals; guest's `pollable.block()` wakes.
5. `wacs run --windowed` does steps 2 + 3 automatically.

wgpu-native operations (adapter / device / buffer / pipeline / queue.submit / surface configure / GetCurrentTexture / Present) are internally thread-safe and run from the worker. The exception is `SDL_Metal_CreateView` (NSView creation) which is dispatched through `MainThreadDispatcher` automatically by `SilkGpuDevice.ConnectGraphicsContext`.

---

## Platform notes

- **macOS arm64** — fully verified. wgpu surface uses the Metal-layer path; SDL's `SDL_Metal_CreateView` provides the layer; `SilkSurface.DropSdlRenderer` releases the window's content-view first so the wgpu Metal layer can claim it.
- **Windows / Linux** — the swap-chain path throws `PlatformNotSupportedException` from `SilkGpuDevice.ConnectGraphicsContext`. Headless render-to-texture works (no surface-create dependency); only `wasi:surface` + `connect-graphics-context` are gated. Wiring `SurfaceDescriptorFromWindowsHwnd` / `SurfaceDescriptorFromXlibWindow` / `SurfaceDescriptorFromWaylandSurface` would mirror the macOS path; the wgpu-native and Silk APIs are present, just not yet wired.
- **CI without a GPU** — `Wacs.WASI.GFX.Silk.Test`'s fixture tests soft-skip when wgpu-native fails to initialize. Set `WACS_REQUIRE_WGPU=1` to force-fail in that case.

---

## Worked examples

### Headless render: triangle to an offscreen texture

Pattern from [`wasi-webgpu-hello-render`](../Spec.Test/components/fixtures/wasi-webgpu-hello-render/src/lib.rs):

1. `gpu.request-adapter` → `adapter.request-device`.
2. `device.create-shader-module` with a WGSL vertex+fragment pair.
3. `device.create-render-pipeline(layout: auto)` with one color target (Rgba8unorm).
4. `device.create-texture(usage: RENDER_ATTACHMENT | COPY_SRC)` for the offscreen target; `texture.create-view(None)` for the render-pass attachment.
5. `device.create-buffer(usage: MAP_READ | COPY_DST)` sized to `width × bytes_per_row` for readback.
6. Command encoder: `begin-render-pass` with one color attachment (load=Clear, store=Store), set-pipeline + draw(3) + end; `copy-texture-to-buffer` for readback.
7. `queue.submit` → `buffer.map-async(Read)` → `get-mapped-range-get-with-copy` → verify.

### Windowed render: GPU compute + present (no swap-chain in the test loop)

Pattern from [`wasi-webgpu-game-of-life-windowed`](../Spec.Test/components/fixtures/wasi-webgpu-game-of-life-windowed/src/lib.rs):

1. `surface.constructor(width, height)` opens the SDL window.
2. `graphics-context.context.constructor()` and `surface.connect-graphics-context(ctx)`.
3. `gpu.request-adapter` / `adapter.request-device` / `device.queue`.
4. `device.connect-graphics-context(ctx)` — this is the swap-chain bridge. The host's `SilkGpuDevice.ConnectGraphicsContext` drills to the underlying SDL window, creates a Metal layer (on the main thread), builds a wgpu surface descriptor, and configures the surface against the device.
5. Build pipelines + bind groups (compute + render).
6. Frame loop:
   - `wasi:io/poll.poll([frame, key, ...])` blocks until an event lands.
   - On frame: `ctx.get-current-buffer()` → `gpu-texture.from-graphics-buffer(buf)` returns the current swap-chain texture. `texture.create-view(None)` for the render pass.
   - Compute pass → render pass → `queue.submit` → `ctx.present()` (routes through `wgpuSurfacePresent`).
   - On key-down(Escape): return.

The compute kernel + render shader share the surface texture only for display; the storage buffers backing the simulation are pure wgpu objects.

---

## Architectural notes

- Each backend package carries `[assembly: WasiHostPackage]`, so `runtime.AutoDiscoverHostPackages()` finds whichever ones the host process has loaded.
- The wasi-gfx + wasi-webgpu hosts share one `ResourceContext` (the Preview2 resource table) so pollables minted by `surface.subscribe-frame()` are reachable from `wasi:io/poll.poll`.
- The `WasiPreview2GfxBundle` composite (in `WACS.WASI.GFX.DependencyInjection`) is auto-generated by the `CompositeBundleGenerator` source-gen from the `[WacsCompositeBundle]` attribute — no hand-maintained property list.
- Phase 1's `[WitPackageMapping]` source-gen attribute lets a single WIT package map to multiple CLR namespaces during the source-gen pass; wasi-gfx uses this to keep `Wacs.WASI.GFX.{GraphicsContext, FrameBuffer, Surface}` separate while sharing one `wit/` directory.

---

## See also

- [`Wacs.WASI.GFX/README.md`](../Wacs.WASI/Wacs.WASI.GFX/Wacs.WASI.GFX/README.md) — package map
- [`Wacs.WASI.GFX.Silk/README.md`](../Wacs.WASI/Wacs.WASI.GFX/Wacs.WASI.GFX.Silk/README.md) — Silk backend internals
- [`Wacs.WASI.GFX.Webgpu/README.md`](../Wacs.WASI/Wacs.WASI.GFX/Wacs.WASI.GFX.Webgpu/README.md) — webgpu contract assembly
- [`docs/COMPONENT_CHAINING.md`](COMPONENT_CHAINING.md#wasi-gfx-chaining) — multi-host chaining details
