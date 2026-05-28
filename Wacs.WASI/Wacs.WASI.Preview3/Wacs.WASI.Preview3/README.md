# WACS.WASI.Preview3

WASI Preview 3 host bindings for WACS, pinned to
`wasi-0.3.0-rc-2026-03-15`.

Sibling of `WACS.WASI.Preview2`. Where Preview 2 uses
synchronous host imports (`io/streams.output-stream.write` etc.),
Preview 3 uses the **Component Model async ABI** — host imports
take `stream<T>` / `future<T>` handles and return `future<...>`,
with the host's async work driven by the canon-async
dispatcher (`WACS.ComponentModel.Async.AsyncDispatcher`).

## v0 status

**Vertical slice** per the WASIp3 plan: `wasi:cli/run` (async
entry point), `wasi:cli/{stdin,stdout,stderr}` (stream-based
stdio), and the `stream<u8>` ↔ `System.IO.Stream` bridge.

- Package skeleton + interfaces.
- Default `Console.Open{StandardInput,StandardOutput,StandardError}`
  backings.
- Bridge code from `StreamBuffer<byte>` (canon-async data plane)
  to host `Stream`.
- Test harness placeholder.

**Not yet wired**: end-to-end binding from a wit-component-
emitted `.component.wasm` to these interfaces. The
canon-async binder + shim recognizer
(`WACS.ComponentModel` 0.8.13+) cover the read side; the host
binding layer here registers delegates that the canon-async
binder will call. Validation against a real fixture awaits
`wasi-0.3.0-rc-2026-03-15` toolchain stabilization in
`bytecodealliance/wasm-tools` + `wit-bindgen`.

## How it composes

```csharp
var runtime = new WasmRuntime();
var host = runtime.UseWasiPreview3(b =>
{
    b.Stdout = new ConsoleStdio(Console.Out);
});
// Once a wit-component-emitted .component.wasm is available,
// the component's wasi:cli/stdout import resolves to
// host.Stdout.WriteViaStream(...).
```

## Layout

Mirrors `WACS.WASI.Preview2`:

```
Wacs.WASI.Preview3/
  Cli/
    CliRun.cs       — host-side hook for invoking exported run().
    CliStdio.cs     — IStdin / IStdout / IStderr interfaces +
                      Console-backed defaults.
  Io/
    StreamBridge.cs — StreamBuffer<byte> ↔ System.IO.Stream
                      adapter.
  WasiPreview3Host.cs        — composite IBindable.
  WasiPreview3HostBuilder.cs — fluent config.
  wit/deps/wasi-cli-0.3.0-rc-2026-03-15/
    package.wit
```

## Plan reference

See `docs/wasip3-phase-3-closeout.md` for what the canon-async
substrate underneath this package delivers, and the WASIp3 plan
for the Phase 4 + Phase 5 scope.
