# WACS.WASI.Preview3.DependencyInjection

`Microsoft.Extensions.DependencyInjection` extensions for
`WACS.WASI.Preview3`. Mirrors `WACS.WASI.Preview2.DependencyInjection`'s
shape.

## Usage

```csharp
services.AddWacsWasiPreview3(opts =>
{
    opts.Stdout = new StreamBackedSink(Console.OpenStandardOutput());
});
```

## v0 scope

Default service registration for the host-interface impls
shipped in `WACS.WASI.Preview3` 0.1.0:

- `IStdin` → `StreamBackedStdin` (over
  `Console.OpenStandardInput()`)
- `IStdout` → `StreamBackedSink` (over
  `Console.OpenStandardOutput()`)
- `IStderr` → `StreamBackedSink` (over
  `Console.OpenStandardError()`)

End-to-end runtime binding is the deferred piece — pairs with
fixture availability per the WASIp3 plan's Phase 4 v0
closeout (see `docs/wasip3-phase-3-closeout.md` for what the
canon-async substrate underneath delivers).
