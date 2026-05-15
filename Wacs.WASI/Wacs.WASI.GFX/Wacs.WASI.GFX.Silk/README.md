# WACS.WASI.GFX.Silk

Silk.NET/SDL backend for [WACS.WASI.GFX](https://www.nuget.org/packages/WACS.WASI.GFX).
Implements `IBackend` for the v0 wasi-gfx packages
(graphics-context + surface + frame-buffer) on top of SDL2 via
[Silk.NET](https://github.com/dotnet/Silk.NET).

## Status

v0 scaffolding stub. The full SDL event pump, window
management, and CPU pixel-blit implementation lands in a
follow-up milestone. The CLI `--wasi-gfx` flag and the package
identity are already wired so downstream consumers can pin
against this package today.

## Usage (when complete)

```sh
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
