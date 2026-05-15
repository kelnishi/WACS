# wasi-gfx v1 plan

v0 (`WACS.WASI.GFX 0.1.0`) shipped the CPU rendering path of the
wasi-gfx proposal: contract + Silk.NET/SDL backend + DI bundle, both
the interpreter component path and the transpiler direct-link path
working end-to-end against `wasi-gfx/wasi-gfx-runtime`'s parity
fixtures. PR #159 / branch `wasi-gfx-v0`.

v0 was harder than it should have been. The wasi-gfx v0 post-mortem
identified roughly **half the v0 effort went to building cross-package
machinery that didn't exist** — every prior host family (Preview2,
wasi-nn) is WIT-self-contained at the resource level, so wasi-gfx was
the first consumer to need things like `list<borrow<R>>` lifts,
cross-package CLR type identity, and composite-bundle auto-discovery.
Each missing piece manifested as a silent failure that took hours of
resolver-level tracing to find.

v1 is structured to make the **next** host family (whether that's
this family's webgpu, or a future `wasi-audio`, or anything else) a
scaffold-and-implement exercise rather than an architecture-fixing
exercise. Phase 1 lands the architectural improvements first so the
rest of v1 builds on cleaner foundations.

---

## Phase 1 — Make adding a new WASI host family straightforward

Nine architectural fixes, ordered for landing impact-first. Each
item lists the concrete file(s) touched, the acceptance criterion,
and the v1 work it unblocks. None breaks existing public API; the
new mechanisms are additive and the old hardcoded fallbacks remain
during a transition window.

### Phase 1 progress

| Item | Status | Commit |
|---|---|---|
| 1a — diagnostics | shipped | `e4da5926` |
| 1b — attribute-driven bundles | shipped | `8dead299` |
| 1c — DI-sibling auto-discovery | shipped | `60d23bd4` |
| 1d — source-gen pkg-mapping | deferred | source-gen refactor; manual `WitHostPackageNamespaceMap` works fine today |
| 1e — canonical-ABI shape coverage | shipped | `7db96d34` |
| 1f — first-class static-method IL | shipped | `c7877948` |
| 1g — scoped backend factory | deferred | most invasive item per the plan; deferred to a focused branch |
| 1h — auto-generated composites | deferred | only 2 composites exist; source-gen ROI is negative until 4+ |
| 1i — multi-version wasi:io | shipped | `ffaf6a1f` |

Six of nine items shipped on this branch. The three deferred items
are all source-gen-heavy refactors whose benefit is felt only when
a new sibling family lands — 1b + 1c already removed the
hardcoded edits per new family, so the remaining items are perf /
ergonomic polish that can land in a focused later branch.

### 1a. Diagnostics — loud-fail unresolved direct-link bindings

**Where:** `Wacs.Transpiler.Lib/AOT/Component/ComponentMainHost.cs`
(the `IImports` stub), `Wacs.Transpiler.Lib/AOT/Component/DirectLinkedImportEmit.cs`
(emit gates), `Wacs.Transpiler.Lib/AOT/Component/HostPackageResolver.cs`.

The single highest-leverage v1 change. v0 burned ~2 hours debugging
the `IPollable[]` canonical-ABI gap because the lenient `IImports`
stub silently returned `null` instead of logging "served default
for `wasi:io/poll@0.2.8.poll`, direct-link emit rejected the
binding."

Add:
- One-line `Console.Error.WriteLine` per unique (module, entity)
  the first time the lenient stub serves a default, gated on a
  new `WACS_TRANSPILER_DEBUG` env var (off by default in
  production, on by default in `dotnet test`).
- An `[InternalLog]`-style trace inside `DirectLinkedImportEmit`'s
  gate returns: when a binding is rejected by a `return false`,
  log the gate name + the reason (which CLR type / wasm signature
  mismatched).
- A first-class `--trace-imports` CLI flag on `wacs run` that
  enables both above and dumps the resolver's binding table at
  startup.

**Acceptance:** the v0 `poll → IPollable[]` regression would have
been caught at first run, not after instrumentation. Add a
regression test: a fixture that imports a known-unsupported WIT
shape, run with `--trace-imports`, assert the stderr output names
the entity and the rejecting gate.

**Unblocks:** every future v1 phase. Debugging-time cost on the
next family drops from "hours of plumbing instrumentation" to
"read the stderr."

### 1b. Drop hardcoded family lists — attribute-driven bundle discovery

**Where:** `Wacs.Transpiler.Lib/AOT/Component/HostPackageResolver.cs`
(`FindWasiPreview2Bundle`), `Wacs.WASI.Preview2.DependencyInjection/WasiPreview2RuntimeScope.cs`
(`ResolveBundle`).

Today both files have a hardcoded cascade:

```csharp
// FindWasiPreview2Bundle
const string gfxCompositeName = "Wacs.WASI.GFX.DependencyInjection.WasiPreview2GfxBundle";
const string nnCompositeName  = "Wacs.WASI.NN.DependencyInjection.WasiPreview2NNBundle";
// ResolveBundle
var gfxComposite = ...; if (gfxComposite != null) return gfxComposite;
var nnComposite  = ...; if (nnComposite  != null) return nnComposite;
```

Three edits in two files per new family. Replace with:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class WacsCompositeBundleAttribute : Attribute
{
    public string Family { get; init; }
    public int Priority { get; init; } = 0;
}

// In WasiPreview2GfxBundle.cs:
[WacsCompositeBundle(Family = "wasi-gfx", Priority = 10)]
public sealed class WasiPreview2GfxBundle { ... }
```

`FindWasiPreview2Bundle` walks AppDomain for types with the
attribute, sorts by Priority desc, picks the highest-loaded one.
`ResolveBundle` does the same against the DI container.

**Acceptance:** delete the two hardcoded cascade blocks; add a
unit test that registers two attributed composites in a test
assembly, asserts the higher-priority one wins.

**Unblocks:** every new family. Adding wasi-audio means
attributing its composite, not editing Preview2's resolver.

### 1c. Auto-discover DI sibling assemblies

**Where:** new assembly-level attribute in `Wacs.ComponentModel.Runtime`,
consumed in `Wacs.Transpiler.Lib/AOT/Component/HostPackageResolver.cs`
and `Wacs.WASI.Preview2.DependencyInjection/WasiPreview2RuntimeScope.cs`.

v0 needed THREE redundant load mechanisms for `Wacs.WASI.GFX.DependencyInjection.dll`
(ProjectReference, explicit `Assembly.Load` in `WasiGfxSilkBindable`,
adding to `ResolveHostPackages` in the CLI) because none alone
covered every race. Replace with a single declarative hook:

```csharp
// In Wacs.WASI.GFX/AssemblyInfo.cs:
[assembly: WacsDependencyInjectionSibling(
    "Wacs.WASI.GFX.DependencyInjection")]
```

The resolver, on loading the contract assembly, `Assembly.Load`s
every sibling declared by attribute. The CLI's `ResolveHostPackages`
becomes a no-op for siblings (the attribute drives it).

**Acceptance:** delete the explicit `Assembly.Load` in
`WasiGfxSilkBindable.BindToRuntime` and the GFX entries in
`RunHandler.ResolveHostPackages`. Test that `--wasi-gfx` still
resolves all impls.

**Unblocks:** future families don't need to teach the CLI about
their packages.

### 1d. Source-gen package-mapping via assembly attribute

**Where:** `Wacs.ComponentModel.Bindgen.SourceGen/WitHostInterfaceGenerator.cs`,
new assembly-level attribute in `Wacs.ComponentModel.Runtime`.

`WitHostPackageNamespaceMap` (added in v0) is a project-wide
MSBuild string. It works but the configuration is manual and
per-project. Replace with a self-describing attribute:

```csharp
// Generated automatically into each package's AssemblyInfo
// alongside the [WitSource]-attributed interfaces:
[assembly: WitPackageMapping(
    Package = "wasi:io", Namespace = "Wacs.WASI.Preview2.Io")]
```

Downstream package's source-gen walks the referenced assemblies'
`[WitPackageMapping]` attributes and builds the map automatically.
Manual `WitHostPackageNamespaceMap` becomes the override mechanism
for edge cases only.

**Acceptance:** delete the `WitHostPackageNamespaceMap` property
from `Wacs.WASI.GFX.csproj`; the generated `Surface.g.cs` still
references `Wacs.WASI.Preview2.Io.IPollable`.

**Unblocks:** any new family with cross-package WIT refs (the
common case for any non-Preview2 family).

### 1e. Canonical-ABI shape coverage tests

**Where:** new test project `Wacs.Transpiler.Lib.CanonAbi.Test`,
exercising `CanonicalSlotCount` + `EmitLiftForType` /
`EmitLowerForType` for every WIT type combinator.

v0 discovered `list<borrow<R>>` was silently unsupported. The fact
that this wasn't caught earlier is a coverage hole.

Add tests covering at minimum:
- `list<T>` for T ∈ {primitive, string, byte[], own<R>, borrow<R>, tuple, record, option, result}
- `option<T>` for the same set
- `result<T, E>` for both arms
- nested combinations one level deep (`list<option<R>>` etc.)

Each test asserts both that direct-link emit accepts the shape
AND that a round-trip lift+lower produces equivalent values.

**Acceptance:** all currently-supported shapes pass; shapes that
don't yet round-trip are explicit `Skip` with a TODO. The CI
fails when an unintentional regression drops a shape.

**Unblocks:** new families don't accidentally hit unsupported
canonical-ABI shapes at runtime.

### 1f. First-class IL emit for WIT static methods

**Where:** `Wacs.Transpiler.Lib/AOT/Component/DirectLinkedImportEmit.cs`,
optionally retire `Wacs.ComponentModel.Runtime/HostInterfaceRuntime.cs`.

v0 emits WIT `static func` as a C# `static` default-interface
method with a body that reflectively finds an impl class's
`{Name}Static` factory. Every `[static]X.Y` invocation pays a
reflection cache lookup + invoke.

The transpiler already knows the impl class for any
`[static]X.Y` via `TryFindResourceImpl(typeof(IX))`. Drop the
interface body's reflection entirely; emit IL that calls the
impl class's `{Name}Static` directly:

```cil
call <impl>.<Name>Static(...)
```

The source-gen still emits the `static` interface method to keep
the resolver's classification; its body becomes a single `throw
new NotImplementedException("Direct-link path required")` —
unreachable at runtime under the transpiler.

**Acceptance:** `[static]buffer.from-graphics-buffer` invocation
no longer routes through `HostInterfaceRuntime.InvokeStaticFactoryReflective`
(verify via a perf test or a `Method.GetMethodBody` inspection).
Same fixture still renders.

**Unblocks:** modest perf for any family with static WIT methods.
Future Preview2 work on `Fields.from-list` etc. doesn't need the
shim either.

### 1g. Replace `WasiGfxAmbient` with scoped backend factory

**Where:** `Wacs.WASI.GFX/WasiGfxAmbient.cs` (delete),
`Wacs.WASI.GFX.DependencyInjection/{Context,Surface,...}.cs`
(rework), `Wacs.Transpiler.Lib/AOT/Component/DirectLinkedImportEmit.cs`
(constructor emit).

v0's resource constructors use a process-global
`WasiGfxAmbient.Backend` static because the SourceGen-resource
convention requires a parameterless ctor — there's no DI hook
to inject the backend. This breaks multi-runtime-in-one-process
embedders.

The fix has two parts:

1. **Source-gen convention:** generate a parameterized ctor on
   the impl class taking a `WacsResourceContext` (or similar
   typed context). The transpiler's resource-construction IL
   emit passes `Resources` (already on the module ctor) when
   `Newobj`'ing the impl.

2. **`WasiGfxConfiguration` / `WasiGfxBundle` thread the
   backend** through the context; the impl's `Create()` pulls
   it from the context, not the ambient.

This is the most invasive item in Phase 1 — source-gen + transpiler
+ contract package all touch. Schedule last in Phase 1.

**Acceptance:** delete `WasiGfxAmbient.cs`. A test that
constructs two `WasmRuntime`s with different `IBackend` instances
in the same process renders correct fixtures into each
(currently this is broken — the second `SetBackend` clobbers the
first).

**Unblocks:** library embedders (C# apps hosting WACS) that want
multiple wasm components running with different host configs.

### 1h. Auto-generate composite bundles

**Where:** new `Wacs.ComponentModel.Bindgen.SourceGen` feature.

`WasiPreview2NNBundle` and `WasiPreview2GfxBundle` are hand-written
classes forwarding 20+ properties each. A `WasiPreview2NNGfxBundle`
for a guest importing both would need 40+ forwards. Combinatorial
explosion.

Add an MSBuild item:

```xml
<ItemGroup>
    <WacsCompositeBundle Include="Wacs.WASI.Preview2.WasiPreview2Bundle" />
    <WacsCompositeBundle Include="Wacs.WASI.GFX.DependencyInjection.WasiGfxBundle" />
</ItemGroup>
```

The source-gen emits a `WasiPreview2GfxBundle` class with all
forwarders, the `[WacsCompositeBundle(Family=...)]` attribute,
and the DI registration extension method. Hand-written composites
become auto-generated.

**Acceptance:** delete `WasiPreview2NNBundle.cs` and
`WasiPreview2GfxBundle.cs`; replace with `<WacsCompositeBundle>`
items in the respective DI csprojs. Both bundles still resolve
identically.

**Unblocks:** N-way composites for guests importing multiple
families.

### 1i. Preview2 multi-version `wasi:io` support

**Where:** `Wacs.WASI.Preview2/Io/IoBindings.cs`.

Today Preview2 binds `wasi:io/poll@0.2.8` only. The upstream
wasi-gfx proposal pins to `wasi:io@0.2.0` (shape-identical, just
the version string differs). v0 patched the vendored copy; better
fix is server-side.

Change `IoBindings` to register at every io version Preview2
ships compatibility for. Start with `0.2.0`, `0.2.1`, `0.2.2`,
... up to `0.2.8`. Same handlers, different namespace strings.

**Acceptance:** delete the `wasi:io` bump from
`Wacs.WASI.GFX/wit/{surface,deps/io}.wit`; the upstream
wasi-gfx@HEAD vendored verbatim still runs.

**Unblocks:** any future component built against an older io
version — common in the wasi-gfx ecosystem today.

### Phase 1 sequencing

Land in this order:

1. **1a + 1e** (diagnostics + coverage tests). Quick wins; everything else benefits from being able to debug.
2. **1b + 1c + 1d** (attribute-driven discovery). Removes the largest cluster of new-family hardcoded edits.
3. **1i** (multi-version io). Trivial, unblocks ecosystem compatibility.
4. **1f** (first-class static-method IL emit). Drops the reflective helper.
5. **1h** (auto-generated composites). Source-gen feature, modest scope.
6. **1g** (scoped backend factory). Largest blast radius, schedule last.

After 1a–1d ship, the next phase's developer experience is
dramatically better — phases 2–4 below should land in 1/3 the time
v0 took.

---

## Phase 2 — Window-close / Quit-menu graceful shutdown

v0 limitation: clicking the SDL window's close button, selecting
"Quit Wacs.Console" from the macOS menu bar, or sending SIGINT
to the process don't cleanly cancel the wasm guest. User has to
Ctrl-C the terminal.

**Root cause:** SDL emits `SDL_QUIT` events but `SilkGfxBackend.RunMainLoop`'s
switch statement explicitly ignores them ("v0 lets the embedder's
cancellation token drive shutdown"). There's no plumbing between
the OS quit event and the `CancellationTokenSource` driving
`ExecuteWindowed`'s wasm task.

**Fix:**
- Wire `SDL_QUIT` to a backend-owned `CancellationTokenSource` that
  the embedder can subscribe to.
- `ExecuteWindowed` registers cancellation on the wasm runtime's
  gas-limit hook so an in-progress wasm `poll()` aborts on the
  next iteration.
- Handle `SDL_WINDOWEVENT_CLOSE` per-window: if it's the last
  window, signal quit; otherwise just close that one.
- On macOS, also handle the `Cmd-Q` AppKit event (SDL2 forwards
  this as `SDL_QUIT`).

**Acceptance:**
- Triangle fixture exits cleanly within 100ms of clicking the
  close button on any platform.
- `Cmd-Q` exits cleanly on macOS.
- Process exit code is 0 (clean shutdown), not 130 (SIGINT).

**Estimated effort:** ~1 day, including cross-platform testing.
Self-contained — no architectural dependencies.

---

## Phase 3 — `wasi:webgpu`

The fourth wasi-gfx WIT package. ~35 KB of WIT, mirrors the
browser WebGPU spec verbatim — roughly 10× the WIT surface of
all three v0 packages combined. New `WACS.WASI.GFX.Webgpu` sibling
package; the existing `WACS.WASI.GFX.Silk` either extends to add
WebGPU support via `Silk.NET.WebGPU` (which wraps wgpu-native),
or a new `WACS.WASI.GFX.Webgpu.Silk` sibling owns the GPU path.

### 3a. Vendor + source-gen

- Vendor `webgpu/webgpu.wit` from `WebAssembly/wasi-gfx@HEAD`
  with the same io-version-bump deviation as v0 (or, if Phase 1i
  landed, no deviation needed).
- Source-gen emits ~30 interfaces / 100+ methods. The bulk of
  the work is per-resource: `gpu-adapter`, `gpu-device`,
  `gpu-queue`, `gpu-buffer`, `gpu-texture`, `gpu-render-pipeline`,
  `gpu-compute-pipeline`, etc.

### 3b. SPI extension

`WACS.WASI.GFX/IBackend.cs` gains:

```csharp
IGpu CreateGpu();
```

Plus the SPI surface for the GPU resource hierarchy. Aim for
backend-agnostic types so future non-Silk backends (e.g.
Vulkan-direct) can fit.

### 3c. Silk webgpu backend

`Silk.NET.WebGPU` already wraps wgpu-native. Map our SPI to its
API. Most of the per-resource impl classes are
delegate-to-Silk plumbing; the wit-bindgen-emitted shapes
(descriptors, etc.) need lift/lower to Silk's types.

### 3d. Graphics-context bridge

`wasi:graphics-context.context.get-current-buffer()` returns an
`abstract-buffer` that webgpu turns into a `GPUTexture`. The
existing CPU-path `abstract-buffer` wraps a byte[]; the webgpu
path needs to wrap a swapchain texture handle. Either:

- Polymorphic `IAbstractBuffer` impls (CPU vs GPU), surface
  picks based on which rendering API connected first, OR
- Per-graphics-context type — `CpuGraphicsContext` vs
  `WebgpuGraphicsContext` selected at construction.

The wasi-gfx WIT doesn't distinguish, so the polymorphic
approach is closer to the spec.

### 3e. Parity fixtures

The upstream `wasi-gfx-runtime/examples/apps/` directory has
`hello_compute` (compute pipeline) and `skybox` (full
render-pipeline with KTX2 textures). Port both as Rust fixtures
into `Spec.Test/components/fixtures/`.

**Acceptance:**
- `wacs run --wasip2 --wasi-gfx --windowed --call start hello_compute.component.wasm`
  renders the compute output.
- `wacs run --wasip2 --wasi-gfx --windowed --call start skybox.component.wasm`
  renders the skybox.
- Both visually match the wasmtime-based upstream reference.

**Estimated effort:** 3–6 weeks of focused work depending on how
deep the Silk.NET.WebGPU mapping goes. The largest single phase
in v1.

### 3f. v1 release packaging

Bump `WACS.WASI.GFX 0.1.0 → 0.2.0` (minor — new package in
family) plus the sibling. Family tag: `WACS-WASI-GFX-v0.2.0`.

---

## Phase 4 — Documentation refresh

After Phases 1–3 land, the docs should match reality. Refresh:

### 4a. Component-chaining doc

`docs/COMPONENT_CHAINING.md` describes the existing N=2 architecture
(Preview2 + wasi-nn). Update to:
- Describe the attribute-driven discovery from Phase 1b/1c/1d.
- Add a "Writing a new WASI host package" guide that walks through:
  scaffolding the contract + DI + backend, declaring composite
  bundle attributes, declaring `WitPackageMapping` attributes,
  adding to the CLI's `--bind` resolution path.
- Update the example block to show wasi-gfx alongside the
  existing wasi-nn / Preview2 cases.

### 4b. Transpiler architecture doc

`Wacs.Transpiler/Wacs.Transpiler/README.md` — refresh the
direct-link sections to cover:
- `IResource[]` canonical-ABI support
- Static-method first-class IL emit
- Attribute-driven bundle / sibling discovery
- The new `--trace-imports` diagnostic flag

### 4c. wasi-gfx-specific docs

- `Wacs.WASI/Wacs.WASI.GFX/README.md` (per-package README)
  refreshed for v1 webgpu coverage.
- New `docs/WASI_GFX_USAGE.md` modeled after the existing
  `WASI_NN_USAGE.md` — walks through embedder use cases,
  threading, backend selection, and common debugging.

### 4d. Migration notes

If any of Phase 1's items have breaking changes for downstream
consumers, capture them in `docs/MIGRATION_v0_to_v1.md`. Phase 1
is designed to be additive, but specifically:
- Phase 1g (scoped backend factory) replaces `WasiGfxAmbient` —
  embedders directly using the ambient need a migration path.
- Phase 1f drops the reflective `InvokeStaticFactoryReflective`
  helper — anyone who built custom SourceGen output against it
  needs to migrate.

**Acceptance:** an outside developer can follow the docs to add
a new WASI host family without reading any source.

**Estimated effort:** 2–3 days, parallelizable with late Phase 3.

---

## Out of scope for v1

Explicit non-goals so we don't accumulate scope creep:

- **`WACS.WASI.GFX.RayLib`** backend. Original plan called it
  out as a parallel sibling; cut from v0; still deferred. The
  cost of maintaining two CPU-path backends is not justified
  while one works.
- **Multi-window composition.** v0 supports one surface per
  graphics-context. The wasi-gfx WIT today doesn't have a
  clear story for N surfaces sharing a context either; defer
  until upstream clarifies.
- **Custom rendering pipelines beyond webgpu.** Vulkan-direct,
  Metal-direct, etc. are theoretically possible additional
  backends but have no demonstrated demand.
- **VR/AR.** Upstream wasi-gfx README explicitly lists this as
  a non-goal.

---

## Acceptance for v1 exit

- All Phase 1 items shipped and validated.
- Window close / Quit menu work cleanly on macOS, Linux,
  Windows.
- `wasi:webgpu` end-to-end on both interpreter and transpiler
  paths.
- A new host family (synthetic test or wasi-audio prototype)
  scaffolds in under a day of focused work using only the
  refreshed docs.
- Family tag: `WACS-WASI-GFX-v1.0.0`.
