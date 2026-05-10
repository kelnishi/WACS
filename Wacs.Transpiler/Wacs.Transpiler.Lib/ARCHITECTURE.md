# Wacs.Transpiler.Lib — Architecture

The WACS AOT transpiler converts a WebAssembly module into a .NET assembly
whose public surface is a regular CLR `Module` class with typed `IExports` /
`IImports` interfaces. The generated assembly runs through the CLR's normal
JIT (or NativeAOT codegen if the consumer asks), bypassing the
expression-tree dispatch the interpreter (`Wacs.Core.Runtime`) uses.

This README documents the *architecture* — pipeline stages, design choices,
and a code map. For CLI usage and integration recipes, see the
[`wacs aot` section](../../Wacs.Console/Wacs.Console/README.md#wacs-aot--wasm--nativeaot-native-binary)
of the Wacs.Console README.

## Where it sits

```
WASM bytes
    │
    ▼  BinaryModuleParser.ParseWasm
WasmModule (Wacs.Core)
    │
    ▼  WasmRuntime.InstantiateModule   (interpreter pre-pass — types, validation)
ModuleInstance
    │
    ▼  ModuleTranspiler.Transpile        ← THIS LIBRARY
TranspilationResult { PersistedAssemblyBuilder, types, methods, manifest }
    │
    ▼  TranspilationResult.Bake          (Save → MemoryStream → AssemblyLoadContext.LoadFromStream)
loaded Assembly with runtime-instantiable Module class
    │
    ▼  TranspiledModuleLoader.Load       (discovery + import wiring)
LoadedModule handle ready for export invocation
```

The transpiler reuses the interpreter for the parse / validate / instantiate
pre-pass — the same `WasmModule` and `ModuleInstance` types feed both engines.
This guarantees the two engines see the same module shape and the spec-test
corpus exercises both.

## Public entry points

| Symbol | File | Purpose |
|---|---|---|
| `ModuleTranspiler.Transpile(ModuleInstance, WasmRuntime, string)` | `AOT/ModuleTranspiler.cs:313` | Compile a core wasm module to IL. Returns `TranspilationResult`. |
| `ComponentTranspiler.TranspileSingleModule(Stream, …)` | `AOT/Component/ComponentTranspiler.cs:155` | Component-model wrapper. Routes to `ModuleTranspiler` and lays in the WIT-shaped surface (`ComponentExports`, `[WitSource]`-tagged `I{Iface}` types, `ComponentMetadata`). |
| `TranspilationResult.Bake()` | `AOT/ModuleTranspiler.cs` | Save the `PersistedAssemblyBuilder` to a `MemoryStream`, load it back into the default `AssemblyLoadContext`, and remap public type/method accessors to the loaded types. Idempotent; auto-fired by public accessors. |
| `TranspilationResult.SaveAssembly(string)` | `AOT/ModuleTranspiler.cs` | Persist to `.dll`. Internally bakes once and writes the cached PE bytes. |
| `TranspiledModuleLoader.Load(string, object?, bool)` | `Hosting/TranspiledModuleLoader.cs:61` | Load a saved `.dll`, discover Module class + imports interface, instantiate, return a `LoadedModule` handle. Supports collectible `AssemblyLoadContext` isolation. |
| `MainEntryEmitter.Emit(TranspilationResult, string, string)` | `AOT/MainEntryEmitter.cs` | Bake a `Program.Main(string[])` into the assembly that constructs the module, parses argv, and invokes a named export. Component-mode equivalent: `ComponentMainEntryEmitter`. |

## Pipeline (linear walk)

The numbered steps below correspond to comments in
`AOT/ModuleTranspiler.cs:Transpile`.

### 0. Assembly + module builder

```csharp
var assemblyBuilder = new PersistedAssemblyBuilder(
    assemblyName, typeof(object).Assembly);
var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
```

`PersistedAssemblyBuilder` is the .NET 9+ replacement for the legacy
`AssemblyBuilderAccess.Save` mode (which Microsoft removed when .NET Core
shipped). Types defined under it are *metadata-only* until the assembly is
serialized via `Save(Stream)` and reloaded; see [Bake](#bake) below.

### 0a. Interface generation (`InterfaceGenerator`)

Walks `moduleInst.Repr.Exports` / `moduleInst.Repr.Imports`. Emits two
typed interfaces:

- `IExports` — one method per wasm export, signature in CLR-ised form
  (e.g. `i32` → `int`, `f64` → `double`, multi-return → `(out T1, out T2)`
  pattern).
- `IImports` — one method per wasm import. The Module class's constructor
  takes one as a parameter; every guest→host call dispatches through it
  (or, if the host package resolved the import, through inline IL — see
  [direct-linked imports](#direct-linked-imports-component-mode)).

Emits `[WacsTranspiledImports("Ns.IImports")]` on the assembly so
`WACS.HostBindings.SourceGen` consumers can find the import surface. Also
emits `[WacsImportNames]` carrying the `(methodName, wasmModule, wasmName)`
mapping for source-gen binding match.

### 0a.1. Host-package resolver (component mode only)

If `TranspilerOptions.Resolver` is set, every import method is queried
against it. Hits get inline IL emission (skips the delegate-table dispatch);
misses keep the legacy path. The map is stashed on
`_options.ResolverImportBindings` and consumed by `CallEmitter` /
`DirectLinkedImportEmit` at IL emission time.

### 0b. GC type emission (`GcTypeEmitter`)

WASM GC structs/arrays become native CLR classes — typed fields, not
`StoreStruct`/`Value[]` wrappers. Each emitted type registers under
`(initDataId, typeIdx)` in `GcTypeRegistry`. The runtime's `ConvertStoreArray`
and `ConvertStoreStruct` (`Emitters/GcEmitter.cs`) call
`Activator.CreateInstance` against that runtime Type — see
[GcTypeRegistry remap](#gctyperegistry-remap) for how those registry entries
flow from metadata-only `TypeBuilder.CreateType()` results to runtime types.

### 0c. Element segment evaluation (mostly eager, partly deferred)

For each wasm element segment:

```csharp
for (int i = 0; i < elem.Initializers.Length; i++)
    values[i] = EvaluateElemExpr(elem.Initializers[i], gcTypeEmitter);
ModuleInit.RegisterElemSegment(values);
```

Initializers that produce GC objects (`array.new`, `array.new_default`,
`array.new_fixed`) need the emitted CLR types to be runtime-instantiable —
which they aren't yet under PAB. Those segments get flagged via
`HasGcConstructor` (`AOT/ModuleTranspiler.cs`) and stashed on the result for
post-Bake re-evaluation; see [Deferred GC element segments](#deferred-gc-element-segments).

### 1. Method stubs (Pass 1)

```csharp
methodBuilders[i] = CreateMethodStub(typeBuilder, funcInst, funcType, i);
```

Each wasm function gets a `MethodBuilder` on the `Functions` class with the
correct CLR signature: first param is `ThinContext` (carries memory, table,
global slots), then mapped wasm params, then `out` params for multi-return
results past the first. Two-pass design lets the IL emitter emit `call`
references to functions defined later in the module without worrying about
emit order.

### 2. IL emission (Pass 2 — `FunctionCodegen` + emitters)

```csharp
var codegen = new FunctionCodegen(mb, funcInst, …);
bool emitted = codegen.TryEmit();
if (!emitted) EmitFallbackBody(mb, …);
```

`FunctionCodegen` walks the function's wasm instructions and dispatches each
to a per-prefix emitter:

| Prefix | Emitter | Examples |
|---|---|---|
| Numeric | `Emitters/NumericEmitter.cs` | `i32.add`, `f64.mul`, `local.tee`, … |
| Variable | `Emitters/VariableEmitter.cs` | `local.get`, `local.set`, `global.get` |
| Memory | `Emitters/MemoryEmitter.cs` | `i32.load`, `i64.store8`, `memory.size` |
| Control | `Emitters/ControlEmitter.cs` | `block`, `loop`, `if`, `br`, `br_table` |
| Call | `Emitters/CallEmitter.cs` | `call`, `call_indirect`, `return_call*` |
| Reference | `Emitters/TableRefEmitter.cs` | `ref.null`, `ref.func`, `table.get` |
| Bulk (0xFC prefix) | `Emitters/BulkEmitter.cs` | `memory.copy/fill/init`, `table.copy/init`, `data.drop`, `elem.drop` |
| Atomic (0xFE prefix) | `Emitters/AtomicEmitter.cs` | `i32.atomic.rmw.add`, `memory.atomic.notify` |
| GC (0xFB prefix) | `Emitters/GcEmitter.cs` | `struct.new`, `array.get_s`, `ref.test`, `br_on_cast` |
| SIMD (0xFD prefix) | `Emitters/SimdEmitter.cs` | `v128.load`, `i32x4.add`, `i8x16.shuffle` |
| Exceptions (0x06/0x07/0x08) | `Emitters/ExceptionEmitter.cs` | `try_table`, `throw`, `throw_ref` |

Each emitter reads/writes the IL-side stack via `StackAnalysis` so multi-arity
operations track type information across blocks. Functions whose IL would
violate ECMA-335 stack consistency (or whose emit code rejects an unsupported
edge case) fall back to a stub that throws `WacsTranspilerFallback` at runtime
— the runtime then routes the call through the interpreter's stack invoker.

`CilValidator` (`AOT/CilValidator.cs`) runs after IL emission and rejects bad
emissions at *transpile time* instead of letting them surface as
`InvalidProgramException` at JIT.

### 3. Module class generation (`ModuleClassGenerator`)

Builds the `Module` class consumers see. Hot code paths:

- **Constructor** (`AOT/ModuleClassGenerator.cs:EmitConstructor`)
  - Standard emission: calls `InitializationHelper.InitializeFromEmbedded(span, initDataId)` against the RVA-mapped codec blob (see [Codec blob](#codec-blob-__wacsinitdata)).
  - AotLinked emission: builds `ThinContext` from inlined IL constants — no codec, no registry roundtrip. See [Emission targets](#emission-targets).
- **Export methods** (`EmitExportMethods`) — one per `IExports` method, body forwards to the corresponding `Functions` static method with `_ctx` prepended.
- **Memory properties** (`EmitMemoryProperties`) — exposes `Memory[i]` as `byte[]` getters so component-mode code (`ComponentExportsEmit`) can blit canonical-ABI strings/arrays.

### 4. CreateType + Bake

```csharp
var functionsType = typeBuilder.CreateType()!;
var moduleClassType = moduleClassGen.CreateType();
var result = new TranspilationResult(…);
result.SetPendingElemReeval(gcTypeEmitter, pendingElemReeval);
return result;
```

After `Transpile` returns, the result is *pre-bake*: types are
metadata-only `PersistedTypeBuilder.CreateType()` outputs. Callers that want
to add more types — `MainEntryEmitter`, `ComponentMainEntryEmitter`,
`ComponentTranspiler`'s composition step — do so via `result.ModuleBuilder`
and the internal `*Builder` accessors. First touch of a public accessor
(`result.Assembly`, `result.ModuleClass`, etc.) triggers `Bake()`, which
freezes the builder.

## Architectural design choices

### PersistedAssemblyBuilder + Bake roundtrip

**Why:** `AssemblyBuilder.DefineDynamicAssembly(Run)` produces an
in-process-only assembly that can't be serialized. The legacy WACS
transpiler (pre-migration) used `Lokad.ILPack 0.3.1` to walk the live dynamic
assembly and write a PE — but Lokad NRE'd on `Ldtoken` of any field created
via `DefineInitializedData`, blocking RVA-mapped data segments end-to-end.

**Fix:** Build into a `PersistedAssemblyBuilder` (`System.Reflection.Emit`,
.NET 9+). It supports `DefineInitializedData` end-to-end through `Save`. The
catch is that its `TypeBuilder.CreateType()` returns a *metadata-only* Type
that can't be `Activator.CreateInstance`'d — the runtime needs the assembly
to round-trip through `Save(Stream)` + `AssemblyLoadContext.LoadFromStream`
first.

`TranspilationResult.Bake()` does the round-trip once and remaps:

- `Assembly`, `ModuleClass`, `ExportsInterface`, `ImportsInterface`,
  `FunctionsType` → resolved by `FullName` against the loaded assembly.
- `Methods[]`, `FunctionMethodMap` → resolved by name + parameter
  signature on the loaded declaring type.

Public accessors (`Assembly`, `ModuleClass`, etc.) auto-bake on first read.
Internal `*Builder` accessors (`ModuleClassBuilder`, `ExportsInterfaceBuilder`,
…) skip the bake so emitters that run after `Transpile` returns can keep
adding types via `result.ModuleBuilder`.

#### Corelib AssemblyRef rewrite at SaveAssembly

PAB stamps the saved DLL's corelib AssemblyRef as `System.Private.CoreLib`
(the runtime impl identity, taken from `typeof(object).Assembly`). The C#
compiler resolves base types from the ref-pack contract `System.Runtime`
instead, so consumer csprojs that statically reference our saved `.dll`
trip CS0012: *"the type 'Object' is defined in an assembly that is not
referenced. You must add a reference to assembly 'System.Private.CoreLib'"*.

The official PAB workaround
([dotnet/runtime#103357](https://github.com/dotnet/runtime/issues/103357))
is to hand PAB a `MetadataLoadContext`-loaded `System.Runtime` from the
ref pack instead of `typeof(object).Assembly` — but only if every type
passed to PAB is also MLC-bound. Our IL emit uses impl `typeof(...)`
everywhere; mixing the two produces saved DLLs whose memberrefs can't
runtime-bind. Migrating the entire IL-emit pipeline to MLC-bound corelib
types is a large refactor we punted.

Instead, [`CoreLibAssemblyRefRewriter`](AOT/CoreLibAssemblyRefRewriter.cs)
post-processes a copy of the baked bytes at
`TranspilationResult.SaveAssembly` time, rewriting the corelib AssemblyRef
in place: name `System.Private.CoreLib` → `System.Runtime`, public-key
blob shrunk from a 160-byte full key to the 8-byte System.Runtime PKT,
Flags bit cleared. The runtime treats both identities as type-equivalent
through type-forwards, so semantics are preserved. The in-process
Bake/Load round-trip skips the rewriter entirely so the loaded
`_assembly` keeps its impl-corelib identity for callers that introspect
metadata reflectively.

**Known limitation.** Generic-instantiation FieldRefs that cross the
renamed corelib boundary (e.g. `[System.Runtime]List<Wacs.Core.Value>`)
fail to bind in isolated `AssemblyLoadContext`s. The
`AotLinkedCallIndirectModule_CrossProcess_RoundTrip` test exercises this
(Skip-marked with the same explanation in the test source). The
`wacs aot --wasi` smoke test path works fine because it uses Standard
emission and never triggers AOT-linked generic FieldRefs. The hack
disappears whenever PAB upstream fixes the issue or the IL-emit pipeline
is migrated to MLC-bound corelib types — see the long-form rationale in
the rewriter source.

### RVA-mapped data segments

Wasm data segment bytes are stored as RVA-mapped initialized data in the
emitted PE — bytes live in the `.sdata`/`.rdata` section, demand-paged from
disk by the OS loader, surfaced zero-copy as `ReadOnlySpan<byte>` via
`RuntimeHelpers.CreateSpan<byte>(field.FieldHandle)`.

Two distinct blobs:

#### `__WACSAotData.Segment_N` (active data segments under AotLinked emission)

`AOT/ModuleClassGenerator.cs:EmitDataSegmentCopies` emits, per active
segment:

```il
ldtoken     <__WACSAotData.Segment_N>
call        ReadOnlySpan<byte> RuntimeHelpers::CreateSpan<byte>(RuntimeFieldHandle)
ldloc       ctxLocal
ldfld       <ctx.Memories>
ldc.i4      memIdx
ldelem.ref
ldfld       <Memory.Data>
ldc.i4      offset
ldc.i4      len
call        BulkHelpers::CopySegmentToMemory(ReadOnlySpan<byte>, byte[], int, int)
```

`BulkHelpers.CopySegmentToMemory` (`AOT/Emitters/BulkEmitter.cs`) does
`src.Slice(0, len).CopyTo(dst.AsSpan(dstOffset, len))` — a single
`memcpy`-equivalent from PE pages to the wasm linear memory's backing array.

#### Codec blob (`__WACSInit.Data`)

The whole `ModuleInitData` (memories, tables, globals, type hashes,
saved-segment bytes, deferred globals — everything the cross-process Module
ctor needs) is serialized via `InitDataCodec.Encode` and stashed as RVA on
`__WACSInit.Data`. The Module ctor IL:

```il
ldtoken     <__WACSInit.Data>
call        ReadOnlySpan<byte> RuntimeHelpers::CreateSpan<byte>(RuntimeFieldHandle)
ldc.i4      _initDataId
call        ThinContext InitializationHelper::InitializeFromEmbedded(ReadOnlySpan<byte>, int)
```

`InitializeFromEmbedded` has two paths:

- **In-process** (transpile-and-load in same process): `InitRegistry`
  contains the entry; skip the codec entirely. The pinned span is never
  touched.
- **Cross-process** (saved `.dll` loaded fresh): `InitRegistry` is empty;
  call `InitDataCodec.Decode(ReadOnlySpan<byte>)` which uses
  `UnmanagedMemoryStream` over the pinned span — no allocation, no copy
  (true zero-copy from PE pages to `ModuleInitData`).

Pre-migration the same blob lived in the user-string heap (`#US`) as a
base64 string literal decoded on cctor. That cost ~2.67× disk
(4/3 base64 expansion × 2 UTF-16 doubling) plus a one-shot
`Convert.FromBase64String` allocation at startup. Measurements on a 4 KB
data segment fixture (see `Wacs.Transpiler.Test/PeShapeTests.cs`):

| Metric | RVA path | Base64 baseline |
|---|---|---|
| Codec blob on disk | 4 205 B | 11 213 B |
| Reduction | — | **62.5%** |

### Deferred GC element segments

GC-typed element-segment initializers (`array.new` etc.) need the emitted
CLR class to be *runtime-instantiable* (so `Activator.CreateInstance` works).
Pre-bake the class is metadata-only and `CreateInstance` refuses with
`ArgumentException("Type must be a type provided by the runtime.")`.

Pipeline:

1. During `Transpile`, `ModuleInit.RegisterElemSegment` is called eagerly
   with placeholder `Value(ValType.Any)` for any segment whose initializer
   contains `array.new`/`array.new_default`/`array.new_fixed`.
2. The (segId, initializers[]) pairs land on
   `TranspilationResult._pendingElemReeval`.
3. `Bake()` calls `gcTypeEmitter.RemapEmittedToLoadedTypes(loadedAssembly)`
   — replaces every `EmittedTypes[idx].ClrType` with the runtime-loaded
   equivalent.
4. Re-runs `EvaluateElemExprForBake` against each captured initializer and
   calls `ModuleInit.UpdateElemSegment` to overwrite the placeholder.

Same pattern via `GcTypeRegistry.RemapToLoadedTypes` for the runtime
helpers (`ConvertStoreArray`, `ConvertStoreStruct`) that consume the
registry post-Bake.

### Emission targets

`TranspilerOptions.Emission` selects between three modes:

- **`Auto`** (default) — picks `AotLinked` when the module fits the
  feasibility envelope (`IsAotLinkedAutoPromotable` in
  `ModuleClassGenerator.cs`); falls back to `Standard` otherwise.
  Saves ~50% on first-trial cold start and ~28% on warm cold start
  for promoted modules — the codec stack
  (`InitDataCodec.Decode`, `BinaryReader`, `InitializeFromData`)
  never JITs and never runs.
- **`Standard`** — Module ctor calls
  `InitializationHelper.InitializeFromEmbedded` against the RVA-mapped
  codec blob. Cross-process safe; works for any module shape. The
  codec machinery (`InitDataCodec`, `InitRegistry`, `ModuleInit`)
  ships in the consumer's binary.
- **`AotLinked`** — Module ctor builds `ThinContext` directly from
  inlined IL constants. No `__WACSInit` holder, no codec call, no
  registry dependency. NativeAOT consumers can dead-strip the codec
  machinery from the final native binary. Throws at transpile time
  via `AssertAotLinkedFeasible` if the module shape requires the
  codec stack — use `Auto` for the "AotLinked when feasible,
  Standard otherwise" semantics.

The `AotLinkedSavedDllOmitsCodecHolderType` test
(`Wacs.Transpiler.Test/AotLinkedEmissionTests.cs`) is the trim-evidence
assertion: confirms the saved AotLinked `.dll` references neither the
`__WACSInit` holder type nor `InitializeFromEmbedded`.

#### Auto promotion envelope

`IsAotLinkedAutoPromotable` is stricter than `IsAotLinkedFeasible` —
it only promotes shapes already covered by an existing AotLinked
emission test, so a configuration that's feasible-but-not-tested
falls back to Standard rather than risking silent miscompilation.

Currently auto-promoted:

| Shape | Coverage |
|---|---|
| Compute-only modules | ✅ |
| Single memory + active data segments | ✅ |
| Primitive globals (i32/i64/f32/f64) + null/funcref/externref globals | ✅ |
| Tables + funcref active element segments + `call_indirect` | ✅ |
| `ref.test` / `br_on_cast` on funcref | ✅ |
| Passive data segments + `memory.init` | ✅ |
| Passive funcref element segments + `table.init` | ✅ |
| Multi-memory | ✅ |
| Local exception tags | ✅ |
| Imported functions (wired through `IImports`) | ✅ |

Still rejected — fall back to `Standard`:

- Imported memory / table / global / tag (linker integration)
- GC global inits (`array.new`, `struct.new` initializers)
- GC element values (`ref.i31`, `array.new` in element segments)
- Modules with non-encodable element segment initializers
  (`global.get` references, etc.)

The Auto-fallback path is silent: if a module isn't promotable, it
just transpiles to Standard and the consumer sees the codec ctor.
`Emission = AotLinked` explicitly throws if the user pinned the
target but the module is out of envelope — useful for whole-program
NativeAOT builds where you want a build-time error rather than a
silent fallback.

#### What AotLinked emission emits

The AotLinked ctor body, in order (see
`ModuleClassGenerator.EmitAotLinkedCtorBody`):

1. **Memory / Table / Global arrays** — `EmitMemoryArray`,
   `EmitTablesArray`, `EmitGlobalsArray` allocate per-instance
   `MemoryInstance[]` / `TableInstance[]` / `GlobalInstance[]` from
   constant counts + per-slot `Newobj` + `EmitPrimitiveValue` for
   the initial value (handles primitives, null refs, non-null
   funcref/externref).
2. **`new ThinContext(memories, tables, globals, null, null)`** —
   the `ImportDelegates` and `FuncTable` slots are populated later.
3. **`EmitDataSegmentCopies`** — for each active data segment, copy
   from `__WACSAotData.Segment_N` (RVA-mapped) into the right
   memory slot via `BulkHelpers.CopySegmentToMemory`.
4. **`EmitElementSegmentCopies`** — for each active funcref/null
   element segment, write the resolved Value into
   `ctx.Tables[i].Elements[slot]`.
5. **`EmitTypeHashArrays`** — populate `ctx.FuncTypeHashes`,
   `FuncTypeSuperHashes`, `TypeHashes`, `TypeIsFunc` as IL-baked
   constant arrays (consumed by `call_indirect` subtype checks +
   `ref.test`/`ref.cast`).
6. **`EmitActiveSegmentDrops`** — `ModuleInit.DropDataSegment` /
   `DropElemSegment` for each active segment (WASM 3.0 §4.5.4
   step 16).
7. **`EmitSegmentBaseIds`** — stamp `ctx.DataSegmentBaseId` /
   `ElemSegmentBaseId` from transpile-time constants.
8. **`EmitInitDataIdStamp`** — `ctx.InitDataId = _initDataId`. Keys
   into `MultiReturnMethodRegistry` (call_indirect dispatch for
   multi-result funcs) and `GcTypeRegistry` (runtime GC type
   lookups).
9. **`EmitPassiveDataSegmentRegistrations`** — for each passive
   data segment, `ModuleInit.RegisterDataSegmentAt(absoluteId,
   span.ToArray())` so cross-process `memory.init` resolves.
10. **`EmitPassiveElementSegmentRegistrations`** — same recipe for
    element segments, encoded as `int[]` (`-1` = null, `>=0` =
    funcIdx).
11. **`EmitTagsArray`** — `ctx.Tags = new TagInstance[totalTags]`
    with one fresh `TagInstance(null)` per local tag (identity-
    based equality only).

After `EmitAotLinkedCtorBody`, the ctor falls through to the shared
post-init steps used by `Standard`:
`EmitImportDelegateWiring`, `EmitFuncTablePopulation`,
`EmitTypedDelegateShimsAndRegister`, `EmitCabiReallocCacheIfPresent`,
`BindTableDelegates`, `_ctx` field assignment, start-fn invocation.

### Direct-linked imports (component mode)

Default import dispatch:

```il
ldarg.0
ldfld   ctx.ImportDelegates
ldc.i4  i
ldelem.ref
… box args …
callvirt System.Delegate::DynamicInvoke
```

That works but pays the boxing + dictionary-style dispatch on every
guest→host call. With a `HostPackageResolver` configured,
`DirectLinkedImportEmit` recognizes typed `[WitSource]`-tagged interfaces and
emits inline IL:

```il
ldarg.0
ldfld   ctx.HostBundle
callvirt T_HostBundle::get_TypedInterface()
… push wasm args (with canonical-ABI lift if needed) …
callvirt I_TypedInterface::Method(…)
… store results to wasm linear memory at retArea (canonical-ABI lower) …
```

Direct-linked dispatch handles roughly the same shape matrix
`wit-bindgen-csharp` does:

| Shape | Notes |
|---|---|
| primitives | `i32` / `i64` / `f32` / `f64` + narrow ints + `bool` |
| `string` | `cabi_realloc` + UTF-8 / UTF-16 / Latin1 encode per import option |
| `byte[]` (`list<u8>`) | `cabi_realloc` + raw memcpy |
| `Option<T>` | recursive on inner shape (incl. `Option<Option<X>>` / `Option<Result<X,Y>>`) |
| `Result<TOk, TErr>` | each arm storable per the same rules |
| resource handle (`own<R>` / `borrow<R>`) | `Resources.AllocateResource` (return) / `Resources.GetResource` (param) |
| `list<T>` for primitive `T` / `string` / `byte[]` | outer `(ptr, count)` + per-element store |
| `list<list<T>>` | three-level `cabi_realloc` (outer pair-array + per-sub buffers) |
| `list<own<R>>` | per-element `AllocateResource` + `i32` handle |
| `list<tuple<...>>` / `list<record<...>>` | per-element fields packed inline at the type's natural stride. Field types may be primitive / `string` / `byte[]` **or `own<R>`** — covers `wasi:filesystem/preopens.get-directories` (`list<tuple<own<descriptor>, string>>`), `wasi:cli/environment.get-environment` (`list<tuple<string, string>>`), and the broader env / args / headers / TCP `accept` "list of (resource-or-string, label)" shape class. |
| `list<Option<X>>` / `list<Result<X, Y>>` | per-element variant store at outer-ptr + i*elemSize |
| variants | `EmitVariantStoreAt` per-case payload encoding |

Shapes outside that fall back to the delegate path silently.
`CanEmitDirect` (in `DirectLinkedImportEmit.cs`) is the gate;
the resolver-aware predicate variants
(`IsTupleOfFlatFields` / `IsRecordOfFlatFields`) are what gate
the resource-bearing aggregates.

### `ThinContext` — the runtime hand-off slot

`AOT/ThinContext.cs` is the runtime context the generated IL threads
through every call. Fields:

- `Memories` / `Tables` / `Globals` — the standalone allocation slots,
  populated either from the codec or from inlined IL (AotLinked).
- `ImportDelegates` / `FuncTable` — used by call-indirect / non-direct-
  linked imports.
- `Module` / `Store` — back-references to the interpreter side, populated
  only when the assembly runs inside a `WasmRuntime` (mixed-mode). Null in
  standalone runs.
- `HostBundle` — direct-linked imports' typed bundle source.
- `Resources` — slot for component-resource handle tables.
- `InitDataId` — the transpile-time `InitRegistry` key for in-process fast
  paths.

Every transpiled function takes `ThinContext` as its first parameter.
Functions that need cross-module state (call_indirect to imported
functions, throw-into-host, etc.) read it from there; pure compute
functions ignore it.

### Codec format (`InitDataCodec`)

`AOT/InitDataCodec.cs` implements a versioned binary format documented in
`AOT/InitDataFormat.md`. Header carries an 8-byte `WACSINIT` magic + 1-byte
major + 1-byte minor + 2-byte reserved. Body is a sequence of
`(tag, length-prefixed-payload)` sections.

Encoder produces `byte[]`; decoder accepts both `byte[]` (legacy) and
`ReadOnlySpan<byte>` (zero-copy, used by the RVA path). Both routes share a
private `DecodeFromStream(Stream)` helper — the span overload wraps the
pinned span in `UnmanagedMemoryStream` via `fixed (byte* p = bytes)`. This is
the only `unsafe` block in the lib.

Forward compat: unknown section tags are skipped via `ms.Position = end`,
so a v1.M decoder can read v1.N (N > M) files for any additive section
extension. Bumping major is the breaking-change escape hatch.

## Testing surface

| Test class | What it covers |
|---|---|
| `TranspilerTests` | Per-wast `TranspileEachWastModuleNoCrash` smoke — every spec-test wasm transpiles without throwing. |
| `AotSpecTests` | Full WebAssembly 3.0 spec test suite executed through the transpiler — assert-equiv with the interpreter. |
| `AotLinkedEmissionTests` | AotLinked emission end-to-end + trim-evidence (saved `.dll` doesn't reference codec machinery). |
| `CrossProcessLoadTests` | Save → wipe `InitRegistry`/`ModuleInit` → load via `AssemblyLoadContext` → invoke. Exercises the cross-process codec path that the in-process tests don't. |
| `PeShapeTests` | PE-shape regression assertions: `__WACSInit.Data` field RVA non-zero, no large UTF-16 LE base64 run in the `.dll`, plus diagnostic prints (assembly size + cold-start timing). |
| `ComponentTranspilerTests` | Component-mode emission — argv-parse `--emit-main`, named-WIT-type lookup, tuple-return / Result-return shapes. |
| `DirectLinkedImportTests` | Component-mode inline-IL import dispatch matrix (primitives, strings, lists, options, results, resources). |
| `BranchHintEmissionTests` | `metadata.code.branch_hint` custom-section consumption — `if`-arm reordering and `_coldTailEmissions`. |
| `InitDataCodecTests` | Codec encode/decode round-trip for every section. |

## Code map

```
Wacs.Transpiler.Lib/
├── AOT/
│   ├── ModuleTranspiler.cs        — orchestrator + TranspilationResult lifecycle (Bake)
│   ├── FunctionCodegen.cs         — per-function IL emission driver, dispatches to emitters
│   ├── ModuleClassGenerator.cs    — Module class shape: ctor, exports, memory props
│   ├── InterfaceGenerator.cs      — IExports / IImports interface emission
│   ├── GcTypeEmitter.cs           — WASM GC structs/arrays → CLR classes
│   ├── DataSegmentEmitter.cs      — wasm data section walker; emits MemoryDecl + DataSegmentInfo
│   ├── InitDataCodec.cs           — versioned binary serializer for ModuleInitData
│   ├── InitDataFormat.md          — codec format spec
│   ├── InitializationHelper.cs    — runtime init, GcTypeRegistry, ModuleInit
│   ├── ModuleInit.cs              — runtime-side data/element segment registry
│   ├── ThinContext.cs             — runtime context threaded through transpiled IL
│   ├── ModuleLinker.cs            — multi-module composition (cross-module imports)
│   ├── CilValidator.cs            — transpile-time CIL stack-shape validation
│   ├── StackAnalysis.cs           — typed-stack tracking during emission
│   ├── TranspilerOptions.cs       — public options surface (SIMD strategy, emission target, …)
│   ├── MainEntryEmitter.cs        — emits Program.Main for core-WASM modules
│   ├── Emitters/
│   │   ├── NumericEmitter.cs      — i32/i64/f32/f64 + sign-ext + wrap
│   │   ├── VariableEmitter.cs     — local.get/set/tee, global.get/set
│   │   ├── MemoryEmitter.cs       — load/store + memory.size/grow
│   │   ├── ControlEmitter.cs      — block/loop/if/br/br_table/return
│   │   ├── CallEmitter.cs         — call/call_indirect/return_call*
│   │   ├── TableRefEmitter.cs     — ref.* + table.*
│   │   ├── BulkEmitter.cs         — 0xFC-prefix bulk memory/table + BulkHelpers runtime helper
│   │   ├── AtomicEmitter.cs       — 0xFE-prefix shared-memory atomics
│   │   ├── GcEmitter.cs           — 0xFB-prefix struct/array/i31/extern + ConvertStoreArray/Struct
│   │   ├── SimdEmitter.cs         — 0xFD-prefix v128 (scalar + intrinsics paths)
│   │   ├── SimdHelpers.cs         — scalar fallback implementations of v128 ops
│   │   └── ExceptionEmitter.cs    — try_table/throw/throw_ref + WasmException
│   └── Component/
│       ├── ComponentTranspiler.cs       — component-model entry point
│       ├── ComponentExportsEmit.cs      — emits ComponentExports static class with WIT-shaped methods
│       ├── ExportInterfaceEmit.cs       — emits [WitSource]-tagged I{Iface} types
│       ├── DirectLinkedImportEmit.cs    — inline IL import dispatch
│       ├── HostPackageResolver.cs       — discovers (package, interface, item) bindings on host packages
│       ├── ComponentMainEntryEmitter.cs — Program.Main for components
│       ├── ComponentMainHost.cs         — argv parser + invoker shared by ComponentMain
│       ├── ComponentImportStubs.cs      — DispatchProxy-based no-op IImports for components
│       ├── ComponentAssemblyEmit.cs     — embeds ComponentMetadata.EmbeddedWitBytes
│       └── TupleFieldAccess.cs          — Roslyn-compiled ItemPofN<…> helpers (PAB-Ldfld workaround)
└── Hosting/
    ├── TranspiledModuleLoader.cs  — public `Load(path|assembly, imports?, isolate?)` entry
    ├── HostedRunner.cs            — runs an export through a TranspilationResult (used by --run path)
    ├── ImportDispatcher.cs        — DispatchProxy factory for IImports proxies; throws on missing handler by default, lenient: true opts out
    └── BindingLoader.cs           — IBindable assembly discovery (for --bind)
```

## Key invariants and constraints

- **`Wacs.Core` stays at `net8.0;netstandard2.1`** — runtime-side public
  surface. The transpiler-side projects (`Wacs.Transpiler.Lib`,
  `Wacs.Transpiler`, `Wacs.Console`, `Wacs.Transpiler.Test`,
  `Wacs.ComponentModel.Bindgen.Test`, `Wacs.Bench`) are at `net9.0` for
  `PersistedAssemblyBuilder`.
- **AOT compatibility is a hard requirement.** No runtime
  `Reflection.Emit`, no `MakeGenericMethod` outside source-generators or
  build-time code. The transpiler itself runs on the dev machine; the
  generated assembly must be NativeAOT-friendly. Components emitted with
  `Emission = AotLinked` can be dead-stripped of the codec stack.
- **Transpile-time validation > runtime validation.** `CilValidator`,
  `Debug.Assert` on `Value` construction, no sentinel types — the goal is
  every transpile failure surfaces with a (module, instruction, reason)
  triple at transpile time, never as `InvalidProgramException` at JIT.
- **Symmetric API surfaces across engines.** A consumer who writes against
  `WasmRuntime + ModuleInstance` (interpreter) can swap to a transpiled
  `Module` class without API churn — same `IExports`, same `IImports`,
  same memory model.
- **The text and binary parsers must agree.** A bug in `BinaryModuleParser`
  is mirrored in `TextModuleParser`; fixes go in the validator layer when
  the gap is value-range. The transpiler trusts whatever `ModuleInstance`
  the parse produces.

## Performance characteristics

Cold start of a 4 KB-data-segment saved `.dll`, measured by
`PeShapeTests.Diagnostic_ColdStartFromSavedDll`:

| Phase | Time |
|---|---|
| `AssemblyLoadContext.LoadFromAssemblyPath` | ~110 µs |
| Module ctor (cross-process codec decode + memory init + segment copy) | ~3.3 ms |
| First export call (read byte at offset 0) | ~150 µs |
| **Total cold** | **~3.6 ms** |

In-process transpile cold start is dominated by IL emission (Pass 2). For
larger modules (CoreMark scale) IL emission dominates over PAB
`Save`+`Load`. AotLinked emission elides the codec decode; for compute-only
fixtures the Module ctor drops to sub-100 µs.

### Auto vs Standard on fib(100) cold start

`Wacs.Bench.Coldstart` numbers (post-warmup median for steady-state,
first-trial for cold), µs:

| Phase | Standard | AotLinked (Auto-promoted) | Δ |
|---|---|---|---|
| Activate (first) | 1715 | 765 | **−55%** |
| Activate (steady) | 289 | 198 | **−32%** |
| TOTAL (first) | 1981 | 989 | **−50%** |
| TOTAL (steady) | 417 | 300 | **−28%** |

Saved `.dll` size on the same fixture: 4 608 → 4 096 bytes
(**−11%**) — the `__WACSInit` codec blob is gone.

### Phase breakdown of an AotLinked Module ctor (post-warmup, fib)

`PeShapeTests.Diagnostic_ProfileModuleCtor` decomposes the ctor.
For fib (1 export, no funcref tables) post-warmup:

| Phase | Time |
|---|---|
| AssemblyLoadContext.LoadFromAssemblyPath | ~15 µs |
| `RuntimeHelpers.CreateSpan<byte>` over `__WACSInit.Data` (Standard) | ~4 µs |
| `InitDataCodec.Decode(span)` (Standard, cross-process) | ~6 µs |
| `InitializeFromData` (Standard, cross-process) | ~5 µs |
| Activator.CreateInstance (full ctor) | ~150-300 µs |
| Residual (FuncTable + delegate shims + BindTableDelegates) | ~130 µs |

The codec stack is sub-30 µs even on the cross-process path under
Standard — what AotLinked saves is the **first-trial JIT** of those
methods, plus skipping their work entirely. The structural ~130 µs
residual is the same under both emissions (FuncTable population
is amortizable via static-template work that's been analyzed but
not landed).

## License

Apache 2.0 — see [LICENSE](https://github.com/kelnishi/WACS/blob/main/LICENSE).
