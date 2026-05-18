# WIT harness on WACS — approach explainer

How WACS satisfies the WebAssembly Component Model + WIT spec for
component **exports**, focused on the harness path that turns a
`.wasm` component + its `.wit` contract into a typed C# façade.

## What "satisfies the WIT spec" means here

The Component Model defines:

1. A **type system** (WIT) — records, variants, enums, flags,
   options, results, tuples, lists, strings, primitives,
   resources (own / borrow handles).
2. A **canonical ABI** — how those types lift / lower across the
   wasm boundary as i32/i64/f32/f64 slots and linear-memory
   layouts.
3. An **interface / world model** — exports / imports grouped by
   versioned interface, with resource methods and free functions
   on those interfaces.

The harness's job is to turn a guest's WIT contract into a typed
host surface. Every supported WIT shape must round-trip:

- **Lift** — read a typed value out of wasm linear memory at a
  canonical-ABI offset, hand it back to host C#.
- **Lower** — accept a typed C# value, write it into wasm linear
  memory or push it onto the wasm flat-param stack at canonical
  offsets.
- **cabi_post cleanup** — when a wasm-allocated buffer (string
  body, list element array) is handed up to the host, fire the
  guest's `cabi_post_*` to free it once we've copied.

## Architecture in three files

```
Wacs.ComponentModel.Harness.Lib/
├── CanonicalAbi.cs       — size + alignment + offset rules
├── WitTypeEmit.cs        — TypeBuilder emission for named WIT types
├── LiftEmit.cs           — IL for reading wasm memory → CLR
└── WorldHarnessEmit.cs   — wrapper IL + lower path + invoker plumbing
```

### `CanonicalAbi.cs`

Pure layout math. For any `CtValType`, returns `(size, align)`
per the spec's deterministic alignment rules. Records use
positional packing with per-field alignment padding; variants
use `disc + max(payload_align, disc_align)` framing; options /
results are 2-case variants; tuples are records-without-names.

All offset-using code paths (lift, lower, cabi_post walker)
read from this one module, so changing the layout rules
propagates everywhere automatically.

### `WitTypeEmit.cs`

Two emission passes over every named WIT type (world-level **and**
interface-level):

1. **Shells** — `DefineType` on each record/variant/resource so
   forward references resolve. Enums and flags emit eagerly as
   complete CLR types (no payloads, no forward refs).
2. **Bodies** — record fields + ctor + getters; variant cases
   as nested sealed subclasses; resource class with
   `_hostHandle / _rep / _dtor / _drop / _hostTable` fields,
   `IDisposable` Dispose, `(hostHandle, rep, dtor, drop, hostTable)`
   constructor, and `Handle` / `Rep` public getters.

The `TypeRegistry` keys all dictionaries by **structural type
reference** (`Dictionary<CtRecordType, TypeBuilder>`, etc.) not
by WIT name string. This makes two interfaces that both declare
a type called `error` coexist without collision — the parser
gives each declaration its own structural type object identity.

### `LiftEmit.cs`

Emits one `Lift_<TypeName>(MemoryInstance memory, int ptr)`
static method per named record/variant on the harness class.
`EmitLiftField` walks any structural type and either:

- Inlines a primitive read (`MemoryHelpers.ReadI32LE` etc.),
- Delegates to a registered `Lift_<TypeName>` static for
  records / variants, or
- Recurses into nested types (option, result, tuple, list,
  resource).

A parallel `EmitLiftFromBase` walker takes the memory + base
pointer as locals (rather than the fixed `arg.0 / arg.1`
contract the static lift methods use) so list elements with
composite types (option, tuple, nested list) can chain offsets
from any starting address.

### `WorldHarnessEmit.cs`

The wrapper IL — one C# method per export, doing:

1. **Lower** each user-facing arg through `EmitFlattenedArg` —
   primitives push directly; strings call `StringCoding.LowerUtf8`
   (alloc via `cabi_realloc`, write UTF-8, push `(ptr, len)`);
   lists alloc + write per element; records walk fields; resources
   extract `_handle`; etc.
2. **Call** the strongly-typed `Func<…>` invoker delegate
   bound to the wasm export name.
3. **Lift** the return per `EmitFlatLowered`'s type dispatch —
   inline primitive read for primitive returns, ret-area walk for
   aggregates, `LiftUtf8` for strings, `(ptr, count)` walk for
   lists, `newobj` for resource handles.
4. **`cabi_post_*`** if the return transitively contains strings
   or lists.

## Naming and namespacing

WIT identifies things at two levels: a **package** (`wasi:cli@0.2.0`)
and an **interface** within it (`run`). Real WIT freely re-uses
type names (`error`, `stream`, `bucket`, …) across interfaces;
the harness has to keep them apart.

The approach is **C# namespaces at the high level, nested classes
for resource-specific surface**:

```
WitHarnessSpike.Generated
├── {World}Harness                    — class with one method per export
├── {WorldType}                       — world-level types (record/variant/…)
└── {InterfaceSegment}                — one sub-namespace per interface
    ├── {InterfaceType}               — interface-declared types
    └── {ResourceType} : IDisposable  — resource as a class with Dispose
```

For example:

```
WitHarnessSpike.ResourceMethods.Generated
├── DemoHarness
│   ├── WacsResourceMethodsSpikeCounter_NewCounter(uint) -> Counter
│   ├── WacsResourceMethodsSpikeCounter_Counter_Increment(Counter) -> uint
│   └── WacsResourceMethodsSpikeCounter_Counter_Merge(uint, uint) -> uint   (static)
└── WacsResourceMethodsSpikeCounter
    └── Counter : IDisposable
        ├── int Handle { get; }    — host-side identity
        ├── int Rep    { get; }    — wasm-side handle
        └── void Dispose()
```

`borrow<R>` parameters and returns surface as
`Wacs.ComponentModel.Harness.Runtime.Borrowed<R>` — a
`readonly struct` with `int Rep` and no `Dispose`.

The interface-segment Pascal-cases the package's namespace +
path + name into one token (`wacs:resource-methods-spike/counter`
→ `WacsResourceMethodsSpikeCounter`). This is verbose but
collision-safe; users typically add a `using` alias on the
consuming side.

Resource methods land **flat on the harness** taking the resource
as first arg (instance methods) or no `self` (static methods).
The nicer `b.Read(len)` nested layout was designed but deferred —
it needs a back-ref pattern from resource class to harness, which
forces a multi-phase emission restructure. The wasm-side plumbing
is identical, so the surface can change without changing the
binary contract.

## How resources work

WIT resources are opaque handles owned by either the wasm guest
or the host runtime. Across the wasm boundary they're just i32
indices into a per-component-instance handle table.

WACS layers **two** handle spaces around that boundary, for
reasons explained below:

```
┌─────────────────────────────────────────────────────────────┐
│  host C#  ──  Resource.Handle = HOST handle (1, 2, 3, …)    │
│  HostHandleTable ──  hostHandle → rep                       │
├─────────────────────────────────────────────────────────────┤
│  harness lower  ──  Resource.Rep = WASM rep (= handle)      │
│  CanonResourceBinder + ResourceHandleTable (rep-as-handle)  │
├─────────────────────────────────────────────────────────────┤
│  wasm guest  ──  treats handle and rep as the same int      │
└─────────────────────────────────────────────────────────────┘
```

### Wasm-side: rep-as-handle (CanonResourceBinder)

`Wacs.Core.Runtime.CanonResourceBinder` walks the inner core
module's imports for the `("[export]<iface>", "[resource-*]<name>")`
shape and binds three adapters against a per-resource
`ResourceHandleTable`:

- `[resource-new]<name>(rep) -> handle` — register rep, return
  `rep` (1:1).
- `[resource-drop]<name>(handle)` — remove the slot.
- `[resource-rep]<name>(handle) -> rep` — return `handle`.

Both `HarnessLoader.Load` and `ComponentInstance.Instantiate`
call `BindImports` before instantiating the core module, then
`ResolveDtorTrampolines` after — the dtor isn't exported until
the inner module is wired. The drop adapter is split from the
dtor invocation so the two wasm-side calls stay top-level (a
re-entrant wasm-from-host-from-wasm dispatch trips WACS's frame
stack today).

Why rep-as-handle? wit-bindgen 0.41 Rust guests don't import
`[resource-rep]` when emitting their instance-method wrappers —
they stash `(handle == rep)` in static state and dereference it
directly. Returning fresh handles from `[resource-new]` would
break them. Other toolchains may differ; the binder is the
single place to swap behavior if a future codegen wants
separate handles inside the wasm.

### Host-side: independent handle space (HostHandleTable)

`Wacs.ComponentModel.Harness.Runtime.HostHandleTable` sits one
layer above and allocates user-visible handles independently of
rep: auto-increment counter (skipping the 0 sentinel) + LIFO
freelist for recycled slots. Each resource type gets its own
table, allocated inline in the harness ctor.

The emitted Resource class carries both quantities:

```csharp
public sealed class Bucket : IDisposable
{
    private int _hostHandle;        // host-side identity
    private readonly int _rep;      // wasm-side handle
    private readonly Action<int> _dtor;
    private readonly Action<int> _drop;
    private readonly HostHandleTable _hostTable;

    internal Bucket(int hostHandle, int rep, Action<int> dtor,
                    Action<int> drop, HostHandleTable hostTable)
    { _hostHandle = hostHandle; _rep = rep;
      _dtor = dtor; _drop = drop; _hostTable = hostTable; }

    public int Handle => _hostHandle;   // for dictionary keys, equality
    public int Rep    => _rep;          // for hand-rolled Borrowed<R>

    public void Dispose()
    {
        if (_hostHandle != 0)
        {
            _hostTable.DropOwn(_hostHandle);   // freelist
            _dtor(_rep);                        // run guest cleanup
            _drop(_rep);                        // free wasm slot
            _hostHandle = 0;                    // idempotent guard
        }
    }
}
```

- Lift `own<R>` return: invoker pushes rep → harness calls
  `hostTable.NewOwn(rep)` to mint a fresh host handle → `newobj
  Resource(hostHandle, rep, dtor, drop, hostTable)`.
- Lower `own<R>` arg: extract `Resource.Rep` (public getter) and
  push to wasm.
- `Dispose()` returns the host slot to the freelist, then runs
  the dtor and the canon `[resource-drop]` adapter as two
  top-level wasm calls.

Decoupling identity from rep means:

- The user sees stable, monotonic handles in `Resource.Handle`
  regardless of whether the wasm allocator reuses the same
  pointer across drop/new cycles.
- Two `new()` calls that happen to receive the same rep value
  still yield distinct host handles — the table never confuses
  them.

### Borrow vs own — type-distinct, not value-tagged

The companion type for `borrow<R>` in WIT is `Borrowed<TR>`, a
`readonly struct` that wraps the rep with no host-table entry:

```csharp
public readonly struct Borrowed<T> where T : class
{
    public readonly int Rep;
    public Borrowed(int rep) { Rep = rep; }
}
```

Crucially, `Borrowed<T>` is **not** `IDisposable`. User code
that took a borrow can't accidentally call `Dispose()` on it —
that would invoke the dtor + drop on a resource the host
doesn't own, which is a use-after-free on the lender.

- Lift `borrow<R>` return: `newobj Borrowed<TR>(rep)` — no
  table interaction, no allocation tracking.
- Lower `borrow<R>` arg: `ldfld Borrowed<TR>.Rep` directly off
  the struct on the stack.

To pass an owned `Resource<R>` as a borrow parameter, user code
constructs the struct explicitly: `new Borrowed<Bucket>(bucket.Rep)`.
Future ergonomic surface (an implicit converter or a `Borrow()`
helper on Resource) can land additively.

### What's still deferred

- **Call-scope borrow invalidation** — refusing to dereference
  a `Borrowed<T>` after the lending call returned. Needs an
  `ExecContext` hook the runtime doesn't expose yet; the
  type-level distinction in v2 is the safety floor.
- **Cross-instance handle transfer** — handing handles between
  composed component instances. Static composition via
  `wasm-tools compose` already works through the wasm boundary;
  dynamic host-mediated composition would build on the v2
  foundation (one `HostHandleTable` per (instance, resource)
  pair, plus borrow tracking across instance boundaries).

## What the harness can't yet do

Three named niches, all small and well-understood:

| Niche | Status | Why |
|---|---|---|
| Multi-result `func() -> (a: T, b: U)` | **Moot** | WIT spec dropped this in favor of `-> tuple<…>` / `-> record { … }`; `wit-bindgen` rejects the old syntax at parse |
| MAX_FLAT_PARAMS overflow (>16 flat slots) | **Diagnostic only** | Canonical ABI prescribes a single-i32 param area; harness emits a clear error pointing at the spec mechanism |
| Alt string encodings (UTF-16, Latin1+UTF-16) | **Awaiting toolchain** | `wit-bindgen` doesn't expose the `string-encoding` canon option; `StringCoding` helpers are isolated so a future slice can swap in additional codecs |

WASIp3 async (`future<T>` / `stream<T>`) is intentionally out of
scope — that's a separate WACS-runtime track.

## Coverage summary

| Axis | Coverage |
|---|---|
| WIT type-system kinds | 13 / 13 (100%) |
| Lift path | ~99% |
| Lower path | ~96% (intermediate-slot variant width-join + a few `list<aggregate>` element-writes deferred) |
| Export shapes | ~99% (multi-return moot) |
| Resource lifecycle (emit) | ~98% (own + borrow type-distinct; nested layout deferred) |
| Resource lifecycle (runtime) | ~85% (canon adapters bound; call-scope borrow invalidation deferred) |
| Canonical-ABI plumbing | ~85% (MAX_FLAT_PARAMS + alt encodings missing) |
| Async / WASIp3 | 0% (out of scope) |

**Overall harness-side: ~97%, 32 fixtures pass.**

## Fixture as proof — anatomy of a slice

Every capability ships with a Rust + WIT fixture under
`Spec.Test/components/fixtures/wit-harness-spike-*/`:

```
wit-harness-spike-result-params/
├── wit/world.wit                       — the WIT contract
├── src/lib.rs                          — Rust guest implementing the world
├── Cargo.toml                          — wit-bindgen 0.41
├── wasm/<name>.component.wasm          — compiled component (committed)
└── Generated.Validate/                 — host-side validator (C#)
    ├── *.csproj                        — runs HarnessEmitter.EmitInMemory
    └── Program.cs                      — opens component, calls exports, asserts
```

The validator loads the wit dir, asks `HarnessEmitter` to emit the
harness assembly in memory, instantiates it with `LoadFrom(bytes,
bindWasi)`, calls exports via reflection (since the emitted types
are dynamic), and asserts results match what the Rust guest
returned. Every slice's PR adds one or more such fixtures and
the full set runs in regression on every change.

## References

- WIT spec: https://github.com/WebAssembly/component-model/blob/main/design/mvp/WIT.md
- Canonical ABI: https://github.com/WebAssembly/component-model/blob/main/design/mvp/CanonicalABI.md
- wit-bindgen (Rust + others): https://github.com/bytecodealliance/wit-bindgen
- WACS repo: https://github.com/kelnishi/WACS
