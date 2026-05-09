# Changelog

## WACS.Cli 1.5.6 — `--wasi-nn` skips legacy IBindable under `--wasip2` to close registry split

Pre-fix, `wacs run --wasip2 --wasi-nn` registered the WASI.NN
backend twice — once via `Wacs.WASI.NN.OnnxRuntime`'s `IBindable`
(which calls `WitBindings.Bind` → registers BindHostFunction
handlers + WASI.NN's internal `host.Graphs` / `host.Tensors` /
`host.Errors` resource registries) and once via the wasip2
RuntimeScope's `AutoRegisterOnnxBackend` (which wires the ONNX
backend into the DI bundle's `WasiNNConfiguration`, surfaced
through `WasiPreview2NNBundle`'s `IGraphFuncs` to the transpiler's
direct-link emit, with handles minted in `WasiPreview2Resources`).

The two registries hold the same `i32` handle namespace but no
bridge between them. A guest minting `wasi:nn/graph-funcs.load`'s
return handle through one path and looking it up later through
the other gets either `Resource handle N is not registered` (if
the lookup misses) or `Handle 0 is reserved as the null sentinel`
(if a default-init slot leaked through). The `wasi-nn-slm.wasm`
demo trips this between `load()` and
`graph.init_execution_context()`.

Fix per round-10's option (2): under `opts.Wasip2`, skip the
WASI.NN IBindable from the `ApplyBindings` path. The
`ReflectivelyAddWasiNN` flow already wires the ONNX backend to
the direct-link side; the IBindable's `WasiNNHost` (separate
`Graphs`/`Tensors`/`Errors`) is redundant and structurally
incorrect under wasip2. Interpreter-only `--wasi --wasi-nn`
(Preview 1 + WITX legacy ABI) keeps the IBindable since
direct-link isn't on its path.

```diff
-if (opts.WasiNN) paths.Add("Wacs.WASI.NN.OnnxRuntime");
+if (opts.WasiNN && !opts.Wasip2)
+    paths.Add("Wacs.WASI.NN.OnnxRuntime");
```

Verified by the round-10 follow-up probe (`/tmp/nn-probe`): the
30-line wasi-nn shim that calls `load()` then
`graph.init_execution_context()` traps pre-fix at the second call
("Resource handle 4 is not registered"); post-fix the
WitBindings registration doesn't happen and the direct-link path
mints the handle in `WasiPreview2Resources` where the lookup
finds it.

Out of scope (separate gap if it surfaces): a programmatic
embedder that wires both paths explicitly (not via the CLI) hits
the same registry split. A library-level `WasiNNHost
.SuppressWitBindings` opt-out is the natural follow-up but not
needed to close the SLM trip site.

## WACS 0.13.5 / WACS.Cli 1.5.5 / WACS.Transpiler.Lib 0.8.5 — direct-link emit accepts SourceGen-shape resource constructors

`Wacs.ComponentModel.Bindgen.SourceGen` emits resource constructors
as `void Create(args)` instance methods on the resource interface
(rather than static factories returning the interface). The
`Wacs.WASI.NN.DependencyInjection.Tensor` impl class follows that
contract — public parameterless ctor + `void Create(...)` for the
two-step `Activator.CreateInstance` then `Create` lift the canonical
ABI's `[constructor]X` calls into.

Pre-fix, `DirectLinkedImportEmit.cs:101` rejected this shape
(`if (!method.IsStatic) return false`). The constructor fell
through to legacy delegate dispatch, which never bound a real
handle for it, and the guest received 0 (the canonical-ABI null
sentinel). The first downstream `[method]X.<x>` call AVs the host
on `Resources.GetResource(typeof(IFace), 0)` — observed end-to-end
in the `wasi-nn-slm.wasm` SLM after the round-7+8 high-address
fixes unblocked it that far.

`HostPackageResolver` adds `TryFindResourceImpl(Type
resourceInterface, out Type implType)` that walks the loaded
host-package assemblies for a public class implementing the
interface with a public parameterless constructor. Cached per-
interface; first match wins (stable order across host packages).

`DirectLinkedImportEmit`'s constructor gate now accepts both
shapes:

- **Static factory** — existing path. Method is static, returns
  the interface, IL emits `Call → AllocateResource`.
- **Void instance method** — new path. Method is non-static and
  returns void, resolver finds an impl class. IL emits
  `Newobj <impl>; dup; stloc inst; castclass <iface>` before the
  arg lift loop, then the lift loop pushes args, then `Callvirt
  <Create>` (void), then `ldloc inst; ldarg ctx; ldfld Resources;
  ldtoken <iface>; call typeof; ldloc inst; callvirt
  AllocateResource → handle`.

Test surface: new
`DirectLinkedImportTests.DirectLinkedImport_SourceGenCtorThenInstance_AllocatesAndResolves`
defines `ISgWidget` (SourceGen-shape, with `void Create();
read: func() -> u32;`) plus `TestSgWidget` (parameterless ctor +
sentinel-recording Create). Wasm imports `[constructor]widget` +
`[method]widget.read`, calls them in sequence, asserts the
sentinel value (42) round-trips. Pre-fix the gate rejects the
SourceGen shape; post-fix the test passes.

Out of scope (separate work): `wasi-nn-slm.wasm` end-to-end
verification stays the user's call locally to avoid the round-4 /
round-6 overclaim pattern.

## WACS 0.13.4 / WACS.Cli 1.5.4 / WACS.Transpiler.Lib 0.8.4 — high-address bulk memory ops + MemSlice chokepoint

Round 7 closed `(int)ea` truncation in the load/store helpers
(`MemoryHelpers.{Load,Store}*`) but missed the bulk-op family and the
`[OpHandler]`-dispatch chokepoint. Both had the same shape and the
same crash mode — any guest writing to a memory address past
`int.MaxValue` AVs the host process. Rust's release-mode `vec![0u8; N]`
lowers to a single `memory.fill` after `cabi_realloc`, so non-trivial
allocations past 2 GiB trip it.

Migrated to the `nuint` overloads added in 0.13.3:

- `Wacs.Transpiler.Lib/AOT/Emitters/BulkEmitter.cs`
  `BulkHelpers.{MemoryCopy, MemoryFill, MemoryInit}` — widen
  `dst` (and `src` for the dst-side memory in MemoryCopy) to
  `nuint` at the start, route through `mem.AsSpan(nuint, int)`.
  `MemoryInit`'s `src` stays `int` (data segment is byte[]-bounded).
- `Wacs.Core/Wacs.Core/Instructions/MemoryHandlers.cs` `MemSlice`
  — single chokepoint for every `[OpHandler]` load/store dispatch.
  Last line `return mem.AsSpan((int)ea, width)` becomes
  `return mem.AsSpan((nuint)ea, width)`. This site was missed in
  round 7's per-instruction-file sweep.
- `Wacs.Core/Wacs.Core/Instructions/MemoryBulk.cs` —
  `InstMemoryInit.Execute` (line 235), `InstMemoryCopy.Execute`
  (line 389), `InstMemoryFill.Execute` (line 459) all switch
  guest-memory address args from `(int)x` to `(nuint)x`.

Test surface: 3 new [Fact]s in
`Wacs.Transpiler.Test.MemoryHelpersHighAddressTests` covering
`BulkHelpers.MemoryFill / MemoryCopy / MemoryInit` at
`addr = 0x80000400` (~2 GiB + 1 KiB) on a NativePointer
33000-page memory. Pre-fix each AVs; post-fix bytes round-trip.
Total in that suite: 8/8 (5 from gap 15 + 3 from gap 16).

Out of scope: atomics still pin `int ea` through abstract
`InstAtomicLoad.DoLoad` signatures. Same-shape gap, different
cohort. Follow-up.

## WACS 0.13.3 / WACS.Cli 1.5.3 / WACS.Transpiler.Lib 0.8.3 — high-address load/store on NativePointer memories

`MemoryHelpers.StoreI32` / `LoadI32` (and every load/store/narrow/F32/F64
sibling) cast the effective address to `int` on the final
`mem.RefAs<byte>(...)` / `mem.AsSpan(...)` call. With ea > `int.MaxValue`
— anything past 2 GiB into a NativePointer-backed linear memory —
that cast wrapped to a negative pointer offset; the kernel signaled
SIGSEGV and the .NET runtime aborted with `AccessViolationException`.
Bypassed managed exception handling, so the wasm-trap-to-exit-1
path didn't catch it.

The bounds check itself was correct (`ea` is `long` and compared
against `mem.ByteLength` which is `nuint`). Only the truncating cast
on the access call was wrong.

`MemoryInstance` adds `nuint` overloads alongside the existing `int`
ones:
- `RefAs<T>(nuint ea)` — `byte* + nuint` pointer arithmetic on
  `NativeBase`; ManagedArray branch keeps the safe `(int)ea` cast
  (Array.MaxLength bounds the byte[] backing ≤ 2 GiB).
- `AsSpan(nuint offset, int length)` — same shape for narrow
  load/store siblings (`StoreI32_8`, etc.).

Migrated call sites:
- `Wacs.Transpiler.Lib/AOT/Emitters/MemoryEmitter.cs`
  `MemoryHelpers` — every `(int)ea` cast (59 sites across i32/i64
  + every narrow variant + f32/f64) now passes `(nuint)ea`.
- `Wacs.Core/Wacs.Core/Instructions/Memory/{I32,I64,F}MemoryLoad.cs`
  + `Inst{I32,I64}Store.cs` + `FMemoryStore.cs` — interpreter
  per-instruction handlers had the same shape; now route through
  the `nuint` overloads.

Test surface: new
`Wacs.Transpiler.Test.MemoryHelpersHighAddressTests` covers
`StoreI32` / `LoadI32` / `StoreI64` / `LoadI64` / `StoreI32_8` +
`LoadI32_8U` / `StoreF32` / `LoadF32` / `StoreF64` / `LoadF64` at
`ea = 0x80000000 + 1024` (~2 GiB into the memory) on a NativePointer
33000-page (~2.0625 GiB) instance. Pre-fix every test AVs;
post-fix all five round-trip cleanly. `NativeMemory.AllocZeroed`
is lazy-zero on calloc so the 2 GiB virtual reservation does not
commit physical pages.

Out of scope (separate gap): atomics. `AtomicHelpers.CheckEa`
still returns `int`, and the `int ea` parameter cascades through
the abstract `InstAtomicLoad.DoLoad(ExecContext, int ea)` /
`InstAtomicStore.DoStore` signatures. Same shape as gap 15 but a
different cohort of guests (atomic-using shared-memory threading);
follow-up.

## WACS 0.13.2 / WACS.Cli 1.5.2 / WACS.Transpiler.Lib 0.8.2 / WACS.ComponentModel 0.3.2 — host paths route through MemoryInstance; retire byte[] pinning across canonical-ABI

NativePointer-mode memories carry an empty sentinel `Array.Empty<byte>()`
in `MemoryInstance.Data` so accidental `mem.Data[i]` access surfaces
loudly. Pre-fix, every host-side canonical-ABI path pinned that field
directly: the AotLinked active-data-segment install copied through
`BulkHelpers.CopySegmentToMemory(byte[] dst, …)`; the canonical-ABI
lower path called `Buffer.BlockCopy(value, 0, mem.Data, …)`; the lift
path read `_memory.Data[disc]` and passed `_memory.Data` to
`StringMarshal.LiftUtf8` and `ListMarshal.LiftPrim`. All AOORed in
NativePointer mode.

Routes every canonical-ABI host path through
`MemoryInstance.AsSpan(int, int)` (the existing mode-aware accessor)
so both `ManagedArray` and `NativePointer` backings work the same.
Helper signatures migrated from `byte[]` to `MemoryInstance`:

- `StringMarshal.LiftUtf8` / `LiftUtf16` / `LiftLatin1OrUtf16` / `CopyToGuest`
- `ListMarshal.LiftPrim<T>` / `LiftStringList` / `LiftStringListUtf16` / `CopyArrayToGuest<T>`
- `BulkHelpers.CopySegmentToMemory`
- `ModuleInit.CopyDataSegment` (interpreter active-segment install)

`PrimitiveStore` gains a reader sibling family — `ReadU8`, `ReadU16LE`,
`ReadU32LE`, `ReadI32LE` — used at IL emit time to decode disc bytes
and (ptr, len) header pairs. The scalar writer family
(`StoreI8` / `StoreU8` / `StoreI16` / … / `StoreBool`) now takes
`MemoryInstance` instead of `byte[]`.

Transpiled module class's `Memory` property changes type from
`byte[]` to `MemoryInstance`. Saved DLLs from v0.8.1 keep the old
shape; v0.8.2 generates the new shape. Consumers that read
`instance.Memory` directly need to update — `mem.Data` becomes
`mem.AsSpan(...)` for byte access.

IL emit sites in `DirectLinkedImportEmit` and `ComponentExportsEmit`
drop the `Ldfld MemoryInstance.Data` instruction at every helper
call site (the `MemoryInstance` is left on the stack instead) and
replace `BitConverter.ToInt32(byte[], int)` lookups with
`PrimitiveStore.ReadI32LE(MemoryInstance, int)`. Variant disc-byte
reads use `PrimitiveStore.ReadU8/U16/U32` instead of `Ldelem_U1`.

Test surface: new `data-segment-component` fixture (active data
segment + string return). `ComponentInstanceTests
.Component_data_segment_install_and_string_lift_under_storage`
covers the interpreter component path × `MemoryStorageMode`;
`ComponentTranspilerTests
.TranspileSingleModule_data_segment_install_and_lift_honor_storage`
covers `EmissionTarget × MemoryStorageMode` (4 cases). Both flavors
of guest-memory shape are exercised: segment install at module ctor
+ string lift on call.

Existing `StringMarshalTests` / `ListMarshalTests` updated to stage
inputs in a `MemoryInstance` rather than a bare `byte[]`.

Out of scope (separate gaps): `AtomicHelpers` (transpiler atomic
ops still pin `mem.Data` for `ref byte` semantics), MemoryInstance's
own `WriteInt32` / `WriteUtf8String` convenience methods (used by
WASI Preview1), and `Wacs.WASI.NN`'s `ExecContextExtensions`. Each
fails the grep'able `\.Data\b on MemoryInstance` invariant outside
the `MemoryInstance.cs` file in domains independent of canonical-ABI.

## WACS 0.13.1 / WACS.Cli 1.5.1 / WACS.Transpiler.Lib 0.8.1 / WACS.ComponentModel 0.3.1 — `--native-memory` honored on every component path

`--native-memory` was silently no-oped for component-mode runs:
the CLI pinned the storage mode but neither
`Wacs.ComponentModel.Runtime.ComponentInstance.Instantiate` (the
interpreter component path) nor
`ModuleClassGenerator.EmitMemoryArray` (the AotLinked emission)
read the pin. Components requesting more than the
`ManagedArray` ~2 GiB cap got `memory.grow → -1` regardless of the
flag.

The pin migrates from `Wacs.Transpiler.AOT.ModuleInit.CurrentMemoryStorage`
(only readable from the transpiler layer) to
`Wacs.Core.Runtime.AmbientRuntime.MemoryStorage` so every layer
above `Wacs.Core` shares one source of truth.

Reads added:
- `Wacs.ComponentModel.Runtime.ComponentInstance.Instantiate`
  (single-core and multi-core paths) constructs `RuntimeOptions`
  with `MemoryStorage = AmbientRuntime.MemoryStorage`.
- `Wacs.Transpiler.AOT.ModuleClassGenerator.EmitMemoryArray` emits
  `Ldsfld AmbientRuntime.MemoryStorage` before `Newobj` against
  the 2-arg `MemoryInstance(MemoryType, MemoryStorageMode)` ctor,
  so the runtime value of the pin reaches every memory the
  AotLinked path constructs.

Test surface: new `grow-memory-component` fixture (exports
`grow-big: func() -> s32` whose core does `(memory.grow 50000)`).
`Wacs.ComponentModel.Test.ComponentInstanceTests
.Component_memory_honors_AmbientRuntime_storage` exercises the
interpreter component path (returns -1 under ManagedArray, 1
under NativePointer);
`Wacs.Transpiler.Test.ComponentTranspilerTests
.TranspileSingleModule_memory_init_honors_AmbientRuntime_storage`
covers the cross-product of `EmissionTarget × MemoryStorageMode`.
`NativeMemory.AllocZeroed` is lazy-zero (calloc on Unix,
VirtualAlloc on Windows), so the 3 GiB virtual reservation does
not commit physical pages.

## WACS 0.13.0 / WACS.Cli 1.5.0 / WACS.Transpiler.Lib 0.8.0 / WACS.ComponentModel 0.3.0 / WACS.WASI.Preview2 0.4.0 / WACS.WASI.Preview1 0.13.0 / WACS.HostBindings.Abstractions 0.3.0 — Linear-memory storage modes, memory64, and component-model lift fixes

Lifts WACS's linear-memory backing to a host-selected mode and
plumbs that mode through every layer of the runtime, so the wasm32
4 GiB ceiling and memory64's 2^48 ceiling are both reachable.
Also closes two component-model lift correctness bugs that
surfaced under realistic host-side memory growth.

### Linear-memory storage modes

`MemoryInstance` carries two backings selected via
`RuntimeOptions.MemoryStorage`:

- **`ManagedArray`** (default): managed `byte[]` grown via
  `Array.Resize`. Capped at `Array.MaxLength` (~2 GiB).
- **`NativePointer`**: `byte* NativeBase` + `nuint NativeSize`
  allocated via `NativeMemory.AllocZeroed` (.NET 6+) or
  `Marshal.AllocHGlobal` + `InitBlockUnaligned` (legacy). Grow
  allocates a new buffer, `Buffer.MemoryCopy`s the live bytes,
  frees the old. Capped at `WasmMaxPages` (4 GiB) for memory32
  modules and `WasmMaxPages64` (2^48) for memory64.

New public surface on `MemoryInstance`:

- `StorageMode` (read-only) — which backing this instance uses.
- `byte* NativeBase` + `nuint NativeSize` — public for emit code.
- `nuint ByteLength` — authoritative byte length, both modes.
- `Span<byte> AsSpan(int offset, int length)` — mode-dispatched
  span access. The existing `[Range]` indexer also dispatches.
- `ref T RefAs<T>(int ea) where T : unmanaged` — mode-dispatched
  `ref T`. Atomic load/store/RMW routes through this so the same
  `Interlocked.*` / `Volatile.*` sites work on both backings.
- `IDisposable` — `Dispose` frees the native buffer in
  NativePointer mode, no-op in ManagedArray. A finalizer
  backstops native-mode leaks.

`RuntimeOptions.MemoryStorage` (default `ManagedArray`) flows from
`WasmRuntime.InstantiateModule` through to the `MemoryInstance`
ctor. Every interpreter memory-access site (the `MemSlice`
chokepoint covering 25+ `[OpHandler]` load/store/narrow/bulk
ops, the per-instruction memory load/store classes, bulk init/
copy/fill, SIMD v128, and atomics) dispatches through the
mode-aware surface. The transpiler's emit follows the same
pattern: `MemoryHelpers` and `BulkHelpers` take `MemoryInstance`
and dispatch per access; `MemoryEmitter` / `SimdEmitter` /
`BulkEmitter` pass `MemoryInstance` to the helpers directly;
`EmitMemorySize` uses `Call get_Size` instead of
`Ldfld Data; Ldlen`.

`ManagedArray` callers stay byte-stable; NativePointer is
covered by `MemoryInstanceNativeStorageTests` (allocation, grow
preservation + zero-fill, indexer + AsSpan parity, dispose
idempotency) and `MemoryNativePointerEndToEndTests` ([Theory]
cases running every memory op in both modes through real wasm
fixtures).

### memory64

memory64 modules (`(memory i64 N)`) execute end-to-end through
the interpreter and transpiler when paired with NativePointer.
Bounds checks use a wrap-safe unsigned form:

```csharp
if ((ulong)ea > (ulong)mem.ByteLength
    || (ulong)mem.ByteLength - (ulong)ea < (ulong)width)
    trap;
```

Negative `ea` casts to a huge ulong that fails the first
clause; `ea` near `ByteLength` fails via subtract-and-compare
without overflow risk. The check covers single-byte / narrow /
full-width loads and stores, SIMD, atomics, and bulk
init/copy/fill. `OpStack.PopAddr` no longer traps on negative —
memory64 addresses with the high bit set are valid wasm.
`InstTableGet` / `InstTableSet` also moved to unsigned compare
so table64 (`(table i64 …)`) indices behave correctly.

All four spec.test fixtures under `spec/test/core/memory64/`
pass on both the WAST and WAST-transpiled paths.

memory64 modules going through the AOT saved-DLL path
(`wacs aot --wasi`) work today only when the effective address
fits in int32 — the transpiler's emitted memory-op IL truncates
`(int)ea` at the AsSpan call site. Spec memory64 tests pass
because the test wat wraps to small `ea`; arbitrary >2 GiB
transpiled access does not. The interpreter and direct
`wacs run` paths are unaffected. `WacsHostMemory.AsSpan(int, int)`
is also int-bounded; host bindings reading >2 GiB views need a
future `MemoryHandle`-style API.

### `wacs run --native-memory`

`wacs run --native-memory model.wasm` (or
`--wasip2 --native-memory ...` for components) backs the
guest's linear memory with native-pointer storage. The flag
flips `RuntimeOptions.MemoryStorage` for the interpreter
`InstantiateModule` call and pins the static
`ModuleInit.CurrentMemoryStorage` (a new public field, default
`MemoryStorageMode.ManagedArray`) that the transpiler's
`InitializationHelper` reads when constructing transpiled
module classes. `ExecuteSingleCore` and `ExecuteComponent`
restore the prior values on exit so subsequent in-process
callers (test harnesses, library hosts) see the original mode.

### `WacsHostMemory` mode-aware

The host-binding ABI carries a NativePointer-mode case alongside
the managed `byte[]` case. Wasip1 hosts running with
`MemoryStorageMode.NativePointer` produce a `WacsHostMemory`
that dispatches reads and writes against native memory.

`Wacs.HostBindings.Abstractions.WacsHostMemory`:

- New `WacsHostMemory(IntPtr nativeBase, int length)` ctor.
  The struct tracks both backings (`byte[]? _data` +
  `IntPtr _nativeBase`) and dispatches every accessor through
  a null-check on `_data` — JIT inlines to a single branch per
  access.
- New `IsNative` property — true when the view is over native
  memory.
- All accessors (`ReadByte`/`WriteByte`/`AsSpan`/`ReadInt32LE`/
  `WriteInt32LE`/`ReadInt64LE`/`WriteInt64LE`/`Contains`/
  `WriteUtf8String`/`ReadUtf8String`/`ReadStruct`/`ReadStructs`/
  `WriteStruct`) work in either mode.
- `Data` getter still returns a `byte[]` for back-compat — but
  in NativePointer mode it returns `Array.Empty<byte>()`.
  Legacy callers that reach for `.Data` directly fail loud
  (AOOR on first index) instead of silently zero-reading; they
  should migrate to `AsSpan`.

`Wacs.WASI.Preview1.Clock.WacsHost` (the Preview1 ExecContext →
WacsHostMemory adapter) branches by `MemoryInstance.StorageMode`.
NativePointer-backed memories take the `(IntPtr, int)` ctor
with `(IntPtr)mem.NativeBase` and
`min(NativeSize, int.MaxValue)` length.

`Wacs.HostBindings.Test`: 14 tests (was 8). Six new cases
allocate via `NativeMemory.AllocZeroed`, exercise the
accessors, and free the buffer.

### wasip2 host bindings

The wasip2 host-binding stack threads `MemoryInstance` instead
of raw `byte[]` everywhere — about 30 helpers in `MemoryReader`
/ `MemoryWriter`, the `ExecContextExtensions` shortcuts, ~150
callsites across `SocketsBindings`, `FilesystemBindings`,
`HttpTypes`, `Cli`, `Clocks`, `Io`, and `Random`, plus 39
private `Write*` helpers in those binding files. Every read and
write goes through the mode-dispatching `mem.AsSpan(...)` /
`mem.RefAs<T>(...)` / `mem.ByteLength` surface, so a wasip2
binding works against either backing without per-binding
awareness.

API changes:

- `MemoryReader.{ReadUtf8String, ReadByteArray, ReadByteArrayList,
   ReadI32LE, ReadU16LE, ReadU32LE, ReadU64LE}`: `byte[] memory`
  → `MemoryInstance memory`.
- `MemoryWriter.{WriteI32LE, WriteU16LE, WriteU32LE, WriteU64LE,
   WritePrimitiveLE, WriteResultUnitOk, ZeroRange}`: same.
- `MemoryWriter.WriteUtf8StringAllocated` / `WriteOptionString`:
  `Func<byte[]> getMemory` → `MemoryInstance memory`. Callers
  no longer need to model the post-`cabi_realloc` re-fetch —
  `mem.AsSpan` reads the live backing on each access.
- `ExecContextExtensions.Memory(this ExecContext ctx)`: returns
  `MemoryInstance`, not `byte[]`. Callers passing `ctx.Memory`
  as a method group invoke it as `ctx.Memory()`.

Per-binding-file changes follow a uniform pattern: `mem[ptr]`
→ `mem.AsSpan(ptr, 1)[0]`; `Array.Copy(src, X, mem, Y, len)`
→ `new ReadOnlySpan<byte>(src, X, len).CopyTo(mem.AsSpan(Y, len))`;
`Encoding.UTF8.GetString(mem, ptr, len)` →
`Encoding.UTF8.GetString(mem.AsSpan(ptr, len))`.

`ErrorCodeEncoderTests`'s `BumpAllocator` test fixture wraps a
real `MemoryInstance` (1-page) instead of a raw `byte[]`;
assertions go through a thin indexer/Span helper.

### Component-model lift fixes

Two correctness bugs in `DirectLinkedImportEmit`:

**Records with `option<X>` fields.**
`wasi:filesystem/types.descriptor.stat` returns
`result<descriptor-stat, error-code>`, where descriptor-stat is
`record { type, link-count, size, opt<datetime>×3 }`. The
predicate path rejected this record because
`IsRecordOfPrimitives` walked fields with the non-resolver
`IsFlatField` and bailed on the option fields; direct-link emit
fell back to the `IImports` proxy and the proxy returned a
default-zero `DescriptorStat`. Resolver-aware
`IsFlatField(t, resolver)` now accepts `Option<X>` whenever
`IsAggregateReturnSupported(t, resolver)` recognizes the
Option's wire form. `IsAggregateReturnSupported`'s record +
tuple branches use the resolver-aware predicate so option
fields cascade through. `EmitTupleOrRecordFieldStore`
dispatches Option fields to `EmitOptionStoreAt` with a per-
field base-address local. `MaxFieldAlign`, `AlignOfFlatField`,
`SizeOfFlatField`, `SizeOf` pick up Option-aware overloads so
per-field offsets align on the inner type's `MaxAlignOf`.

E2E coverage: new `E2E_DescriptorStat_RecordWithOptionFields`
exercises a stub `IDescriptor.Stat()` returning a known `Size`
through the `wasi-fs-stat-component` fixture.

**`cabi_realloc`-driven `memory.grow` invalidates captured byte[].**
`MemoryInstance.Grow` does `Array.Resize(ref Data, …)`, which
reallocates the backing `byte[]`. Every helper in
`PrimitiveStore` captured `byte[] dest` BEFORE calling
`cabi_realloc`, so the post-realloc copy targeted the stale
(pre-grow) array's `int Length`, throwing AOOR for any
allocation that crossed a page boundary. Rust std hid the trap
behind "out of memory" because `fs::read` loops on `read(buf)`
past 24 KiB.

Every cabi_realloc-using helper (`StoreString` /
`StoreStringUtf16` / `StoreStringLatin1OrUtf16` /
`StoreByteArray` / `StorePrimitiveArray<T>` /
`StoreByteArrayList` / `StorePrimArrayList<T>` /
`StoreStringList` / `StoreListOfStringList` /
`StoreListOfByteArrayList`) now takes `MemoryInstance mem`
instead of `byte[] dest` and reads `mem.Data` per access.
`mem.Data` is read AFTER each cabi_realloc, so writes target
the post-grow array. The fixed-width primitive helpers
(`StoreI8` / `StoreU8` / … / `StoreBool`) still take
`byte[] dest` — they have no cabi_realloc and no grow risk.

`DirectLinkedImportEmit`'s emit sites split into two prefixes:
variable-length helpers receive `MemoryInstance`, fixed-width
helpers receive `byte[] dest` as before. The split runs through
the top-level dispatch, `EmitTupleOrRecordFieldStore`,
`EmitOptionStoreAt`, `EmitVariantStoreAt`, and
`EmitResultArmStore`.

Regression coverage: new `PrimitiveStoreGrowTests` (3 cases):
byte[] across grow, string across grow, byte[][] with
mid-iteration grow. The cabi_realloc lambda calls
`mem.Grow(...)` to mirror Rust std's growing realloc.

### Tests

| Suite | Total | Notes |
|---|---|---|
| Wacs.Core | 394 | +31 (allocation/grow units, [Theory] mode pairs, memory64 fixtures, atomic round-trips) |
| Wacs.Transpiler | 752 | +1 e2e (DescriptorStat record-with-options) |
| Wacs.ComponentModel | 350 | +3 (PrimitiveStoreGrowTests) |
| Wacs.WASI.Preview2 | 189 | unchanged (BumpAllocator fixture rewritten over MemoryInstance) |
| Wacs.WASI.Preview1 | 72 | unchanged |
| Wacs.HostBindings | 14 | +6 (NativePointer-mode WacsHostMemory accessors) |
| Spec.Test | 770/772 | +8 (4 memory64 + 4 table64 fixtures) |

## WACS.Transpiler.Lib 0.7.3 / WACS.Cli 1.4.1 / WACS.WASI.Preview2 0.3.1 / WACS.WASI.Preview2.DependencyInjection 0.1.1 — gap 9: preopens reach the wasip2 transpiler engine

Closes the gap that prevented `wacs run --wasip2 -d models repro.wasm`
(reading `/models/x.txt`) from succeeding under the transpiler
engine. The reproducer now runs end-to-end:

```
$ wacs run --wasip2 -d models preopen-repro.wasm
got: hi
```

Two layered fixes:

1. **WACS.Transpiler.Lib (`DirectLinkedImportEmit`)**: extends the
   per-field aggregate emit to recognize tuple/record fields that
   are resource interfaces (`own<R>`) alongside the existing
   primitive / string / byte[] cases. New shape covered:
   `list<tuple<own<R>, string>>` (the gap-9 reproducer's
   `wasi:filesystem/preopens.get-directories` return) and the
   broader env/args/headers/accept "list of (resource, label)"
   shape class. Per-element wire layout: handle@+0 (i32, 4B) +
   string-ptr@+4 (i32, 4B) + string-len@+8 (i32, 4B) for the
   gap-9 shape; per-element store dispatches per-field —
   `ctx.Resources.AllocateResource(typeof(IRes), value) +
   StoreI32` for `own<R>`, `cabi_realloc + StoreString` for
   strings, primitive `StoreXxx` for primitives. Resolver-aware
   variants of the predicates (`IsTupleOfFlatFields`,
   `IsRecordOfFlatFields`, `SizeOfFlatField` overload,
   `IsFlatField` overload) keep the existing primitive-only
   paths byte-stable; only the list-of-aggregate path consults
   the resolver.

2. **WACS.WASI.Preview2.DependencyInjection
   (`WasiPreview2RuntimeScope`)**: new one-shot owner of the DI
   scope that binds the wasip2 host package against the
   transpiler runtime. Auto-detects WASI.NN.DI and registers the
   composite `WasiPreview2NNBundle` whenever both packages are
   on the load path — required because the transpiler emits its
   direct-link IL against the composite type at transpile time;
   handing back the base bundle here trips
   `InvalidCastException` at the first import call. Embedders
   that want preopens hand them in via the scope's `preopens`
   parameter instead of re-implementing `IPreopens` +
   `services.AddSingleton`.

3. **WACS.WASI.Preview2 (`Preopens`)**: restored
   `Preopens(IEnumerable<(string hostPath, string guestPath)>)`
   ctor so the scope can build a `Preopens` instance from any
   iterable mount-pair source.

4. **WACS.Cli (`RunHandler`)**: the `--dir` flag now accepts the
   wasmtime-style `host::guest` mount-pair syntax in addition to
   the bare-path form. Validation checks the host-path side
   only. The wasip2 path constructs a `WasiPreview2RuntimeScope`
   inside `configureImports` so the bundle the transpiler
   receives is the same one the run uses.

5. **WACS.Transpiler.Lib (`ComponentMainHost.Run`)**: accepts
   optional `prebuiltBundle` / `prebuiltResources` parameters so
   the run path can hand off the scope's bundle/resources
   directly. Saved-dll `Program.Main` IL keeps the old reflective
   fallback (passes `null` / `null`).

Verification:
- 750/751 (1 SKIP) Wacs.Transpiler tests pass — including a new
  `E2E_Preopens_GetDirectories_ListResourceStringTuple` E2E test
  that transpiles `Spec.Test/components/fixtures/wasi-preopens-component`
  with a 3-entry `IPreopens` stub and verifies `count` returns 3.
- 347 ComponentModel + 189 Preview2 + 18 WASI.NN + 72 Preview1 +
  355 Core + 13 Bindgen + 8 HostBindings tests pass.
- End-to-end: `wacs run --wasip2 -d models repro.wasm` reads
  `/models/x.txt` cleanly; hello-wasip2 unchanged.

## Spec.Test fixtures — WASI 0.2.3 → 0.2.8 bump

Bumps the `Spec.Test/components/wasi-cli` submodule pointer from
v0.2.3 to v0.2.8, and propagates the version bump across every
fixture and test asserton:

- 168 fixture WIT files: `@0.2.3` → `@0.2.8` in package /
  use / import declarations.
- 68 fixture WAT files: `@0.2.3` import strings updated.
- 101 committed `<fixture>/wasm/<base>.component.wasm` binaries
  regenerated via `Spec.Test/components/build_fixtures.sh`.
- Hello-world reference (12 files, 9 with `v0_2_3`-baked
  filenames) regenerated via
  `Spec.Test/components/build_hello_world_reference.sh` with
  `wit-bindgen-cli 0.30.0` (the pin).
- 7 test C# files: `@0.2.3` / `0.2.3` / `v0_2_3` → `@0.2.8` /
  `0.2.8` / `v0_2_8` in fixture-loading assertions, package-name
  constructor calls, and reference-filename strings.

Net delta: 324 files changed, 398+ / 1777-. The size asymmetry is
the regenerated wasm binaries — `wasm-tools` 1.221's encoder packs
slightly tighter than the originals were (no semantic difference;
the .wat / .wit are byte-stable input → byte-stable output for any
given tool version).

The runtime-side WACS.WASI.Preview2 was already at 0.2.8 (PR #120);
this brings the test fixtures into alignment, retiring the
"deliberately decoupled" caveat in the README.

## [WACS.WASI.Preview2 0.3.0] — Bundled WIT bumped to WASI 0.2.8

Refreshes the vendored WIT tree under `Wacs.WASI.Preview2/wit/`
from upstream `WebAssembly/wasi-cli@v0.2.8` (latest stable patch,
released after v0.2.3 with zero ABI changes — only doc clarifications
and version-string bumps in `use` clauses). All hardcoded
`wasi:*@0.2.3` strings in the per-subsystem `*Bindings.cs` files
update in lockstep.

The 0.2.3 → 0.2.8 delta is purely cosmetic at the wire level — the
Component Model spec stabilizes minor revisions of WASI, so guests
compiled against any 0.2.x version bind to this version-tolerantly.
What changes: the version annotation in error messages, the strings
`wacs inspect --imports` reports, and the canonical `[WitSource]`
package identity the source-gen emits.

The `Spec.Test/components/wasi-cli` submodule and the test fixtures
under `Spec.Test/components/fixtures/` stay pinned at v0.2.3 — they
exercise the loader/emitter against a specific frozen version. The
two coordinates are deliberately decoupled.

## [WACS.Core 0.12.2] — Version-tolerant GetBoundEntity

Mirrors PR #119's `HostPackageResolver.TryResolve` fallback for the
interpreter path: when an exact `(module, entity)` lookup misses,
strip the trailing `@<version>` and try again, then fall back to an
O(n) scan over all keys for any matching the same stripped module
+ entity. Lets guests built against newer WASI patch revisions
bind to host packages registered against older ones (or vice
versa), since wasm Component Model treats minor revisions of WASI
as ABI-stable.

## [WACS.ComponentModel 0.2.0] — WIT parser accepts pre-release semver tags

`WitLexer` now emits dedicated `Dash` and `Plus` tokens (only when
not part of `->` or kebab-case identifiers). `WitParser.ParseSemver`
consumes them as the optional pre-release / build suffixes per
semver, populating `WitVersion.Prerelease` / `Build`.

Closes the `wasi:nn@0.2.0-rc-2024-10-28` (and any future rc-tagged)
WIT package's "unexpected character '-'" failure path. Unblocks the
SourceGen-driven host-interface emission for wasi-nn (see
WACS.WASI.NN 0.3.0).

## [WACS.WASI.NN.DependencyInjection 0.2.0] — Concrete resource impls

Replaces the GraphStub / ErrorStub placeholders with real resource
implementations (`Tensor`, `Graph`, `GraphExecutionContext`, `Error`)
of the source-gen interfaces. Each class has a parameterless ctor
so the canonical-ABI resource-construct lift can
`Activator.CreateInstance` it; instance methods either route to the
backend SPI (`Graph` → `IBackendGraph`, `GraphExecutionContext` →
`IBackendContext`) or hold pure state (`Tensor`, `Error`).

`GraphFuncsImpl.Load` / `LoadByName` now return real `Graph`
instances; `Graph.InitExecutionContext` mints a real
`GraphExecutionContext`; `compute` bridges between the wasi-nn
resource handles and the backend SPI's `NamedTensor` values
(copying output bytes so the resource handle owns its data
independent of the next compute).

Smoke tests in `Wacs.WASI.NN.Test/DependencyInjectionResourceTests`
cover the round-trip + double-construction + access-before-construct
guards.

The remaining piece for the SLM workload's transpiler-direct-link
path is multi-bundle wiring in `ComponentMainHost`: the existing
ctor-arity-based emit assumes a single `object hostBundle` slot,
so a component importing both `wasi:cli/*` (Preview2) and
`wasi:nn/*` can't yet have both bundles wired through one slot.
The resolver's `bundleType` parameter takes a single type today;
extending to a composite bundle (or `Type[]`) is the open work.

## [WACS.WASI.NN.DependencyInjection 0.1.0] — WasiNNBundle scaffolding

New package mirroring `Wacs.WASI.Preview2.DependencyInjection`. Ships
the `WasiNNBundle` that the transpiler's `HostPackageResolver`
direct-links wasi-nn's stateless `graph.load` /
`graph.load-by-name` against, plus
`services.AddWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new
OnnxBackend()))` for DI registration.

`GraphFuncsImpl` is the concrete `Nn.IGraphFuncs` implementation —
delegates to the configured `WasiNNConfiguration` backends (same
registry the interpreter binding consults). `Result<IGraph,
IError>` returns route through `GraphStub` / `ErrorStub`
placeholders that satisfy the type contract.

The resource-method-direct-link (`graph.init-execution-context`,
`tensor.constructor`, `inference.compute`) is the next deferred
chunk — the `GraphStub.InitExecutionContext` returns
`unsupported-operation` with a clear "wait for the resource-impl
PR" message rather than silently mis-dispatching. Resource methods
on the interpreter `BindToRuntime` path continue to work via the
hand-written `WitBindings` today.

## [WACS.WASI.NN 0.3.0] — Source-gen [WitSource] interfaces

Wires `Wacs.ComponentModel.Bindgen.SourceGen` against
`wit/wasi-nn.wit`, producing `[WitSource]`-decorated interfaces
under `Wacs.WASI.NN.Nn.{Errors, Graph, Inference, Tensor}`. The
transpiler's `HostPackageResolver` discovers these to direct-link
component-model wasi-nn imports — symmetric with how
`Wacs.WASI.Preview2` wires its hand-migrated subsystems.

The hand-written `WitBindings` continues to own the interpreter-
side `BindHostFunction` wiring; the generated interfaces feed the
transpiler-direct-link path on the wasip2 component path.

## [WACS.WASI.Preview2 0.2.0] — WasiPreview2Host composite + UseWasiPreview2 extension

`WasiPreview2Host` is the interpreter-side composite that wires every
sub-binding (random, clocks, io, streams, cli, filesystem, optionally
sockets + http) onto a `WasmRuntime` from one shared
`ResourceContext`. Symmetric with `WasiNNHost` — interpreter
consumers no longer thread the resource context through eight
separate `BindToRuntime` calls.

`runtime.UseWasiPreview2(b => b.WithStdout(...).EnableSockets())` is
the matching one-liner. Default posture matches Wasmtime: host
clocks/random/cli stdio + sandboxed-no-fs are wired, sockets and http
require explicit opt-in. The
`Wacs.WASI.Preview2.DependencyInjection` bundle path remains the
perf-optimized (transpiler direct-link) wiring.

## [WACS.Cli 1.4.0] — Component-mode ergonomics: auto-dispatch + --bind + --wasi-nn

`wacs run --wasip2 my.component.wasm` now starts a stock command
component without `--call`. The CLI looks for the canonical
`wasi:cli/run@<version>#run` export (matched via the new
`[WasmName]` round-trip attribute) and dispatches it automatically;
falls back to `_start`, then to a helpful error listing the
available exports. Aligns with wasmtime / jco / wasmer behavior for
stock command components.

`--bind <asm>` is now honored on the component paths
(`ExecuteComponent` + `ExecuteComponentTranspiled`), not just on the
core paths. Custom IBindable host packages can satisfy component
imports the same way they do for core modules. On the
component-transpiler path bindings run AFTER the default trap-stub
registration so `--bind` overrides cover the imports they care about.
`--bind` accepts both file paths and assembly names (resolves via
`Assembly.LoadFrom` / `Assembly.Load`, mirroring `--host-package`).

`--wasi-nn` shorthand: equivalent to
`--bind Wacs.WASI.NN.OnnxRuntime`. The DLL is bundled with the CLI
(via `ExcludeAssets="compile"` like Preview2) so it resolves out of
the box. For other backends (MLNet, LlamaSharp), pass the package
name through `--bind` directly.

## [WACS.WASI.NN 0.2.0] — IBindable + UseWasiNN extension

`WasiNNHost` now implements `IBindable` (it already exposed
`BindToRuntime(WasmRuntime)` — declaring the interface is
truth-in-advertising). Lets it ride the `--bind` discovery path.

New `runtime.UseWasiNN(b => b.AddBackend(GraphEncoding.ONNX, new OnnxBackend()))`
extension method. Replaces the
config → host → BindToRuntime sequence with the same shape we want
across the WASI host family.

## [WACS.WASI.Threads 0.2.0] — IBindable polish for symmetry

- Tagged `[assembly: WasiHostPackage]` so
  `runtime.AutoDiscoverHostPackages()` finds it alongside the
  other tagged WASI packages.
- New `runtime.UseWasiThreads()` extension method — one-liner
  symmetric with `UseWasiPreview2` / `UseWasiNN`.
- New `--wasi-threads` CLI flag (shorthand for
  `--bind Wacs.WASI.Threads`); the package is bundled with the
  CLI so the flag resolves out-of-box.

`WasiThreads` already implemented `IBindable` with a parameterless
ctor, so `--bind Wacs.WASI.Threads` worked before this change.
This is consistency polish across the WASI host family.

## [WACS.WASI.NN.MLNet 0.2.0] — Parameterless WasiNNMLNetBindable for --bind

Adapter exposing a parameterless ctor that pre-registers the
ML.NET-flavored ONNX backend. `--bind Wacs.WASI.NN.MLNet` activates
it via `BindingLoader`, identical shape to the OnnxRuntime adapter.
Tagged `[assembly: WasiHostPackage]` for `AutoDiscoverHostPackages`.

## [WACS.WASI.NN.LlamaSharp 0.2.0] — Parameterless WasiNNLlamaSharpBindable

Adapter for the GGUF / LlamaSharp backend with environment-variable-
driven name registry. Set `WACS_WASINN_GGUF_DIR=/path/to/models` and
every `*.gguf` file in that directory is registered under its
filename-sans-extension. Empty registry is fine — guests calling
`load-by-name` get `NotFound` rather than a trap.

`--bind Wacs.WASI.NN.LlamaSharp` activates it for guests using
`graph-encoding.ggml`. For richer registries (HF cache scan,
per-model `ModelParams`, custom paths), embedders should construct
`LlamaSharpBackend` directly via `runtime.UseWasiNN(b => b.AddBackend(...))`.

Tagged `[assembly: WasiHostPackage]`.

## [WACS.WASI.NN.OnnxRuntime 0.2.0] — Parameterless WasiNNOnnxBindable for --bind

Adapter exposing a parameterless ctor that pre-registers the ONNX
backend. `BindingLoader.LoadFromAssembly` activates it
automatically, so `wacs run my.wasm --wasip2 --bind Wacs.WASI.NN.OnnxRuntime`
(or the new `--wasi-nn` shorthand) is the whole story for stock
ONNX components — no per-consumer shim DLL.

## [WACS.HostBindings.Abstractions 0.2.0] — `[WasmName]` + `[WasiHostPackage]`

`[WasmName(string)]` carries the original wasm name on
auto-generated IExports/IImports methods. Round-trips a sanitized
CLR identifier (`wasi_cli_run_0_2_0_run`) back to its wasm form
(`wasi:cli/run@0.2.0#run`) for dispatch and diagnostics. Stamped
automatically by the WACS interface generator; hand-written types
implementing those interfaces don't need to apply it.

`[assembly: WasiHostPackage]` flags an assembly as auto-discoverable
by the runtime's host-package scan
(`runtime.AutoDiscoverHostPackages()`). Pairs with
`runtime.UseHostPackages(name1, name2, …)` for the explicit-list
shape. Either path activates every `IBindable` with a parameterless
ctor that the tagged assembly ships.

## [WACS.Transpiler.Lib 0.7.2] — `[WasmName]` emit, ComponentMainHost auto-resolve, BindingLoader name resolution

`InterfaceGenerator` stamps `[WasmName]` on every IExports / IImports
method, preserving the original wasm name through CLR-identifier
sanitization. Survives Reflection.Emit and PersistedAssemblyBuilder
paths; still dropped by Lokad.ILPack saved-dll output (a
follow-up).

`ComponentMainHost.Run` now accepts a null `exportName` and
auto-resolves `wasi:cli/run@<version>#run` via `[WasmName]` before
falling back to `_start`. Used by the `wacs run --wasip2`
component-command auto-dispatch path.

`BindingLoader.LoadFromAssembly(string)` now accepts either a file
path (`Assembly.LoadFrom`) or an assembly name (`Assembly.Load`),
matching `ResolveHostPackages` so `--bind` and `--host-package` have
identical resolution semantics.

New `WasmRuntime.UseHostPackages(name1, name2, …)` and
`WasmRuntime.AutoDiscoverHostPackages()` extension methods: the
explicit-list and AppDomain-scan shapes for ergonomic IBindable
wire-up. The scan uses the new `[WasiHostPackage]` assembly
marker.

## [WACS.Transpiler.Lib 0.7.1] — Re-instantiation restores dropped active data segments

Each Module instance's ctor copies active data segments from the
process-wide `ModuleInit` registry, then drops them per spec §4.5.4 so
later `memory.init` calls observe an empty segment. The drop turns the
dict entry into an empty array (not a removal) — fine for instance 1,
broken for instance 2: `CopyDataSegment` reads the empty entry and
memory comes up zeroed. Surfaces whenever a transpiled Module class
gets multiple `Activator.CreateInstance` calls in the same process.

`InitializationHelper.InitializeCore` step 4a now restores from
`ModuleInitData.SavedDataSegments` (already populated in step 6 of the
first init) when the live registry entry is empty. Adds a
`ModuleInit.RestoreDataSegment` overwriting variant —
`RegisterDataSegmentAt` is no-op-on-collision by design (cross-process
AotLinked path) and would skip the empty-entry case otherwise.

The "multi-memory bug" investigation that surfaced this: the
interpreter's binary parser handles multi-memory + active-data-with
-explicit-memidx (DataFlags=2) correctly — verified against
hand-encoded bytes byte-identical to wat2wasm output, plus all 32
multi-memory spec wast fixtures. The actual gap was on the transpiler
side and not multi-memory-specific. The new
`AotLinkedSupportsActiveDataWithExplicitMemIdx` test exercises both
the per-memidx routing and the re-instantiation path; the stale
"interpreter gap" comment in `AotLinkedSupportsMultiMemory` is gone.

## [WACS.Cli 1.3.0 + WACS.Transpiler.Lib 0.7.0] — PersistedAssemblyBuilder, RVA-mapped data, EmissionTarget.Auto

The transpiler retires `Lokad.ILPack 0.3.1` for the .NET 9+
[`PersistedAssemblyBuilder`](https://learn.microsoft.com/dotnet/fundamentals/runtime-libraries/system-reflection-emit-persistedassemblybuilder).
Lokad NRE'd on `Ldtoken` of any field created via
`DefineInitializedData`, which had been blocking RVA-mapped data
segments end-to-end. With PAB, that path works.

### RVA-mapped WASM data segments

WASM data segment bytes are now stored as RVA-mapped initialized data
in the emitted PE — bytes live in the `.sdata`/`.rdata` section,
demand-paged from disk by the OS loader, surfaced zero-copy as
`ReadOnlySpan<byte>` via `RuntimeHelpers.CreateSpan<byte>`. The
serialized codec blob (`__WACSInit.Data`) that bridges
saved-and-reloaded modules' empty registry state is RVA-mapped too.
Net effect: the compressed-segment + base64-in-`#US` path the prior
transpiler used is gone. Smaller PEs (~62.5% smaller blob storage on
data-segment-heavy modules), cold start that doesn't pay for a
`Convert.FromBase64String` over the whole codec.

### `EmissionTarget.Auto` is the new default

`AotLinked` emission inlines the `ThinContext` ctor as IL constants
and skips the codec stack entirely. v0.5 introduced it as an opt-in;
v0.7 widens its supported envelope (multi-result indirect dispatch,
multi-memory, exception tags, passive data + element segments,
imported functions) and turns on **`EmissionTarget.Auto`**, which
promotes feasible modules to `AotLinked` and falls back to `Standard`
for shapes outside the conservative envelope. Cuts first-trial cold
start by ~50% on promoted modules. Consumers that need codec
semantics (cross-process registry hint, etc.) can pin
`EmissionTarget.Standard`; consumers willing to fail loudly on
unsupported shapes can pin `EmissionTarget.AotLinked`.

### `ImportDispatcher` throws on missing handlers

Previously, `ImportDispatcher.Create` would silently default-return
when a wasm import had no matching `IImports` member; v0.7 throws
`InvalidOperationException` by default so missing wires fail at
construction time. The lenient default-return behavior is still
available via `ImportDispatcher.Create(..., lenient: true)`, which
`ComponentMainHost` keeps using because component-mode imports often
land via a different code path.

### `wacs aot` cross-csproj fix

`wacs aot` produces a host csproj that statically references the
transpiled `.dll`. PAB stamps the saved DLL's corelib AssemblyRef as
`System.Private.CoreLib` (the runtime-impl identity), but the C#
compiler resolves base types from the ref-pack `System.Runtime` —
without intervention, the host csproj trips CS0012 at compile time.
A new
[`CoreLibAssemblyRefRewriter`](Wacs.Transpiler.Lib/AOT/CoreLibAssemblyRefRewriter.cs)
post-processes a copy of the baked bytes at `SaveAssembly` time,
swapping the AssemblyRef name + PKT in place; type-forwards keep
runtime semantics intact. The rewriter file documents the rationale,
the byte-level edits, the one known limitation (generic-instantiation
FieldRefs across the renamed boundary in isolated ALCs), and the
two upstream conditions under which the hack can be deleted.

`Wacs.Transpiler.Lib` and `Wacs.Console` move to `net9.0`. `Wacs.Core`
remains `netstandard2.1` so embedders on Unity / Godot / older .NET
keep working unchanged.

## [WACS 0.12.1 + WACS.WASI.Preview1 0.12.0] — WAT parser parity, wasm-3.0 spec tip, wasi-testsuite Phase 4

### Wacs.Core 0.12.0 — in-process WAT/WAST parser at full parity

Every wast in the WebAssembly spec testsuite (SIMD, GC, relaxed-SIMD,
hex-float edge cases) round-trips identically through both the binary
and the in-process WAT/WAST pipelines. CI no longer shells out to
`wasm-tools` / `wast2json` to convert .wast fixtures to binary before
running them.

Highlights:

- WAT parser: full 237/237 instruction-dispatch coverage (GC + SIMD).
- Hex-float precision matches the binary parser bit-for-bit.
- Inline `(table funcref (elem $f …))` aligns with wabt.
- WAST runner: `assert_trap (module …)` + the various `ExnNN` shapes
  pass through the same module-instantiation hooks as binary fixtures.
- `BinaryModuleParser` no longer carries cross-parse static state.

### Wacs.Core 0.12.1 — wasm-3.0 spec submodule tip d7aada5

Tracks the upstream `WebAssembly/spec` submodule to commit `d7aada5`,
picking up:

- Inclusive memory page-count limit (PRs #105/#106/#108).
- Tail-call to imported (host) functions (#1872).
- `array.new_data` bounds (#1881).
- Malformed memop reserved bits (#1886/#1936).
- table64 unsigned u64 literal parsing + K dispatch (#104).
- `(module definition …)` validate-only support.
- u32 offset enforcement on memory32 load/store.
- `;;` line-comment CR termination.

### WACS.WASI.Preview1 0.12.0 — wasi-testsuite Phase 4 (43 → 67 of 72)

Lifts 23 fixtures across six subphases (PR #101):

- Phase 4.1 — symlink behavior (lifts 6 fixtures).
- Phase 4.2 — trailing-slash semantics + `path_link` no-follow.
- Phase 4.3 — `fd_readdir` synthesizes `.` and `..`.
- Phase 4.4 — fd-on-dir + preopen errno alignment (4 fixtures).
- Phase 4.5 — rights / lifecycle / timestamp fixes (2 fixtures).
- Phase 4.7 — directory rights split + `path_open` hardening.

## [WACS 0.11.0 + WACS.Transpiler.Lib 0.6.0] — Branch hinting

Wires the WebAssembly [Branch Hinting](https://github.com/WebAssembly/branch-hinting)
proposal end-to-end:

- **WACS 0.11.0** parses the `metadata.code.branch_hint` custom
  section into `Module.BranchHints` (a `(funcIdx → byte_offset →
  BranchHint)` map). The full payload is retained verbatim including
  the length-prefixed data vector so future revisions to the
  proposal can extend the hint encoding without a parser change.
  Every parsed instruction inside a function body now carries its
  body-relative byte offset on `InstructionBase.ByteOffsetInFunc` —
  the lookup key against the hint map.

- **WACS.Transpiler.Lib 0.6.0** consumes the hints in two emission
  shapes:
    * `if`-with-`else` hint=unlikely → `EmitIf` swaps the test
      (`Brtrue then_label` instead of `Brfalse else_label`) and
      emits the else-arm as the hot fall-through with the then-arm
      as the cold side-jump.
    * `if`-without-`else` hint=unlikely → new `_coldTailEmissions`
      mechanism on `FunctionCodegen` lifts the body out of the
      linear flow entirely. Hot path is `Brtrue cold_label;
      <fall-through>`; cold body is emitted between the function
      body's terminator and the funcEndLabel mark, with a back-jump
      to the if's endLabel to resume normal flow. Non-reducible CFG;
      RyuJIT and ILC handle it.

  Optimistic per design: the IL expresses the hint via ordering and
  branch sense. We don't claim downstream JIT/AOT honors it beyond
  what its own block-layout pass already does (RyuJIT tier-1 will
  eventually overrule us with profile data anyway). The bet pays
  off most for `wacs aot` / NativeAOT cold paths where there's no
  profile data to rely on.

The README's Branch Hinting feature row updates from "Custom section
ignored" to describe the new transpiler integration.

Validation is intentionally permissive ("optimistic"): the parser
rejects duplicate `(funcidx, offset)` entries and out-of-range
funcidx, but does NOT cross-validate that each hint's target offset
lands on an `if`/`br_if` instruction. Consumers can re-check at
use site if they need stricter semantics.

## [WACS.Cli 1.2.0 + WACS.WASI.Preview1 0.11.0 + WACS.HostBindings.* 0.1.0 + WACS.Transpiler.Lib 0.5.0] — `wacs aot` end-to-end + WASI rename

A wasm input is now one CLI call away from a self-contained NativeAOT
native binary:

```bash
wacs aot app.wasm -o app                          # compute-only
wacs aot coremark.wasm --wasi -o coremark         # WASI Preview 1
wacs aot app.component.wasm --wasip2 -o app       # WASI Preview 2
```

Internally `wacs aot` transpiles the wasm to a stable-named .dll,
scaffolds a throwaway consumer csproj with the right reference set
(WACS runtime + the new `WACS.HostBindings.*` source-generated
adapter for WASI), and runs `dotnet publish -p:PublishAot=true`. The
final native binary is copied to the requested output path and the
temp directory is removed (unless `--keep-temp`). No JIT, no
`Reflection.Emit`, no `Assembly.Load`, no `MethodInfo.Invoke` at run
time.

### New: `WACS.HostBindings.*` packages

- **`WACS.HostBindings.Abstractions`** — the `[WacsImport]` /
  `[WacsImportNames]` / `[WacsTranspiledImports]` attributes that mark
  static methods as wasm import bindings. Tiny, attribute-only, AOT-
  trim safe. Both `WACS.WASI.Preview1` and `WACS.WASI.Preview2`
  reference it to annotate their host functions.
- **`WACS.HostBindings.SourceGen`** — a Roslyn incremental source
  generator that, at consumer-build time, scans the assembly's
  `[assembly: WacsTranspiledImports("Ns.IImports")]` reference and
  emits an `IImports` adapter that wires the transpiled wasm's
  imports straight to the `[WacsImport]`-annotated statics. No
  reflection, no DispatchProxy, no runtime IL emission — pure
  source-gen, fully NativeAOT-compatible.
- **`Wacs.WASI.Preview1`** — every host function gets an
  ExecContext-free static entry-point variant alongside the existing
  instance method, so the source generator can wire them in directly.
  Behavior unchanged for embedders using the instance API.
- **`Wacs.WASI.Preview2`** — same treatment for the Component-Model
  hosts, including the existing `WasiPreview2Bundle` DI registration.

### AotLinked emission

`TranspilerOptions.Emission = EmissionTarget.AotLinked` skips the
codec wrapper that normally bridges the saved-to-static-reference
path's empty in-process registry. Direct `new ThinContext(...)` from
inlined IL constants instead. Now covers memories + active data
segments, globals (primitive inits), tables, and active element
segments — i.e. enough to run real wasm modules. Trimmer evidence:
the `__WACSInit` codec holder type is not present in the persisted
.dll's bytes. ~22% binary-size reduction on small modules; larger on
data-segment-heavy ones.

### `WACS.WASIp1` renamed → `WACS.WASI.Preview1`

The `WACS.WASIp1` package has been renamed to `WACS.WASI.Preview1` to
make room for `WACS.WASI.Preview2` (and eventually `.Preview3`) under
a single, consistent prefix. The shipped behavior is identical — same
types, same methods, same conformance posture against
`wasi-testsuite`.

The old `WACS.WASIp1` package id is now a **metapackage**: it
transitively pulls in `WACS.WASI.Preview1`, so existing
`<PackageReference Include="WACS.WASIp1" />` entries continue to
restore. C# `TypeForwardedTo` cannot bridge a namespace rename, so
consumer source code must update `using Wacs.WASIp1;` to
`using Wacs.WASI.Preview1;` (one-shot sed). The metapackage emits a
build-time warning (`WACS_WASIp1_DEPRECATED`) pointing at the
migration guide; suppress with
`<SuppressWacsWasip1DeprecationWarning>true</…>` while you migrate.

The `Wacs.Core.WASIp1` namespace inside `Wacs.Core` (`IBindable`,
`ErrNo`, `SystemExitException`, etc.) is **not** renamed. Those
types are interpreter-wiring conventions, not WASI host code.

See [`docs/MIGRATION_WASIp1_to_WASI.md`](docs/MIGRATION_WASIp1_to_WASI.md)
for the full migration guide and the sed one-liner.

## [WACS.WASIp1 0.10.0] — wasi-testsuite integration + correctness pass

Wires the dormant `Spec.Test/wasi` submodule (now pinned to
`prod/testsuite-base` for the prebuilt fixtures) into a new
`Wacs.WASIp1.Test` xUnit project that runs as part of `dotnet test`
in CI. **43 of 72 conformance fixtures pass** at HEAD; the rest are
in `Wacs.WASIp1.Test/skip.json` with documented Phase-4 follow-ups.

Sockets are no longer stubbed — the four `sock_*` host functions are
implemented over `System.Net.Sockets.Socket`, gated on a default-off
`AllowNetworkSockets` flag plus the requirement that the embedder
hand WACS pre-bound, pre-listening sockets via the new
`PreopenedSockets` config list. WASI Preview 1 has no `sock_open` /
`sock_bind` / `sock_listen`, so this is the same model `wasmtime
serve` uses for HTTP.

### Bug fixes

- `fd_seek` no longer truncates `*newoffset` to 32 bits — it's a u64
  in the spec and was overflowing on files >2 GB and silently
  corrupting the upper 4 bytes of the slot even on small files.
- `fd_prestat_get` / `fd_prestat_dir_name` strip the internal
  leading `/` from the directory name and report exactly
  `pr_name_len` bytes (no nul terminator). Matches what
  wasi-libc's `open_scratch_directory` expects, and was responsible
  for ~80% of the conformance fixtures' baseline failures.
- `fd_pread` / `fd_pwrite` / `fd_advise` / `fd_allocate` /
  `fd_filestat_set_size` accept their `filesize` (u64) arguments as
  `long` in the binding signatures and cast inside, since the binding
  dispatcher can't auto-coerce `wasm i64 → System.Int64 →
  System.UInt64`.
- `poll_oneoff` clock subscriptions correctly compute "now" per the
  subscription's `clock_id` in nanoseconds (was mixing .NET 100 ns
  ticks with the guest's nanoseconds, breaking absolute timeouts
  outright); `clock_id` is now actually consulted; write-readiness no
  longer uses the inverted `Position < Length` gate.
- `fd_filestat_set_times` / `path_filestat_set_times` reject
  `(ATIM | ATIM_NOW)` and `(MTIM | MTIM_NOW)` flag combinations per
  spec instead of silently letting NOW override the explicit value.
- `path_filestat_get` honors `LookupFlags.SymlinkFollow` — without
  the flag set, it reports `SYMBOLIC_LINK` for symlinks instead of
  resolving through them. Required bypassing the path mapper for the
  leaf component (the mapper resolves symlinks for sandbox safety,
  which is correct everywhere except `lstat`).

### New / lifted features

- `path_link` and `path_symlink` are real implementations (P/Invoke
  `link(2)` + `CreateHardLinkW` for hard links;
  `File.CreateSymbolicLink` for symbolic). Both gated on the
  matching `WasiConfiguration.AllowHardLinks` /
  `AllowSymbolicLinks` flags (still default-off).
- `fd_fdstat_set_flags` validates against known `FdFlags` bits and
  stores them on the `FileDescriptor`; `Append` is honored by
  `fd_write` (seek-to-end before write); the others are advisory.
- `fd_fdstat_set_rights` enforces "can only remove rights" per
  spec (returns `NotCapable` on any privilege escalation request);
  `fd_read` / `fd_write` / `fd_pread` / `fd_pwrite` enforce the
  resulting rights bits.

### New configuration knobs

- `WasiConfiguration.AllowNetworkSockets` (default `false`) +
  `PreopenedSockets` list.
- `WasiConfiguration.PreopenHostRootDirectory` (default `true` for
  back-compat with the `Wacs.Console` "fd 3 = cwd" model). Flip
  false to follow the wasmtime convention where fd 3 is the first
  explicit preopen.

### Other

- `FileDescriptor` gains `Flags`, `Socket`, and `IsListening` fields
  (used by the above).
- New `Wacs.WASIp1.SocketStream` — a `Stream` wrapper over a
  `Socket` so the existing `fd_read` / `fd_write` iovec paths work
  unchanged on connected sockets.
- New optional Python adapter at `Spec.Test/wasi-adapters/wacs.py`
  for users who want to run the upstream `wasi-testsuite` Python
  harness against an installed `wacs` global tool.

## [WACS.Cli 1.1.0] — `wacs bindgen` verb

Rolls binding generation into the unified `wacs` tool as a fourth
verb, sequenced before any tag push so users only ever see the
unified surface. Symmetric with the `wasm-transpile → wacs`
consolidation that landed in 0.10.0: one CLI, verb-based, smart
auto-detect.

```bash
wacs bindgen ./wit -o ./gen/        # forward: WIT directory → C# bindings
wacs bindgen ./wit/foo.wit -o ./gen/ # forward: single .wit file
wacs bindgen ./app.dll -o ./regen/  # reverse: regenerate from a transpiled .dll
```

Direction inferred from input shape — `.dll` triggers reverse,
`.wit` is forward single-file, a directory is forward tree (with
`deps/` recursion).

The previously-staged-but-never-published
`WACS.ComponentModel.Bindgen` package + its `wit-bindgen-wacs`
CLI are deleted entirely. The `Wacs.ComponentModel.Bindgen/`
project + the `nuget.yml` workflow's matrix entry would never
have been useful — there are no consumers to migrate, and
shipping a brand-new package alongside its replacement would
have created confusion in the NuGet listing.

`WACS.ComponentModel.Bindgen.Lib` (programmatic surface) is
unaffected — source generators and build-time integrations
keep referencing it directly. `wacs bindgen` is itself a thin
wrapper around the same Lib API.

## [0.10.0] — Component Model

The Component Model release. Adds WebAssembly Component Model
support across the toolchain — six new packages, two existing
packages bumped, and the unified `wacs` CLI replaces the legacy
`wasm-transpile` tool. Single PR; commit-by-commit detail in the
git history (`git log v0.9.1..v0.10.0`).

**New packages.**

- **`WACS.ComponentModel 0.1.0`** — pure-C# parser, decoder, and
  interpreter for WebAssembly components. WIT text parsing, full
  canonical-ABI lift/lower (string / list / option / result /
  variant / record / tuple / resource handles), `ComponentInstance`
  for end-to-end instantiation against a `WasmRuntime`, and
  `ComponentBridge` adapters for cross-engine composition
  (interpreter components consumed as transpiler-side host bundles
  via `DispatchProxy`, and the inverse direction binding typed
  exports as host functions).
- **`WACS.ComponentModel.Bindgen 0.1.0`** — `wit-bindgen-csharp`
  CLI that emits `[WitSource]`-tagged C# interfaces from a WIT
  package directory.
- **`WACS.ComponentModel.Bindgen.Lib 0.1.0`** — programmatic
  surface for the same emitter (used by source generators and
  build-time integrations).
- **`WACS.WASI.Preview2 0.1.0`** — typed C# interfaces + default
  implementations for the 25 WASI Preview 2 host packages
  (cli/clocks/filesystem/http/io/random/sockets), backed by
  `Wacs.ComponentModel`. Includes resource-table state via
  `ResourceContext` so handles allocated by one interface
  (`IStdout.GetStdout` returning `own<output-stream>`) resolve back
  through another's instance methods.
- **`WACS.WASI.Preview2.DependencyInjection 0.1.0`** —
  `Microsoft.Extensions.DependencyInjection` extension that
  registers the full Preview 2 surface plus a `WasiPreview2Bundle`
  aggregate the transpiler's direct-linked path consumes.
- **`WACS.Cli 1.0.0`** — unified `wacs` global tool that
  supersedes `wasm-transpile`. Verb-based subcommand layout
  (`wacs run` / `build` / `inspect`) matching `wasmtime` / `wasmer`
  precedent. Direct-run shortcut (`wacs my.wasm` defaults to `run`),
  smart component-vs-core auto-detect, multi-input ModuleLinker
  composition, full instrumentation surface inherited from the
  legacy `Wacs.Console` (gas, profile, instr-logging, super,
  switch).

**Bumped packages.**

- **`WACS 0.9.1 → 0.10.0`** — `WasmRuntime` gains two methods
  (`EnumerateBoundEntities`, `TryGetBoundHostFunctionType`) used
  by the component-model validation layer. Removes legacy
  `Wacs.Core/Components/` prototypes (replaced by
  `Wacs.ComponentModel`).
- **`WACS.Transpiler.Lib 0.3.0 → 0.4.0`** — major feature lands:
  - `ComponentTranspiler` for component-mode AOT transpilation
    (single-core + multi-core via primary canon-lift detection).
  - `ModuleLinker` cross-module composition for multi-input runs.
  - `MainEntryEmitter` + `ComponentMainEntryEmitter` for
    `--emit-main` output.
  - `DirectLinkedImportEmit`: inline IL through typed host bundles
    (no delegate hop) for every canon-ABI shape: primitives,
    string (utf8/utf16/latin1), `list<T>`, `option<T>`, `Result<T,E>`
    (including `Result<Unit, Variant>`), records, variants with
    payload-bearing cases, resource handles. Resource INSTANCE
    methods returning aggregates work end-to-end.
  - `ExportInterfaceEmit`: `[WitSource]`-tagged `I{Iface}` types
    emitted into transpiled `.dll`s, so a transpiled component
    serves as a host package for downstream transpiles
    (chain mode).
  - `WitContract.FromAssembly` two-path: embedded WIT first,
    fallback to `[WitSource]`-tagged interfaces for
    transpiled-output round-trip.
  - 1300+ new tests (`Wacs.Transpiler.Test`, `Wacs.ComponentModel.Test`,
    `Wacs.WASI.Preview2.Test`).

**Deprecated.**

- **`WACS.Transpiler 0.3.0 → 0.3.1`** — `wasm-transpile` is
  superseded by `wacs`. Every flag still works; every invocation
  prints a stderr deprecation banner pointing at the migration.
  `<PackageDeprecationReason>` baked into the package metadata.
  See the entry below.

**End-to-end demo.** Multi-core WASI Preview 2 components run
through direct-linked imports without a delegate hop:

```bash
$ wacs run --wasip2 --call greet wasi-hello-component.wasm
hello
```

## [WACS.Cli 1.0.0] — Unified CLI

Ships a new `wacs` global tool that supersedes `wasm-transpile`.
Verb-based subcommand layout (`wacs run` / `build` / `inspect`)
matches `wasmtime` / `wasmer` industry precedent — keeps execution
flags (gas, profile, instr-logging) separate from compilation flags
(simd strategy, data-storage, tail-call) instead of cramming both
into a single CLI surface.

**Verbs.**
- `wacs run` — execute via interpreter (default) or transpiler
  engine. Carries the full Wacs.Console instrumentation surface
  (`--profile`, `--gas-limit`, `--log-execution`, `--stats`,
  `--super`, `--switch`) plus the multi-input ModuleLinker
  composition + component-mode auto-detect inherited from
  `wasm-transpile`. With `--wasip2` / `--host-package` for a
  component, implicitly upgrades to the transpiler engine since
  the typed bundle is a transpile-time concept.
- `wacs build` — transpile to a `.dll`. Multi-input runs land
  siblings as `<basename>.dll` alongside the chosen `--output`
  path. `--emit-main` bakes a `Program.Main(string[])` boilerplate
  into the output.
- `wacs inspect` — parse-only diagnostics: stats summary
  (functions / exports / memory / data segment bytes), exports /
  imports listing, `--dump-wat` round-trip via TextModuleWriter.

**Direct-run shortcut.** `wacs my.wasm` defaults to `wacs run my.wasm`
when the first positional arg is a `.wasm` / `.wat` file path that
exists.

**Smart defaults.** Component-vs-core auto-detect via the layer
header byte; multi-file input → ModuleLinker composition.

**Migration.** The legacy `wasm-transpile` (`WACS.Transpiler`)
package stays installable at `0.3.1` so existing pipelines keep
working — every flag still functions, output is byte-identical —
but invocations now print a stderr deprecation banner pointing at
the migration. See
[`Wacs.Console/README.md`](Wacs.Console/README.md) for the
verb-by-verb migration table.

**PackageId.** `WACS.Cli` (the bare `WACS` id is the runtime
library, `Wacs.Core`); the tool command users type is `wacs`.

```bash
dotnet tool install -g WACS.Cli
```

## [WACS.Transpiler 0.3.1] — Deprecation banner

Final release of the legacy `wasm-transpile` CLI before its
sunset. Every flag still works; every invocation prints two
deprecation lines to stderr pointing at `WACS.Cli` (`wacs`).
NuGet metadata's `<PackageDeprecationReason>` baked in. README
fronted with a deprecation block + migration table.

## [0.9.1] — JS String Builtins

Implements the full [JS String Builtins
proposal](https://github.com/WebAssembly/js-string-builtins)
(WebAssembly 3.0, Phase 5) backed by `System.String`. Modules compiled
with `--enable-js-string-builtins` (Binaryen) or equivalent now run on
WACS without modification — wasm manipulates host-owned UTF-16 strings
directly, without copying through linear memory on every boundary
crossing.

**Why it works.** The proposal's 13 imports under `wasm:js-string` are
defined observationally against UTF-16 code units — length is the
code-unit count, `charCodeAt` yields a code unit (not a code point),
`substring` is half-open, and surrogate pairs are preserved verbatim.
`System.String` is also UTF-16 with identical indexing and surrogate
semantics, so a pure environment swap yields observably identical
behavior. Nothing in the spec constrains the underlying representation
— only the input/output behavior.

**Host opt-in.** Register the namespace before instantiation, same
idiom as `Wasi.BindToRuntime`:

```csharp
using Wacs.Core.Runtime.Builtins;

var runtime = new WasmRuntime();
JsStringBuiltins.BindTo(runtime);
var modInst = runtime.InstantiateModule(module);
```

Hosts pass strings to wasm by wrapping as an externref:
`new Value(ValType.Extern, 0L, new JsStringRef("hello"))`.

**The 13 imports.** `test`, `cast`, `length`, `concat`, `substring`,
`equals`, `compare`, `charCodeAt`, `codePointAt`, `fromCharCode`,
`fromCodePoint` (11 simple i32 / externref functions) plus
`fromCharCodeArray` and `intoCharCodeArray` (GC-array-typed bridge
functions that read/write `StoreArray`). All 13 implemented as
`IFunctionInstance` subclasses that pop directly off the operand stack
— host-delegate marshaling can't carry externref through `PopScalars`,
so the builtins bypass it entirely.

**Infrastructure changes.** `InstCall.Link` generalized to dispatch
any `IFunctionInstance`, not just `HostFunction` / `FunctionInstance`
— opens the door for additional recognized-import namespaces in the
future. A new `BindHostFunction((module, entity), IFunctionInstance)`
overload on `WasmRuntime` for non-delegate registrations.

**Transpiler, AOT.** No transpiler changes needed — the transpiler
routes imports through `HostedRunner.BuildImportsProxy` →
`CreateStackInvoker` → `ExecContext.Invoke`, which already dispatches
any `IFunctionInstance`. Full AOT compatibility preserved
(`IsAotCompatible=true`); no `Reflection.Emit`, `DynamicMethod`, or
`Expression.Compile` anywhere in the new code path.

**Docs.** JS String Builtins reclassified from ✅ to ✳️ in the feature
matrix, alongside JS BigInt↔i64 and JSPI — the *wasm-level* semantics
are observably supported, but the *JS-API surface* (the namespace
name `wasm:js-string`, the JS-engine-recognized import handling) is a
browser idiom WACS emulates rather than implements natively. New
[`BROWSER_IDIOMS.md`](docs/BROWSER_IDIOMS.md) explainer covers all three
✳️ features: how each proposal maps to a native .NET primitive
(`long`, `System.String`, `Task`/`async`) and the host-side API for
each.

**Tests.** 34 new tests in `Wacs.Core.Test/JsStringBuiltinsTests.cs`:
28 WAT-based integration tests exercising the 11 simple builtins
through the runtime's standard dispatch (happy path, OOB sentinels,
traps, surrogate round-trip), plus 6 direct-invoke tests for the
GC-array-typed builtins (WAT parser doesn't yet support
`array.new_fixed`, so these construct `StoreArray` directly in C# and
drive the bound `IFunctionInstance` via `CreateStackInvoker`).

## [0.9.0] + WACS.Transpiler / Transpiler.Lib [0.3.0] + WACS.WASI.Threads [0.1.0] — Concurrent wasm execution

Makes the WACS runtime reentrant under concurrent host threads,
hardens shared-mutable state, adds a wasi-threads host adapter, and
lands the type-system foundation for shared-everything-threads.
Five stacked layers, 24 commits. No backwards-incompatible changes
to baseline wasm — all new behavior is opt-in or gated behind a
host-visible primitive.

**Layer 1 — Per-thread execution substrate.** The `WasmRuntime.Context`
singleton `ExecContext` became a `ConcurrentDictionary<ThreadId,
ExecContext>` keyed by `ManagedThreadId`. Each host thread entering the
runtime lazily gets its own operand stack, frame pool, locals pool,
and call stack while sharing a new `SharedRuntimeState` (Store,
Attributes, linked instruction arrays) by reference. `WasmThread` +
`IWasmThreadHost` primitives in `Wacs.Core/Runtime/Concurrency/` —
thread-spawn with task-based completion, cancellation-token
observation at call boundaries, `InterruptedException : TrapException`
propagating through existing trap handlers to `WasmThread.Completion`.
`IConcurrencyPolicy` grows async default-methods
(`Wait32Async`/`Wait64Async`/`NotifyAsync`) that wrap the sync versions
— shape only, enables a truly-yielding wait implementation as a later
additive change.

**Layer 2 — Shared-mutable state hardening.** `GlobalInstance.Value`
(24-byte struct) now serializes concurrent read/write through a
lazy per-instance lock when `IsShared` — non-shared globals stay on
the zero-overhead direct path. `TableInstance.Grow` pre-allocates
`List<T>.Capacity` in a single atomic field-swap before appending,
so concurrent `call_indirect` readers never see a mid-resize state;
readers stay lock-free even for shared tables. `TranspiledFunction`
swaps its reused `_paramBuffer` for `ArrayPool<object?>.Shared.Rent/
Return` per call. Dead `_asideVals` static stacks removed.
`Store.ReplaceFunction` documented as init-only.

**Layer 3 — wasi-threads adapter.** New sibling project
`Wacs.WASI.Threads` with `WasiThreads : IBindable`, 30 lines of actual
logic wiring the `wasi:thread-spawn` host import onto
`IWasmThreadHost.Spawn`. Monotonic positive-i32 tid allocation;
`wasi_thread_start` resolution via `ctx.Frame.Module.Exports` — no
explicit module registration. AOT-compatible (net8.0 + netstandard2.1,
`IsAotCompatible`). Hosts that don't want threads don't pay for them.

**Layer 4 — Soak + integration testing.** 13 new tests:
atomic-op-variety stress matrix (every RMW family × i32/i64 +
subword rmw8/rmw16 under 16-thread × 1k-iter contention), end-to-end
wait/notify producer-consumer through `HostDefinedPolicy` (with
timeout and not-equal precheck paths), and a 60-runtime soak that
would have caught the original Layer 1c `ThreadLocal<ExecContext>`
slot-exhaustion crash.

**Layer 5 — Shared-everything-threads foundation.** Feature-flag
`RuntimeAttributes.EnableSharedEverythingThreads` (default false) gates
the Phase-1-proposal subset that's stable enough to ship:
- `shared` annotations on globals (binary bit 1 of the mutability byte;
  text `(global (shared) ...)`) and tables (leveraging existing Limits
  Shared infrastructure).
- `thread_local` annotations on globals (binary bit 2; text
  `(global (thread_local) ...)`). Each host thread sees its own slot,
  initialized from the declared initializer on first access; storage
  lives on the per-thread `ExecContext` from Layer 1c.
- Declaration-driven `IsShared` wiring through to
  `GlobalInstance.EnableConcurrentAccess` / `TableInstance.EnableConcurrentAccess`.
  Layer 2b's "any shared memory → all globals/tables shared"
  approximation stays as a fallback for threads-1.0 modules that
  predate per-declaration annotations.
- Import-type matching: shared/thread_local must match exactly; a
  non-shared host global can't satisfy a shared import.

Deferred in Layer 5 because the proposal hasn't assigned canonical
opcode bytes: `global.atomic.{get,set,rmw.*}` instructions and
`pause`. Shared globals still work correctly through regular
`global.get`/`global.set` via the locking foundation — atomic ops are
a performance refinement on top.

Deferred as separate programs of work:
- **Emscripten pthreads ABI** (complex Web-flavored runtime surface;
  converging wasi-threads is the forward direction for most workflows).
- **Component Model canonical builtins** (`thread.spawn_ref`,
  `thread.spawn_indirect`) — will wire onto the same
  `IWasmThreadHost.Spawn` primitive when Component Model support lands.
- **Shared struct/array types**, **shared function references** —
  type-system discipline still evolving in the proposal.

**Verification:**
- Wacs.Core.Test: **366/366** (+28 new concurrent-execution tests)
- Wacs.Transpiler.Test: 561/561
- Spec.Test (full wasm-3.0 suite): 723/723
- `dotnet publish -p:PublishAot=true` produces a clean 15MB native
  binary.

## [0.8.3] + WACS.Transpiler / Transpiler.Lib [0.2.1] — Threads proposal

Implements the [WebAssembly threads proposal](https://github.com/webassembly/threads)
across all three execution back-ends. Flips README feature table
**Threads / threads ❌ → ✅**. All 47 atomic instructions — load/store
(full-width + subword zero-extending), RMW (add/sub/and/or/xor/xchg in
i32/i64/subword), cmpxchg, wait/notify, and fence — share the same
phase-1 primitives so correctness is identical across back-ends.

- **Polymorphic interpreter** (phase 1 / #79):
  - New `Wacs.Core.Runtime.Concurrency` namespace:
    `ConcurrencyPolicyMode` (NotSupported / HostDefined),
    `IConcurrencyPolicy`, `NotSupportedPolicy` (single-thread semantics
    — matching-value finite-timeout sleeps then returns 1, infinite
    timeout traps, mismatch returns 2), `HostDefinedPolicy` (real
    wait/notify via `ConcurrentDictionary<(MemoryInstance, addr),
    WaitSlot>` + per-waiter `ManualResetEventSlim`).
  - `MemoryInstance` atomic helpers:
    `AtomicLoad/Store/Add/Exchange/And/Or/Xor/CompareExchange{Int32,
    Int64}`. `Interlocked.*` on net8.0+; `CompareExchange` loop
    fallback on netstandard2.1 for And/Or/Xor.
    Lazy `ReaderWriterLockSlim _growLock` only allocated when shared
    + HostDefined — single-threaded modules pay nothing.
  - 47 instruction classes under `Wacs.Core.Instructions.Atomic/`:
    `InstAtomicMemoryOp` base with exact-alignment + shared-memory
    validation, subword CAS via `SubwordCas.Loop` / `SubwordCas.Cmpxchg`.
  - Factory (`SpecFactoryFE.cs`) + WAT parser extended with
    `TryGetAtomicMemoryOpcode` dispatch.
  - `RuntimeAttributes.ConcurrencyPolicy` with IL2CPP-detecting default
    (`Type.GetType("UnityEngine.Application,…")`, AOT-safe).
    `RelaxAtomicSharedCheck` escape hatch for toolchains that emit
    atomics on non-shared memories.
- **Switch runtime** (phase 2 / #80):
  - `BytecodeCompiler.SizeOfAtom` + `EmitAtom` — 12-byte memarg
    (`[memIdx:u32][offset:u64]`) stream encoding, 0 bytes for
    `atomic.fence`.
  - `AtomicHandlers.cs` with 47 `[OpHandler(AtomCode.X)]` methods.
    The source generator (`DispatchGenerator`) auto-discovers them and
    inlines the bodies into `DispatchFE` — **67 AtomCode references**
    in the regenerated `GeneratedDispatcher.g.cs` vs. 0 before.
- **AOT transpiler** (phase 3 / #81):
  - New `Wacs.Transpiler.Lib/AOT/Emitters/AtomicEmitter.cs` + public
    `AtomicHelpers` class. Functions containing atomics transpile to
    native CIL instead of falling back to the interpreter;
    `FallbackCount` is 0 for mixed-family modules.
  - Wait/notify routes through `ThinContext.ExecContext?.Concurrency-
    Policy ?? _standaloneFallback` — standalone / saved-dll consumers
    get `NotSupportedPolicy` semantics by default.
- **Tests (new):**
  - `Wacs.Core.Test.AtomicInstructionTests` — 28 tests (21 polymorphic
    + 7 switch-runtime parity).
  - `Wacs.Core.Test.SpecWastThreadsTests` — 4 tests over a pinned
    snapshot of `WebAssembly/threads@f521d7b3` at
    `Spec.Test/Data/threads/atomic.wast`.
  - `Wacs.Transpiler.Test.AtomicEquivalenceTests` — 12 polymorphic ↔
    transpiled equivalence tests.
  - `Wacs.Core.Test` total: 338/338. `Wacs.Transpiler.Test` total:
    561/561.
- **AOT stays green.** No runtime `Reflection.Emit` introduced;
  IL2CPP-safe by construction in `Wacs.Core`. Transpiler runtime
  assembly unchanged w.r.t. AOT safety (still uses `Reflection.Emit`
  as before — the produced DLL is AOT-loadable).

Concurrent wasm execution in a single `WasmRuntime` and host
thread-spawn imports remain out-of-scope for this release — the
threads proposal itself doesn't standardize spawning, and WACS's
single-`ExecContext` model is a separate refactor tracked for a
future release.

## [0.8.2] First-class WAT / WAST text format

- **Pure-C# WAT reader + writer.** New `Wacs.Core.Text` namespace
  provides a self-contained WebAssembly text-format pipeline:
  - `Lexer` / `Token` / `SExpr` / `SExprParser` tokenize and tree-ify
    WAT source (line / block comments, string escapes, annotations,
    quoted identifiers with full `\XX` / `\u{…}` UTF-8 decoding).
  - `Mnemonics` builds a `FrozenDictionary<string, ByteCode>` once at
    static-ctor time by reflecting over the `[OpCode(...)]` attributes
    already present on every opcode enum field. Parse and render share
    the same source of truth.
  - `TextModuleParser.ParseWat(Stream|string)` produces the *same*
    `Module` object the binary parser produces — two-pass name
    resolution, rec-group flattening, inline-typeuse synthesis with
    rec-isolated dedup, and per-instruction `ParseText` hooks
    co-located with each instruction's binary `Parse` override.
  - `TextScriptParser.ParseWast(...)` produces `ScriptCommand[]` for
    `.wast` scripts, including `(module binary …)` / `(module quote …)`
    and every `(assert_*)` form.
  - `TextModuleWriter.Write(module)` emits canonical, parser-friendly
    WAT that round-trips back through the text parser to a
    structurally equivalent `Module`. Distinct from the existing
    `ModuleRenderer.RenderWatToStream` debug/display variant, which is
    kept for inspection use.
- **`Wacs.Console` accepts `.wat` input.** `dotnet run --project
  Wacs.Console -- module.wat` runs text-format modules through any
  back-end (`--super`, `--switch`, `-t` / `--aot`) identically to
  `.wasm` input. The `-r` / `--render` flag now uses
  `TextModuleWriter` so the emitted `.wat` round-trips cleanly.
- **Spec-suite coverage: 100%.** New `Wacs.Core.Test` xUnit project
  runs two gates across the full WebAssembly 3.0 spec suite
  (`Spec.Test/spec/test/core/*.wast`):
  - `SpecWastSmokeTests` — **120 / 120** `.wast` files parse without
    error. The `SkipList` is empty; there are no text-only skipped
    tests.
  - `SpecWastEquivalenceTests` — **3457 / 3457** modules embedded in
    the spec scripts produce structurally identical `Module` objects
    under both the text parser and the binary parser (including
    preserved `try_table` shapes, rec-group layouts, GC struct /
    array composite types, annotations, and all Phase-5 / Phase-4
    proposals).
- **WIT IDL parser.** New `Wacs.Core.Components` namespace hosts a
  standalone recursive-descent parser for the component model's WIT
  interface definition language (packages, interfaces, worlds, full
  type system including `own<T>` / `borrow<T>` resource handles,
  `use` statements, world includes). Separate grammar from WAT, so a
  separate pipeline. Groundwork for the component-model work tracked
  in the roadmap.
- **AOT stays green.** No runtime `Reflection.Emit`. Reflection over
  `[OpCode("…")]` attributes is one-shot, at static-ctor time, on the
  same pattern `OpCodeExtensions.LookUp` already uses. `dotnet publish
  Wacs.Console -c Release -r osx-arm64 -p:PublishAot=true` continues
  to pass and the published binary parses + executes `.wat` input.

## WACS.Transpiler / WACS.Transpiler.Lib [0.2.0] Cross-process loading

- **Package split**: WACS.Transpiler remains the `wasm-transpile`
  dotnet-tool CLI; the programmatic surface (AOT namespace + Hosting
  helpers) now ships as a separate NuGet package **WACS.Transpiler.Lib**.
  Consumers who only want the library can reference it without pulling
  the tool packaging.
- **Saved .dlls now run in a fresh process.** Every transpiled assembly
  embeds a codec-encoded `ModuleInitData` as a `byte[]` field on a
  generated `__WACSInit` type. The Module constructor dispatches through
  `InitializationHelper.InitializeFromEmbedded`: in-process transpile +
  run keeps the fast `InitRegistry` path; cross-process load decodes the
  embedded bytes and rebuilds memories, tables, globals, data segments,
  and type metadata from the codec with no re-parse of the original
  WASM. Closes the v0.1 "cross-process execution is not yet supported"
  limitation.
- **Codec format documented and versioned.** Format spec in
  `Wacs.Core/Compilation/../../Wacs.Transpiler.Lib/AOT/InitDataFormat.md`:
  8-byte "WACSINIT" magic, u8 major+minor version, TLV-tagged section
  stream. Unknown tags skipped on decode (forward compat); newer-major
  files rejected cleanly. 60+ unit tests cover each section and
  primitive.
- **`TranspiledModuleLoader` (new)**: seamless dynamic-environment
  loading. Reads a saved `.dll`, discovers the Module / IExports /
  IImports types, wires imports (typed object OR by-name delegate
  dictionary via `DispatchProxy`), returns a `LoadedModule` handle
  that exposes the interfaces as first-class reflection objects plus
  `Invoke(name, args)` / `GetExport<TDelegate>(name)` for dispatch.
- **`Wacs.Console` integration**: new `--aot` flag transpiles the
  instantiated module and runs through the transpiled code. Subset of
  `TranspilerOptions` surfaced via `--aot_simd`, `--aot_no_tail_calls`,
  `--aot_max_fn_size`, `--aot_data_storage`; `--aot_save <path>` also
  persists the .dll to disk. CoreMark end-to-end: **17,542 iter/sec**
  on `--aot` vs 376 (`--switch --switch_super`) vs 277 (polymorphic).
- **Still not covered in 0.2** (tracked for v0.3): `--emit-main`
  expansion (auto-bind `--wasi-host`, `--allow-missing-imports` stubs,
  ref-type / v128 argv parsing).
- Spec parity unchanged: 473/473 on WebAssembly 3.0 spec suite; the new
  codec + loader add 70 unit tests + 4 cross-process end-to-end tests
  (549 total transpiler suite).

## [0.8.1] Switch runtime (opt-in, source-generated dispatcher)

- New alternative interpreter backed by a source-generated monolithic
  `switch` over an annotated bytecode stream. Immediates are pre-decoded
  at instantiation (no LEB128 at runtime), branch targets resolved to
  absolute stream offsets, and every reachable function is compiled
  eagerly when `UseSwitchRuntime` is set before `InstantiateModule`.
  AOT-safe — no `Reflection.Emit`, no `DynamicMethod`; build-time source
  generation only.
- Opt-in at the API level:
  ```csharp
  runtime.UseSwitchRuntime = true;
  runtime.ExecContext.Attributes.UseSwitchSuperInstructions = true; // optional stream-fuser
  runtime.InstantiateModule(module);
  ```
- `Wacs.Console` exposes the runtime through two new flags: `--switch`
  routes dispatch through the switch runtime; `--switch_super`
  additionally enables the bytecode-stream super-instruction fuser.
- **Spec parity: 118/118 wast files pass** on the WebAssembly 3.0 spec
  suite (matching the polymorphic runtime).
- Rough microbenchmarks (M1 Pro, .NET 8, median of 3): `switch` +
  `swFuse` is 1.5–2× faster than polymorphic across `fib-iter` / `fac` /
  `sum`. CoreMark: 376 iter/s (`--switch --switch_super`) vs 277 iter/s
  polymorphic — a 36% improvement on a real workload.
- Full architecture walkthrough in
  [`Wacs.Core/Compilation/SWITCH_RUNTIME.md`](Wacs.Core/Compilation/SWITCH_RUNTIME.md)
  (phases A–N, including the iterative Run that eliminates native-stack
  growth per WASM call).
- The polymorphic runtime remains the default and is unaffected.

## WACS.Transpiler [0.1.0] First release

- New NuGet package: `WACS.Transpiler`. Installs as a dotnet global tool
  (command: `wasm-transpile`). Ahead-of-time transpiles a `.wasm` module
  into a .NET assembly.
- CLI surface mirrors `TranspilerOptions`: `--simd`, `--no-tail-calls`,
  `--max-fn-size`, `--data-storage`, `--gc-checking`.
- `--emit-main` / `--entry-point` / `--main-class` bundle a host
  `Program.Main` into the output assembly for modules with no imports
  and scalar exports.
- `--run` invokes the emitted `Program.Main` in-process after
  transpiling, forwarding any trailing positional args — handy for IDE
  run configurations that want to transpile-and-execute in one step.
- Library surface: `Wacs.Transpiler.AOT.ModuleTranspiler.Transpile(...)`
  and `TranspilationResult.SaveAssembly(path)` for programmatic use.
- **Spec-equivalent to the WACS interpreter: 473/473 passing on the
  WebAssembly 3.0 spec test suite**, verified on both macOS ARM64 and
  Linux x64. Includes: multi-result `return` / `call_indirect` dispatch
  (via a MethodInfo registry for targets whose byref out-params don't
  fit Func/Action delegates), `f32.convert_i64_u` / `f64.convert_i64_u`
  routed through the interpreter's spec-exact RTNE helper for
  platform-invariant rounding, `struct.new` / `struct.new_default`
  global initializers with typed field storage, and correct
  sign/zero-extension for packed i8 / i16 struct reads.
- Known limitation: the saved `.dll` is intended for in-process use in
  this release — cross-process standalone execution (init-data embedded
  into the assembly) is a v0.2 milestone. See
  `Wacs.Transpiler/README.md` for details.

## [0.8.0] Public transpiler surface

- Public getters on ~20 instruction classes, `IFunctionInstance.Invoke`
  on the interface, `Store.ReplaceFunction`, and runtime accessors so
  `WACS.Transpiler` can drive transpilation from outside the assembly.
- New `WasmRuntime.TryGetExported{Memory,Table,Global,Tag}` /
  `GetExported{Memory,Table,Global,Tag}` accessors, mirroring the
  existing `TryGetExportedFunction` shape so host code can resolve any
  exported entity without reflecting into internals. Resolves #63.
- **Rename (breaking):** The interpreter super-instruction flag
  `WasmRuntime.TranspileModules` → `WasmRuntime.SuperInstruction`, the
  method `TranspileModule` → `ApplySuperInstructions`, and the
  `Wacs.Core.Runtime.Transpiler` / `Wacs.Core.Instructions.Transpiler`
  namespaces → `...SuperInstruction`. `FunctionTranspiler.TranspileFunction`
  is now `SuperInstructionRewriter.Rewrite`. This disambiguates from the
  new `WACS.Transpiler` AOT package.
- No behavior change for existing consumers beyond the rename — additive otherwise.

## [0.7.5] Fix rollup
- Fix to indirect calls
- Fix to reentrant calls
- Exposing global var index for use in parsing-only contexts

## [0.7.4] Performance
### Link-time optimization
- Instantiated functions are now flattened into a tape at link time
- Labels, branches, and function call targets are now computed during link
- Addressable store elements can now be precomputed and cached during link
- block, loop, trytable, and end instructions are now flagged as nops and will not incur a dispatch function call
### OpStack resident locals
- Local variables are now allocated on the stack
- Local variable operations now have improved cache locality 
- This refactor is prep for link-time register computation

## [0.7.3]
- Reimplemented AOT compatible invoker bindings

## [0.7.2]
- removing Linq.Expression for AOT compatibility

## [0.7.1]
- fixes to CreateInvoker binding

## [0.7.0]
- wasm-3.0 spec support
- exnref/tag support
- memory64 support
- multi-memory support (enabled)

## [0.6.0]
- wasm-gc extension
- function-references extension

## [0.3.0]
- Implemented JSPI-like async binding and execution
- Hooked up more super-instruction threading

## [0.2.0]
- Implemented super-instruction threading
- Precomputed (non-allocating) block labels

## [0.1.6]
- Updating to latest dll
- Fixing package layout
- Fixing Sample importer

## [0.1.4]
- Initial project setup for Unity.
