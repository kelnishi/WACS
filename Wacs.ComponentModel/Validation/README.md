# Wacs.ComponentModel.Validation

Runtime contract validation: compare a `WasmRuntime`'s host-binding
manifest against a WIT spec, catching contract drift at link time
before the component runs.

## When to use this

- **At app startup** — wire your bindings, then `linker.Validate(...)`
  before `ComponentInstance.Instantiate(...)`. Catches missing
  imports as a deterministic exception instead of a wasm trap deep
  inside execution.
- **In CI** — assert that the bindings shipping with your host
  match the WIT files vendored in your repo, so a spec bump or a
  binding regression fails the build instead of leaking to prod.
- **When integrating an external host impl** — validate that a
  third-party `IBindable` covers the WIT contract you expect
  before you trust it with a component.

## Three modes

| `ValidationLevel` | Behavior | Cost |
|---|---|---|
| `Off` (default) | No inspection; `Validate` returns clean | zero |
| `Warnings` | Collects every issue into a `ValidationReport`; consumer chooses what to do | one pass over imports |
| `Strict` | Collects then `throw new ValidationException(report)` on the first issue found | same as Warnings + early-exit |

`Off` matches the pre-validation behavior — the runtime traps at
invoke time if a host method is missing. `Strict` is what you'd
turn on in production startup paths to fail-fast.

## API surface

### `Linker`

Wraps a `WasmRuntime` and tracks bindings registered through it.

```csharp
public sealed class Linker
{
    public Linker(WasmRuntime runtime,
        ValidationLevel level = ValidationLevel.Off);

    public WasmRuntime Runtime { get; }
    public ValidationLevel Level { get; }
    public IReadOnlyCollection<(string Module, string Entity)> Bindings { get; }

    public void Bind(IBindable bindable);
    public ValidationReport Validate(WitContract contract);
}
```

`Bind(IBindable)` calls the bindable's `BindToRuntime` then snapshots
which `(module, entity)` keys it added (diffed against the runtime's
prior state). The accumulated set is `linker.Bindings`.

`Validate(WitContract)` walks the contract's expected imports,
matches each against the runtime's recorded
`FunctionType` (via `WasmRuntime.TryGetBoundHostFunctionType`), and
emits issues for mismatches.

### `WitContract`

Flat list of `ImportEntry` (module, entity, expected param/return
arity). Build it from any of six sources:

```csharp
// 1. From WIT text (single document, ad-hoc)
var contract = WitContract.FromText(@"
    package wasi:demo@0.2.3;
    interface env {
        get-args: func() -> list<string>;
    }
");

// 2. From a vendored WIT directory on disk (recurses, resolves
//    cross-package `use` chains via WitResolver.Resolve)
var contract = WitContract.FromDirectory("wit");

// 3. From a shipped DLL's <EmbeddedResource> WIT files —
//    no source tree needed. The host `Wacs.WASI.Preview2`
//    embeds its WIT under "wit/..." resource names; consumers
//    can validate against the contract the package was built
//    from without shipping the .wit source alongside.
var contract = WitContract.FromAssembly(
    typeof(CliBindings).Assembly);

// 4. From in-memory pre-parsed packages
var packages = WitLoader.LoadDirectoryTree("wit");
WitResolver.Resolve(packages);
var contract = WitContract.FromPackages(packages);

// 5. From a specific WIT *world*'s imports (the right entry
//    point when validating against a component-model world
//    rather than every interface the WIT tree declares —
//    skips guest-export interfaces, recursively expands
//    `include other-world;`).
var packages = WitContract.LoadAssemblyPackages(
    typeof(CliBindings).Assembly);
var contract = WitContract.FromWorld(packages,
    "wasi:cli/imports@0.2.3");

// 6. From the bindings themselves — reflects [WitSource]
//    attributes off generated I* interface types
var contract = WitContract.FromBindingTypes(
    typeof(IRandom), typeof(IExit), typeof(IFields));
```

To embed WIT in your own bindings package, add to the csproj:

```xml
<ItemGroup>
  <EmbeddedResource Include="wit\**\*.wit"
                    LogicalName="wit/%(RecursiveDir)%(Filename)%(Extension)" />
</ItemGroup>
```

The `wit/` prefix is the convention `WitContract.FromAssembly`
filters on by default; pass a different `resourcePrefix` if your
project uses a different scheme. `WitContract.ReadEmbeddedWit`
exposes the raw `(name, text)` pairs for inspection.

### `ValidationReport` / `ValidationIssue`

```csharp
public sealed class ValidationReport
{
    public IReadOnlyList<ValidationIssue> Issues { get; }
    public bool IsClean { get; }
}

public sealed class ValidationIssue
{
    public ValidationIssueKind Kind { get; }
    public string Module { get; }
    public string Entity { get; }
    public string Detail { get; }
}

public enum ValidationIssueKind
{
    MissingBinding,        // contract entry has no binding
    ExtraBinding,          // binding registered, contract has no entry
    ArityMismatch,         // param count differs from canon-lowered expected
    ParamTypeMismatch,     // param wire type differs (reserved; not yet emitted)
    ReturnTypeMismatch,    // return arity differs
}
```

`[resource-drop]X` bindings are filtered as bookkeeping — they're
implicit per the canon ABI and don't appear in WIT, so the
validator skips them when computing `ExtraBinding`.

## Worked examples

### Strict validation at app startup

```csharp
using Wacs.ComponentModel.Validation;
using Wacs.Core.Runtime;

var runtime = new WasmRuntime();
var linker = new Linker(runtime, ValidationLevel.Strict);

linker.Bind(new IoBindings(resources));
linker.Bind(new HttpTypes(resources));
linker.Bind(new OutgoingHandlerBindings(resources, handler));

try
{
    linker.Validate(WitContract.FromDirectory("wit"));
}
catch (ValidationException ex)
{
    Console.Error.WriteLine(ex.Message);
    foreach (var issue in ex.Report.Issues)
        Console.Error.WriteLine("  " + issue);
    return 1;
}

// All bindings match the WIT contract — safe to instantiate.
var ci = ComponentInstance.Instantiate(componentBytes,
    rt => { /* already bound */ });
```

### Self-validation via embedded `[WitSource]`

When you've shipped the bindings but don't want to ship the `.wit`
files alongside, build the contract from the generated interfaces
themselves:

```csharp
var contract = WitContract.FromBindingTypes(
    typeof(IRandom), typeof(IInsecure), typeof(IInsecureSeed));
linker.Validate(contract);
```

The source generator embeds `[WitSource]` attributes carrying the
WIT-text fragments on every generated interface and method;
`FromBindingTypes` reflects over them to reconstruct the contract
without needing the original `.wit` files at runtime.

### Warnings mode for tolerant linking

When you want to surface drift but not block:

```csharp
var linker = new Linker(runtime, ValidationLevel.Warnings);
linker.Bind(new HttpTypes(resources));

var report = linker.Validate(WitContract.FromDirectory("wit"));
if (!report.IsClean)
{
    foreach (var issue in report.Issues)
        logger.Warning("WIT drift: {issue}", issue);
}
// Continue regardless.
```

### Audit mode (collect everything)

`Warnings` doesn't short-circuit — pass the report to
`logger.LogIssue` per-entry, dump to telemetry, etc. `Strict` also
collects all issues before throwing, and the
`ValidationException.Report` carries the full set.

## Wire-shape estimation

Validation compares each contract import's expected arity against
the runtime's recorded `FunctionType.ParameterTypes.Arity` and
`ResultType.Arity`. The contract's expected arity is derived per
the canonical ABI flat-form rules:

| WIT type | Wire slots |
|---|---|
| `bool`, `s8`..`s64`, `u8`..`u64`, `f32`/`f64`, `char`, primitive enum / flags | 1 |
| `string`, `list<T>` | 2 (ptr, len) |
| `tuple<a, b, ...>` | sum of element slots |
| `option<T>` | 1 (disc) + slots(T) |
| `result<T, E>` | 1 (disc) + max(slots(T), slots(E)) |
| `own<R>`, `borrow<R>`, resource ref | 1 (handle) |

Lowering follows the canonical-ABI thresholds **exactly**:

- `MAX_FLAT_PARAMS = 16` — if the sum of param slots exceeds 16,
  all params lower to a single `i32` pointer (indirect params).
- `MAX_FLAT_RESULTS = 1` — if the result lowers to more than 1
  slot, the host receives a trailing `i32` retArea pointer in
  the param list and the function returns void.

The thresholds are applied independently. `get-random-bytes:
func(len: u64) -> list<u8>` checks as `2 params (len, retArea)` +
`0 returns` (list<u8> = 2 slots, hoisted), matching the binding's
`Action<ExecContext, long, int>` shape. `set-method: func(method:
method) -> result<_, _>` checks as `3 params (handle, disc, ptr,
len)` + `1 result` (result<_, _> = 1 disc slot, returned flat) —
so a binding that uses retArea for a bare `result<_, _>` is real
canonical-ABI drift the validator surfaces.

The validation still does not check per-slot ValType (e.g. an
`i32`-vs-`i64` swap on a primitive would only surface if the
slot count differs). Exact ValType match is a follow-up; the
current arity + shape-class depth is enough to surface every
common contract-drift scenario (missing import, signature
change, off-by-one, retArea-vs-flat mismatch).

## What validation does NOT catch

- **Default-impl depth**: A binding can register and pass validation
  while the host method is a no-op stub. Validation checks the
  contract surface, not implementation fidelity. See the
  WASI.Preview2 [README](../../Wacs.WASI.Preview2/README.md) for
  the stubs vs real-impl breakdown.
- **Per-slot ValType**: If a binding swaps `i32` for `i64` on a
  primitive param (would change ABI), validation currently catches
  this only via arity (when `i64` collapses to `i32+i32` or
  similar). Exact ValType match is reserved for a follow-up.
- **Resource lifecycle correctness**: `[resource-drop]X` binding
  presence is filtered out as bookkeeping. If the host's drop
  doesn't actually call `IDisposable.Dispose`, validation can't
  see that.

## Import-side scoping in `FromAssembly` / `FromDirectory`

When the package tree declares at least one world,
`BuildFromPackages` filters interfaces by role:

- **Imported by some world** → included (host binds it).
- **Only ever exported across all worlds** → excluded (guest
  implements it). Examples: `wasi:cli/run`,
  `wasi:http/incoming-handler`.
- **Not referenced by any world** (orphan in the package) →
  included conservatively, since there's no signal to
  exclude it.

Worlds are walked transitively through `include other-world;`
directives. If the package tree has no worlds at all (e.g.
`FromText` with a single inline-interface document),
filtering is bypassed and every interface is included.

For tighter scoping, use `WitContract.FromWorld(...)` to
validate against a specific world's import surface only.

## License

Apache-2.0
