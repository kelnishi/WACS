# WACS.WASI.GFX.DependencyInjection

`Microsoft.Extensions.DependencyInjection` extensions for
[WACS.WASI.GFX](https://www.nuget.org/packages/WACS.WASI.GFX).
Symmetric with `WACS.WASI.NN.DependencyInjection` and
`WACS.WASI.Preview2.DependencyInjection`.

## Usage

```csharp
using Wacs.WASI.Preview2.DependencyInjection;
using Wacs.WASI.GFX.DependencyInjection;
using Wacs.WASI.GFX.Silk;

services
    .AddWasiPreview2()
    .AddWasiGfx(b => b.WithBackend(new SilkGfxBackend()))
    .AddWasiPreview2GfxBundle();   // composite for the
                                   // single hostBundle slot
```

The composite `WasiPreview2GfxBundle` forwards property
lookups to either the Preview2 sub-bundle or the wasi-gfx
sub-bundle. Components importing both `wasi:cli/*` and any
wasi-gfx package resolve through one bundle slot.

## v0 surface

`WasiGfxBundle` exposes the configuration + backend only —
sufficient for embedders that want to introspect or replace
the backend at runtime. The per-resource concrete classes
that `HostPackageResolver` would direct-link against
(mirroring `WASI.NN`'s `Tensor`/`Graph`/etc.) are a v1
follow-up because wasi-gfx's resources all use
`constructor(...)` rather than free-function factories, so
the binding shape differs from `WASI.NN.IGraphFuncs`.

The interpreter path goes through `WACS.WASI.GFX`'s
`WitBindings.cs` directly via
`runtime.UseWasiGfx(b => b.WithBackend(...))` — no DI required.
