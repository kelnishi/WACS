# WACS.WASI.GFX.Silk

Silk.NET/SDL backend for [WACS.WASI.GFX](https://www.nuget.org/packages/WACS.WASI.GFX).
Implements `IBackend` for the v0 wasi-gfx packages
(graphics-context + surface + frame-buffer) on top of SDL2 via
[Silk.NET](https://github.com/dotnet/Silk.NET).

## Status

v0 feature-complete on the CPU rendering path. Opens a real
SDL window, blits RGBA8 pixels per `frame-buffer.buffer.set`,
dispatches OS events (resize / pointer / key) into `wasi:io/
poll.pollable`s on the surface. Works under both the
interpreter component path and the transpiler direct-link
path.

## Usage

```sh
# Interpreter component path:
wacs run --wasi-gfx --windowed my.component.wasm

# Transpiler direct-link path (canonical wasip2 workflow):
wacs run --wasip2 --wasi-gfx --windowed my.component.wasm
```

`--wasi-gfx` loads `Wacs.WASI.GFX.Silk` and registers it as the
gfx backend; `--windowed` reserves the calling (main) thread
for the SDL event loop and runs the guest on a worker.

Or programmatically:

```csharp
using var host = runtime.UseWasiGfx(b =>
    b.WithBackend(new SilkGfxBackend()));
host.Backend!.RunMainLoop(ct);   // call from main thread
```
