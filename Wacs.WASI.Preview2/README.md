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
   [`../Wacs.ComponentModel.Bindgen.SourceGen/README.md`](../Wacs.ComponentModel.Bindgen.SourceGen/README.md).
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

Wire each WASI subsystem you want to expose. Constructor params are
nullable; pass `null` (or omit) to skip an interface.

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

## Validating bindings against the WIT contract

Optional but recommended: catch contract drift at link time before
the component runs. See the
[validation docs](../Wacs.ComponentModel/Validation/README.md) for
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
| `wasi:filesystem` | `Descriptor` + `HostFileInputStream` / `HostFileOutputStream` | `System.IO.File` / `Directory` |
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

## License

Apache-2.0
