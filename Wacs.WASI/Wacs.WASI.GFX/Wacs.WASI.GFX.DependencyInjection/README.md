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

## What's in the box

`WasiGfxBundle` carries the configuration + backend.
`WasiPreview2GfxBundle` is the composite the transpiler's
`HostPackageResolver` direct-links against when both Preview2
and wasi-gfx are loaded.

Per-resource impl classes (`Context`, `AbstractBuffer`,
`Surface`, `Device`, `Buffer`) follow the SourceGen-resource
convention — parameterless ctor + `Create()`, with the
backend pulled from `WasiGfxAmbient` at construction time.
They live in this package and the resolver discovers them via
`TryFindResourceImpl`.

Both engines run wasi-gfx components end-to-end:

```sh
# Interpreter component path:
wacs run --wasi-gfx --windowed my.component.wasm

# Transpiler direct-link path:
wacs run --wasip2 --wasi-gfx --windowed my.component.wasm
```

`AddWasiGfx` + `AddWasiPreview2GfxBundle` register everything
via `Microsoft.Extensions.DependencyInjection`. The CLI
auto-wires both at startup via the
`WasiPreview2RuntimeScope.ReflectivelyAddWasiGfx` hook in
Preview2's DI scope.
