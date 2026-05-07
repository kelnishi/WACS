# Wacs.WASI.Preview1

WASI Preview 1 (`wasi_snapshot_preview1`) host bindings: filesystem,
clocks, random, sockets, proc, poll. Validated against
`wasi-testsuite` (43 / 72 fixtures pass; remaining gaps tracked in the
test project's `skip.json`).

## Contents

- **[Wacs.WASI.Preview1/](Wacs.WASI.Preview1/)** — current Preview 1 host. NuGet `WACS.WASI.Preview1`. The full 47-function syscall surface bound through `WasiConfiguration` + `Wasi.BindToRuntime(runtime)`.
- **[Wacs.WASI.Preview1.Test/](Wacs.WASI.Preview1.Test/)** — wasi-testsuite harness driver; runs every fixture from the spec submodule that this implementation claims to support.
- **[Wacs.WASIp1/](Wacs.WASIp1/)** — DEPRECATED metapackage. NuGet `WACS.WASIp1` — predates the namespace split; transitively pulls `WACS.WASI.Preview1`. Kept so existing consumers don't break; new code should reference `WACS.WASI.Preview1` directly.
