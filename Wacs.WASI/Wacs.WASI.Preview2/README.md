# Wacs.WASI.Preview2

WASI Preview 2 host implementations pinned at WASI 0.2.3. Component-
model based — every binding goes through `Wacs.ComponentModel`'s
canonical-ABI engine. End-to-end coverage on cli / clocks / filesystem
/ http / io / random / sockets.

## Contents

- **[Wacs.WASI.Preview2/](Wacs.WASI.Preview2/)** — host bindings + the WIT-driven source generator that emits `IXxx` host interfaces from vendored `wit/deps/*.wit`. Per-subsystem `*Bindings.cs` orchestrators (`RandomBindings`, `ClockBindings`, `SocketsBindings`, etc.) wire host implementations to a `WasmRuntime`.
- **[Wacs.WASI.Preview2.Test/](Wacs.WASI.Preview2.Test/)** — fixture-driven end-to-end tests; ~189 cases covering every bound subsystem plus the contract-validation linker.
- **[Wacs.WASI.Preview2.DependencyInjection/](Wacs.WASI.Preview2.DependencyInjection/)** — `Microsoft.Extensions.DependencyInjection` integration: register host implementations via `services.AddWasiPreview2(...)` instead of constructing each binder by hand.
