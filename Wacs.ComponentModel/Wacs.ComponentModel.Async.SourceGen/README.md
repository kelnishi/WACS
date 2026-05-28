# WACS.ComponentModel.Async.SourceGen

Roslyn incremental source generator that backs the Component
Model **async** dispatcher in
[`WACS.ComponentModel`](https://www.nuget.org/packages/WACS.ComponentModel).
Three independent generators, each replacing what would
otherwise be a runtime-reflection scan — satisfying the WACS
AOT-safety rule (no `[RequiresDynamicCode]`, no late-bound
discovery).

Packaged as `analyzers/dotnet/cs` — no runtime DLL is shipped or
referenced; the generator runs at consumer build time only.

## What it produces

### 1. `[AsyncComponentHarness]` — typed async harness for a `.component.wasm`

The headline surface. Mark a partial class with
`[AsyncComponentHarness]` and per-export `[AsyncExport]` /
`[SyncExport]` partial methods; the generator emits the ctor +
each method body, wired through `ComponentInstance.InstantiateAot`
+ `InvokeCoreAsyncLift` so the consumer's code stays on the
AOT-safe primitive surface.

```csharp
using Wacs.ComponentModel.Async;

[AsyncComponentHarness]
public partial class MyComponent
{
    [AsyncExport("wasi:cli/run@0.3.0-draft/run")]
    public partial Task<Result<Unit, Unit>> RunAsync(CancellationToken ct);

    [SyncExport("local:demo/math#add")]
    public partial int Add(int a, int b);
}
```

Lift naming follows `wit-component`: `[async-lift]<qualified-export>`
for async-lifted exports, plain export name for sync. The
`AsyncExport` / `SyncExport` strings are opaque to the
generator — they pass through to the runtime lifter lookup.

For the canonical-ABI shapes the generator currently supports
(records, variants, lists, options, results, strings) and what's
still punted, see the memory note
[`project_async_sourcegen_surface`](https://github.com/kelnishi/WACS/blob/main/docs/) — or
just try it; the diagnostics on unsupported shapes name the
specific WIT type.

### 2. `[CanonAsync(...)]` — `CanonOpRegistry.BuildKnownNames`

Runs against `Wacs.ComponentModel` itself. Scans
`AsyncDispatcher` for methods decorated `[CanonAsync("...")]`
(e.g. `[async-lower]`, `subtask.drop`, `stream.new`) and emits
the partial `BuildKnownNames()` implementation supplying the
static set of known canon-async op names. The hand-written
registry declares `partial HashSet<string> BuildKnownNames();`
and this generator fills it in.

Replaces the prior reflection-on-first-access path so the
registry has zero runtime reflection.

### 3. `[ComponentLifter(...)]` — `[ModuleInitializer]` registration

Runs in **every consumer assembly** that ships a
`[ComponentLifter("wit-id")]`-marked static method. Emits a
`[ModuleInitializer]`-decorated registration method that calls
`ComponentLifterRegistry.Register` once per match at assembly
load.

Lets per-host packages (`WACS.WASI.Preview3`, future
`WACS.WASI.Preview3.Http`, etc.) self-register their lifters
without a central, manually-maintained registration list.

## How it relates to the other WACS packages

| Package | Role |
|---|---|
| `WACS.ComponentModel.Async.SourceGen` | **this** — build-time analyzer; not referenced at runtime |
| [`WACS.ComponentModel`](https://www.nuget.org/packages/WACS.ComponentModel) | Hosts the `AsyncDispatcher`, `CanonOpRegistry`, `ComponentLifterRegistry`, and the `[AsyncComponentHarness]` / `[AsyncExport]` / `[CanonAsync]` / `[ComponentLifter]` attributes the generator keys off |
| [`WACS.WASI.Preview3`](https://www.nuget.org/packages/WACS.WASI.Preview3) | Per-host package that consumes the generator — its bindings classes register canon-async lifters via `[ComponentLifter]` |
| [`WACS.ComponentModel.Bindgen.SourceGen`](https://www.nuget.org/packages/WACS.ComponentModel.Bindgen.SourceGen) | Sibling source-gen — emits **host-side** interfaces from WIT files (the I-side that this generator's `[AsyncComponentHarness]` is the C-side of) |

## Wiring it into a project

Reference as an analyzer (mirrors how `Bindgen.SourceGen` is
wired):

```xml
<ItemGroup>
  <PackageReference Include="WACS.ComponentModel.Async.SourceGen"
                    Version="*"
                    PrivateAssets="all" />
  <PackageReference Include="WACS.ComponentModel" Version="*" />
</ItemGroup>
```

The first generator (`AsyncComponentHarness`) needs
`Wacs.ComponentModel.Async.AsyncComponentHarnessAttribute` to be
resolvable in the compilation — pulling
`WACS.ComponentModel` satisfies all three triggers' attribute
references.

## AOT story

Three generators replace what would have been three reflection
sites:

1. Per-component harness construction: was
   `ComponentInstance.Invoke` (reflective bridge) → now
   generated partial method bodies on
   `InstantiateAot` / `InvokeCoreAsyncLift`.
2. Canon-op registry: was reflective scan of `AsyncDispatcher`
   on first access → now generated partial method.
3. Lifter registry: was central manually-maintained list → now
   `[ModuleInitializer]` per consumer assembly.

Each is verified by the WACS AOT acceptance gate
(`AotAcceptanceTests`) — publishing the bench package with
`PublishAot=true` must produce no new IL trim warnings.

## License

Apache-2.0
