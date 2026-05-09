# WACS.WASI.Preview2

WASI Preview 2 (component-model) host implementations for the WACS
WebAssembly interpreter. Implements the canonical-ABI surface for
`wasi:cli`, `wasi:clocks`, `wasi:filesystem`, `wasi:http`, `wasi:io`,
`wasi:random`, and `wasi:sockets` — all of WASI 0.2.3.

## Architecture

Three layers:

1. **Vendored WIT files** at `wit/` — the WASI 0.2.3 specs, sourced
   from upstream `wasi-cli`. Drives both code generation and
   contract validation.
2. **Generated host interfaces** — a Roslyn source generator
   (`Wacs.ComponentModel.Bindgen.SourceGen`) reads the vendored WIT
   at compile time and emits one `public interface IXxx` per WIT
   resource or free-function group, with `[WitSource]` attributes
   carrying the source WIT text. See
   [`../../../Wacs.ComponentModel/Wacs.ComponentModel.Bindgen.SourceGen/README.md`](../../../Wacs.ComponentModel/Wacs.ComponentModel.Bindgen.SourceGen/README.md).
3. **`*Bindings.cs` orchestrators** — per-subsystem `IBindable`
   classes that wire host implementations of those interfaces to a
   `WasmRuntime` via `runtime.BindHostFunction<TDelegate>`. One
   class per WIT package, partial-class-split per resource family.

The bindings layer lifts/lowers between the canonical-ABI wire form
and the generated interface contract. Host methods return
`Result<T, ErrorCode>` (no throw-on-error idiom) — the bindings
encode both Ok and Err sides faithfully into the canon-ABI retArea.

## Installation

```bash
dotnet add package WACS.WASI.Preview2
```

## Usage

### Recommended: composite host + extension method

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2;

var runtime = new WasmRuntime();
runtime.UseWasiPreview2(b =>
    b.WithEnvironment(/* IEnvironment */)
     .EnableSockets()       // off by default
     .EnableHttp());         // off by default
```

The `runtime.UseWasiPreview2(...)` extension constructs a
`WasiPreview2Host : IBindable` that wires all sub-bindings off a
shared `ResourceContext` in one call. Default posture matches
Wasmtime: random / clocks / cli stdio / FS preopens always wired;
sockets and HTTP require explicit opt-in.

### Per-subsystem (verbose)

For embedders who need to override individual subsystems or wire a
strict subset, the hand-managed shape is still supported:

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.Cli;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;
using Wacs.WASI.Preview2.Random;
using Wacs.WASI.Preview2.Sockets;
using Wacs.WASI.Preview2.Http;
using Wacs.ComponentModel.Runtime;

var runtime = new WasmRuntime();
var resources = new ResourceContext();

// Stdio + environment + exit. Default Environment pulls from
// System.Environment; override for sandboxed args/env/cwd.
new CliBindings(resources,
    environment: new Environment(),
    exit: new ExitHandler(),
    stdin: new Stdin(),
    stdout: new Stdout(),
    stderr: new Stderr())
    .BindToRuntime(runtime);

// Clocks
new ClockBindings(resources,
    monotonic: new MonotonicClock(),
    wall: new WallClock(),
    timezone: new Timezone())
    .BindToRuntime(runtime);

// I/O streams + poll + error
new IoBindings(resources, poll: new PollSource()).BindToRuntime(runtime);
new StreamBindings(resources).BindToRuntime(runtime);

// Random
new RandomBindings(
    random: new Random.Random(),
    insecureRandom: new InsecureRandom(),
    insecureSeed: new InsecureSeed())
    .BindToRuntime(runtime);

// Filesystem (preopens optional)
new FilesystemBindings(resources).BindToRuntime(runtime);

// Sockets (network stub by default; subclass for real I/O)
new SocketsBindings(resources,
    instanceNetwork: new InstanceNetworkSource(),
    tcpCreate: new TcpCreateSocket(),
    udpCreate: new UdpCreateSocket(),
    ipNameLookup: new IpNameLookup())
    .BindToRuntime(runtime);

// HTTP types (always wire when using outgoing-handler)
new HttpTypes(resources).BindToRuntime(runtime);
new OutgoingHandlerBindings(resources, new OutgoingHandlerSource())
    .BindToRuntime(runtime);
```

## Dependency-injection integration

For `Microsoft.Extensions.DependencyInjection` consumers (ASP.NET,
generic-host worker services, etc.), the
`WACS.WASI.Preview2.DependencyInjection` companion package
registers every default impl + bindings + a pre-wired `Linker`
in one call:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Wacs.WASI.Preview2.DependencyInjection;

var services = new ServiceCollection();
services.AddWasiPreview2();   // every subsystem default registered

// Selective override — DI's normal mechanism. Defaults use TryAdd,
// so any earlier registration wins:
services.AddSingleton<IOutgoingHandler>(_ =>
    new HttpClientOutgoingHandler(new HttpClient { Timeout = ... }));
services.AddSingleton<IEnvironment>(_ => new SandboxedEnvironment(...));

using var sp = services.BuildServiceProvider();
using var scope = sp.CreateScope();

// Resolve a fully pre-bound Linker — every WASI subsystem already
// wired into the runtime, ready for component instantiation.
var linker = scope.ServiceProvider.GetRequiredService<Linker>();
var runtime = linker.Runtime;
```

`AddWasiPreview2(opts => opts.InstanceLifetime = ServiceLifetime.Scoped)`
is the default — `WasmRuntime` / `ResourceContext` / bindings /
`Linker` all resolve once per scope (fits ASP.NET request-scoped
wasm execution). Pass `Transient` for per-call construction or
`Singleton` for single-component apps.

### One-shot run scope: `WasiPreview2RuntimeScope`

For embedders running a single component against a single
runtime — the typical CLI / tool / one-shot host shape — the DI
package also ships `WasiPreview2RuntimeScope`, a disposable
that bundles the scope lifecycle into one constructor call:

```csharp
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.DependencyInjection;

var runtime = new WasmRuntime();
using var wasi = new WasiPreview2RuntimeScope(
    runtime,
    preopens: new[] { ("./models", "/models") });

// wasi.Bundle      → the resolved bundle (composite when
//                     Wacs.WASI.NN.DependencyInjection is on the
//                     load path; base WasiPreview2Bundle otherwise)
// wasi.Resources   → the WasiPreview2Resources for ctor arg 3
// wasi.Runtime     → runtime, with every *Bindings.BindToRuntime
//                     already fired
```

The scope:

1. Registers the external `runtime` as a singleton in DI before
   `AddWasiPreview2`'s `TryAdd<WasmRuntime>` fires, so the Linker
   binds against THIS runtime (not a fresh one DI would otherwise
   build).
2. Registers a `Preopens(IEnumerable<(host, guest)>)` impl as
   `IPreopens` when `preopens` is non-empty, ahead of
   `AddWasiPreview2`'s `TryAddSingleton<IPreopens>` default.
3. Auto-detects `Wacs.WASI.NN.DependencyInjection` and registers
   the composite `WasiPreview2NNBundle` when available — required
   because the transpiler emits its direct-link IL against
   whichever bundle type was visible at transpile time, and a
   mismatch between transpile-time and run-time bundle types
   trips an `InvalidCastException` at the first import call.
4. Eagerly resolves the `Linker` so the runtime has every
   `wasi:*` binding registered by the time the ctor returns.

`Dispose` releases the underlying `IServiceScope` and
`ServiceProvider`. The wasip2 `wacs run` path uses this; programmatic
embedders should prefer it over hand-rolling the DI scope unless they
need the `IServiceCollection` for additional registrations (which the
optional `configure` callback covers).

## Validating bindings against the WIT contract

Optional but recommended: catch contract drift at link time before
the component runs. See the
[validation docs](../../../Wacs.ComponentModel/Wacs.ComponentModel/Validation/README.md) for
details.

```csharp
using Wacs.ComponentModel.Validation;

var linker = new Linker(runtime, ValidationLevel.Strict);
linker.Bind(new CliBindings(resources, /* … */));
linker.Bind(new HttpTypes(resources));
linker.Bind(new OutgoingHandlerBindings(resources, handler));

// The WIT files this package was built against are embedded in
// the assembly itself — no need to ship them alongside.
var contract = WitContract.FromAssembly(
    typeof(CliBindings).Assembly);
linker.Validate(contract);   // throws ValidationException on mismatch
```

## Implementation depth

Every WASI 0.2.3 subsystem ships with a real .NET-backed default
implementation. The base classes default to "stub" semantics
(empty/no-op) so they don't trap if a host wires them naively;
the production-named subclass per subsystem wires through to the
real BCL primitive.

| Subsystem | Real-impl class | Backing |
|---|---|---|
| `wasi:cli` (env, exit, stdio) | `Environment` / `ExitHandler` / `Stdin` / `Stdout` / `Stderr` | `System.Environment` / `Console` |
| `wasi:clocks/monotonic` | `MonotonicClock` | `Stopwatch` (high-res) + `Task.Delay` for subscribe-* |
| `wasi:clocks/wall-clock` | `WallClock` | `DateTimeOffset.UtcNow` |
| `wasi:clocks/timezone` | `Timezone` | `TimeZoneInfo` |
| `wasi:filesystem` | `Descriptor` + `HostFileInputStream` / `HostFileOutputStream` + `Preopens` | `System.IO.File` / `Directory` |
| `wasi:random` | `Random` / `InsecureRandom` / `InsecureSeed` | `RandomNumberGenerator` (CSPRNG) + `System.Random` |
| `wasi:io/streams` | Through filesystem / cli wrappers | Result-encoded `System.IO.Stream` |
| `wasi:io/poll` | `PollSource` + `ManualResetPollable` / `TimerPollable` | `ManualResetEventSlim` + `Task.WhenAny` |
| `wasi:sockets/tcp` | `SystemTcpSocket` + `SystemTcpCreateSocket` | `System.Net.Sockets.Socket` (Stream) |
| `wasi:sockets/udp` | `SystemUdpSocket` + datagram-stream wrappers | `System.Net.Sockets.Socket` (Dgram) |
| `wasi:sockets/ip-name-lookup` | `IpNameLookup` + `DnsResolveAddressStream` | `System.Net.Dns.GetHostAddresses` |
| `wasi:http/outgoing-handler` | `HttpClientOutgoingHandler` | `System.Net.Http.HttpClient` |

Constructors take optional impl arguments — default to the real-
backed class, pass `null` to skip wiring an interface entirely.

Hosts that need different behavior (sandbox restrictions, custom
DNS, fakes for testing) subclass the relevant base type and
override the methods they want to replace.

`Preopens` accepts a list of `(hostPath, guestPath)` mount pairs
through either of two ctors:

```csharp
new Preopens(("./models", "/models"),
             ("./assets", "/assets"))
// or
new Preopens(IEnumerable<(string, string)> mounts)
```

Each pair becomes a `Descriptor` wrapping the host path; the guest
sees the list through `wasi:filesystem/preopens.get-directories`.
Wiring this through the DI scope's `IPreopens` registration (or
the `WasiPreview2RuntimeScope` `preopens:` parameter) lets the
transpiler's direct-link emit lift the
`list<tuple<own<descriptor>, string>>` return shape inline — no
delegate dispatch on the host call.

## License

Apache-2.0
