# Doc 1 — WASM → CIL Mapping Reference

**Status:** authoritative. Other docs and the implementation plan reference this file.
**Scope:** how every WASM 3.0 concept lands in CIL as emitted by the Wacs.Transpiler AOT path.
**Audience:** anyone modifying the transpiler, adding a feature, or debugging emission.

This document describes **what exists at runtime** (representations) and **what instructions emit** (IL sequences). It does not prescribe implementation; `02-structural-patterns.md` and `04-implementation-plan.md` do that. The mapping below is the contract that patterns and plans must preserve.

---

## 1. Runtime context

Every transpiled function takes `ThinContext` as its first CLR argument (`arg 0`). `ThinContext` holds all module-level state needed at runtime:

- `Memories : MemoryInstance[]` — linear memories, indexed by memidx
- `Tables : TableInstance[]` — tables, indexed by tableidx
- `Globals : GlobalInstance[]` — globals, indexed by globalidx
- `FuncTable : Delegate[]` — all functions (imports then locals), indexed by funcidx; used by `call_indirect`, `call_ref`, `ref.func`
- `ImportDelegates : Delegate[]` — imported-function delegates (same objects as `FuncTable[0..importCount-1]`)
- `Tags : TagInstance[]` — tags, indexed by tagidx. Reference equality is tag equality. Imported tags are wired by the linker to share the exporter's `TagInstance`; local tags are fresh instances allocated in `InitializationHelper.Initialize`.
- `Types : TypesSpace?` — module type space; used for `ref.test`/`ref.cast` on concrete type indices
- `FuncTypeHashes : int[]?` — per-funcidx structural hash; used for `ref.test`/`ref.cast` on funcref
- `Capabilities : TranspilerCapabilities` — feature flags baked at transpile time
- `Store`, `ExecContext`, `Module` — nullable; non-null only when running mixed-mode inside the WACS framework (interpreter interop). Standalone mode leaves them null and the transpiler must not depend on them for correctness.

The transpiler MUST NOT assume `Store`/`Module`/`ExecContext` are present. Any feature requiring those must either fall back gracefully or be gated on `ctx.Module != null`.

---

## 2. Value representation

WASM values appear in three distinct contexts. The representation differs by context.

### 2.1 Representations

| Context | Scalar (i32/i64/f32/f64) | v128 | funcref / externref | GC refs (anyref, eqref, structref, arrayref, i31ref, concrete) | exnref |
|---|---|---|---|---|---|
| **Function signature** (params, returns) | `int`/`long`/`float`/`double` | `Value` (ref backed by `VecRef`) | `Value` | `Value` (see §2.4) | `Value` |
| **Storage** (table elements, global value, struct fields of ref type, array elements of ref type, exception fields) | typed CLR field (`int`, `long`, etc.) | `Value` | `Value` | `Value` | `Value` |
| **Internal CIL evaluation stack** (inside a function, between instructions) | typed (`int`, `long`, etc.) | `Value` | `Value` | `object` | `WasmException` |
| **CIL local** (declared via `DeclareLocal`) | typed | `Value` | `Value` | `object` | `WasmException` |

The split between "signature" and "internal CIL stack" is the core pattern. Crossing the split requires boundary wrap/unwrap (doc 2 §3).

### 2.2 Scalar types

`i32` ↔ `int`, `i64` ↔ `long`, `f32` ↔ `float`, `f64` ↔ `double`. These flow natively on the CIL stack and in locals. No boxing, no wrapping.

Sign-extending loads (`i32.load8_s` etc.) emit the CIL conv sequence (`ldind.i1; conv.i4`). Zero-extending loads use the unsigned form.

### 2.3 Vector type (v128)

`v128` is represented as `Value` with `Type = V128` and `GcRef = VecRef(V128)`. Reasons it is not a bare `V128` struct on the CIL stack:

- `V128` is a managed struct; putting it on the CIL stack at a merge point triggers the same verifier issue as `Value` with a managed ref (see doc 2 §1).
- Using `Value` keeps v128 interchangeable with other ref-shaped values in helpers.

SIMD instructions unbox to `V128` inside the operation and rebox to `Value` on result (see `SimdEmitter`).

### 2.4 Reference types

WASM 3.0 ref types split into two universes with different CLR representations:

**Funcref / externref** — identified by `Value.Type` and `Value.Data.Ptr` (funcidx for funcref, externidx for externref). Optional `Value.GcRef` caches a delegate (`DelegateRef`) for cross-module dispatch. These **stay as `Value`** everywhere — the CIL stack issue does not apply because the payload is primarily a long, not a managed ref field used at merge points.

**GC refs** (anyref, eqref, structref, arrayref, i31ref, none, concrete struct/array types) — identified by the CLR object reference. At function signature and storage boundaries, wrapped as `Value` with `GcRef` pointing to the CLR object. On the internal CIL stack, **the object travels directly** (no `Value` wrapper). This is the crux of phase 1.

**exnref** — a `WasmException` CLR reference on the internal stack and in locals. At storage / signature boundaries the interpreter-facing form is a `Value` with `GcRef = ExnInstance`; boundary wrap is not yet plumbed for exnref-in-signatures (rare in practice). `IsExnRefType(ValType)` identifies this category distinctly from GC refs.

### 2.5 Null refs

- **Funcref null**: `Value` with `Type = FuncRef`, `Data.Ptr = -1`. Detected via `Value.IsNullRef`.
- **GC ref null**: internal CIL stack — plain `null`. At a Value boundary — `Value` with a null-ref type and `GcRef = null`.
- **Exnref null**: CLR `null`.

`ref.null t` emits:
- GC types: `ldnull` (object on stack) — planned.
- funcref: `ldc.i4 0x6F; call Value.Null(ValType)` (Value on stack).
- externref: similar to funcref.

### 2.6 Identity

- **GC ref identity**: CLR reference identity. `ref.eq` on two GC refs reduces to `ceq` on objects (or `ReferenceEquals`).
- **i31 identity**: structural — `i31.new(x)` is equal to `i31.new(y)` iff `x == y` (masked to 31 bits). i31 refs currently round-trip as boxed `int` (planned refinement: `I31Ref` wrapper or tagged-pointer encoding).
- **Funcref identity**: `Data.Ptr` equality within a module; across modules, must compare the bound delegate (`DelegateRef`).
- **Tag identity**: `TagInstance` reference equality. Imported tags share the exporter's `TagInstance` (wired by linker).
- **Function type identity (for `ref.cast` on funcref)**: structural hash — `FuncTypeHashes[funcidx] == target type hash`.

---

## 3. Function signatures

### 3.1 Shape

```
static ReturnType Method(ThinContext ctx, P1 p1, P2 p2, ..., Pn pn)
```

- `ThinContext` is always `arg 0`.
- Each WASM param becomes one CIL arg.
- `Pi` is the **signature representation** from the table in §2.1.
- Return type: zero results → `void`; one result → the signature representation of that type; multiple results → one result is the return value and the rest are written through `ref`/`out` parameters (see `FunctionCodegen` multi-return handling).

### 3.2 Boundary responsibilities

At function entry, reference-typed params arrive as `Value`. For GC refs, the function body works with `object`. Entry must convert once (e.g., to a shadow local) — body then reads/writes the shadow local.

At function return, reference-typed results must be wrapped back to `Value` before `ret`.

See doc 2 §3 for the wrap/unwrap pattern.

### 3.3 Imported and exported functions

- **Locally defined functions**: emitted as static methods on the generated `Functions` class. Signature per §3.1.
- **Imported functions**: invoked via typed delegates in `ctx.ImportDelegates` (signature does NOT include `ThinContext`). The transpiler calls `Invoke` on the delegate.
- **Exports**: a generated `IExports`-derived interface plus a `Module` class that implements it. Each export method delegates to the static function, prepending `ThinContext` from the module instance.

---

## 4. Locals and params

### 4.1 Mapping

WASM local index space: `[0..paramCount)` are params, `[paramCount..paramCount+localCount)` are locals. CIL mapping:

- Param `i` → CIL `arg (i+1)` (offset by 1 for `ThinContext`).
- Local `i - paramCount` → CIL `local i - paramCount` declared via `ILGenerator.DeclareLocal`.

### 4.2 Local CLR type

Declared type = **internal representation** (per §2.1). This differs from signature representation for GC refs: locals of type `anyref` are `object`, not `Value`.

### 4.3 Shadow locals for ref-typed params

Because CIL args of ref-typed params arrive as `Value`, the function must convert them to `object` before use. Pattern: allocate a "shadow local" of type `object`, initialize from the arg at function entry, and route all `local.get`/`local.set` for that param index through the shadow local rather than through `ldarg`/`starg`.

### 4.4 Zero initialization

WASM requires locals to be zero-initialized per spec. CIL `DeclareLocal` does this for value types (zero) and reference types (null), which matches WASM semantics for all types.

---

## 5. Stack discipline

### 5.1 WASM vs CIL stacks

WASM's stack is validated abstractly at module load time. CIL's evaluation stack is verified by the CLR at JIT time. They are not the same stack — WASM abstract stack is emit-time bookkeeping; CIL stack is runtime state.

**Key invariant:** at every point in emission, the CIL stack must match what subsequent instructions expect. `CilValidator` tracks this at emit time (see doc 3).

### 5.2 CLR verifier constraints

The CLR rejects certain stack shapes at **merge points** (labels where multiple paths converge, including branch targets, loop headers, block end labels, and catch handlers):

1. **Structs containing managed refs** cannot sit on the CIL stack at a merge point. `Value` has `IGcRef? GcRef`, so it hits this restriction.
2. **Typed references to ByRef-like types** cannot be held in certain contexts.

Mitigation patterns: label shuttle locals, object-on-stack for GC refs. See doc 2 §1, §2.

### 5.3 Stack height and excess

`StackAnalysis` precomputes two things per instruction:
- `StackHeightBefore` — the CIL stack height when this instruction starts (in units of logical WASM operands).
- `Excess` — for branches: how many values on the stack are "dead" below the carried label operands and must be popped before branching.

The emitter trusts these values; they are the authoritative pre-pass.

---

## 6. Control flow

### 6.1 Block/loop/if

Each opens an `EmitBlock` entry on a stack used during emission. Key fields:

- `BranchTarget : Label` — branches to this depth target this CIL label. For blocks/ifs: the end label. For loops: the start label (backward branch).
- `LabelArity : int` — count of values the label carries (block result arity; loop param arity).
- `ResultClrTypes : Type[]` — the CIL types each carried value has.
- `ResultLocals : LocalBuilder[]?` — shuttle locals used when carried values are `Value` (see doc 2 §1). Null when the label doesn't need shuttle.

Emission:

```
block T*→U*:
  (allocate ResultLocals[U*] if shuttle needed)
  <body>
  (if body-end reachable and shuttle used: store to ResultLocals; Br end)
  MarkLabel(end)
  (if shuttle used: reload ResultLocals onto stack)

loop T*→U*:
  MarkLabel(start)
  <body>
  // Labels at loop depth 0 branch BACK to start, carrying the T* params
  // (no result shuttle — loop labels don't carry results)

if T*→U* [else]:
  (compile condition — i32 on stack)
  Brfalse else_label  // or end if no else
  <if body>  — shuttle + Br end if reachable
  MarkLabel(else)
  <else body>  — shuttle + Br end if reachable
  MarkLabel(end)
  (reload ResultLocals)
```

### 6.2 br / br_if / br_table

All three use `StackAnalysis.Excess` to know how many values to pop before branching.

- **`br L`**: clean excess (shuttle carried past excess dead values if needed, then pop), then `Br target`. If target has `ResultLocals`, store carried values there first and `Br` with an empty stack.
- **`br_if L`**: pop condition (i32). If branch is taken and there's excess or shuttle: save carried to temps, pop excess, either `Br` with shuttle or restore carried and `Br`. Otherwise simple `Brtrue`.
- **`br_table L* Ldefault`**: pop index (i32). If targets all have the same excess and no shuttle, one cleanup + `Switch`. Otherwise per-target trampolines (each does its own excess cleanup and shuttle store).

### 6.3 return and return_call*

`return` is equivalent to `br` to the outermost block (function-level). Implementation: targets `funcEndLabel`, emits `Ret`.

`return_call`, `return_call_indirect`, `return_call_ref` use the CIL `Tailcall.` prefix before `Call`/`Calli`/`Callvirt`, followed by `Ret`. The JIT may honor or ignore the hint; honoring it avoids stack growth for recursive WASM code. `TranspilerOptions.EmitTailCallPrefix` defaults to true.

### 6.4 br_on_null / br_on_non_null

Pop ref. If null (or non-null, respectively) and the predicate matches, branch to target; else push ref back. Cannot use `Dup` of `Value` at a merge — use a saved local.

For GC refs (object on stack), the pattern simplifies: `stloc ref; ldloc ref; brfalse/brtrue skipLabel; ...branch path...; mark skip; ldloc ref`.

### 6.5 br_on_cast / br_on_cast_fail

Pop ref, test heap type. If test matches (fails, respectively), branch carrying the ref (possibly narrowed); else fall through with the original ref. Same save-to-local pattern as br_on_null. The narrowed type flows at runtime as the same object reference — only the validator's tracked type changes.

---

## 7. Calls

### 7.1 Direct call

`call funcidx`:

- If `funcidx < importCount`: load the delegate from `ctx.ImportDelegates[funcidx]` and `Callvirt Invoke`. Ref-typed args require boundary wrap (object → Value) before the call. Ref-typed results require boundary unwrap (Value → object) after.
- If `funcidx >= importCount`: emit `Ldarg.0` (ThinContext), then the args (wrapping refs to Value), then `Call Static_Method`. Unwrap ref results after.

### 7.2 call_indirect

`call_indirect tableidx typeidx`:

- Pop i32 index. Bounds check against `ctx.Tables[tableidx]`. Type check: look up element's function type (via `DelegateRef` target's method info or `FuncTypeHashes`); trap if mismatch.
- Wrap ref args to Value, invoke the delegate, unwrap ref results.

### 7.3 call_ref

`call_ref typeidx`:

- Pop funcref (Value). Null check → trap.
- Extract `DelegateRef.Target` (bound delegate). Type check by delegate's signature matching `typeidx`.
- Wrap/unwrap as for direct call.

### 7.4 Tail calls

`return_call*` — same lookup and boundary handling, but the CIL sequence is `Tailcall; Call; Ret` (or the indirect/virtual variants). Tail call prefix requires the callee's signature to match the caller's return type, which holds because WASM validation enforces it.

---

## 8. Memory

Linear memory is `ctx.Memories[memidx].Memory` — a byte buffer. Loads and stores use `System.Runtime.CompilerServices.Unsafe` or direct pointer arithmetic via `MemoryInstance.MemoryData`.

- **Loads**: pop address (i32 or i64 for memory64), add offset, bounds check, emit typed load instruction. Return typed value.
- **Stores**: pop value, pop address, bounds check, emit typed store.
- **`memory.size`** / **`memory.grow`** / **`memory.fill`** / **`memory.copy`** / **`memory.init`** / **`data.drop`**: dispatch to `MemoryInstance` helpers.

Memory access is trap-checked at runtime; the transpiler does not statically prove bounds.

---

## 9. Tables

Tables are `List<Value>` under `TableInstance.Elements`, holding typed references (funcref or externref or GC ref depending on table type).

- **`table.get tableidx`**: pop index, bounds check, read `Elements[idx]`. At the signature/storage boundary this is `Value`. If the table is a GC ref type, unwrap Value → object at the boundary for internal CIL stack.
- **`table.set tableidx`**: pop value, pop index, bounds check. For funcref, enrich `val.GcRef = new DelegateRef(ctx.FuncTable[funcidx])` so cross-module dispatch works.
- **`table.size`** / **`table.grow`** / **`table.fill`** / **`table.copy`** / **`table.init`** / **`elem.drop`**: helper calls.

---

## 10. Globals

`ctx.Globals[globalidx].Value` holds a `Value`. Mutable globals are set via the `Value` property (which updates internal state consistently). Scalar globals extract the typed field from the `Value` after loading. Ref-typed globals require boundary wrap/unwrap.

---

## 11. GC instructions

### 11.1 Type emission

Each WASM struct/array type becomes a CLR class at transpile time (`GcTypeEmitter`):

- Struct: class with named fields (`field_0`, `field_1`, ...) of the field's CLR type.
- Array: class with `elements : T[]` and `length : int`.
- `TypeCategory : int` static field — 0 for struct, 1 for array. Used for abstract-type `ref.test`/`ref.cast`.
- `StructuralHash : int` static field — canonical hash across modules. Used for `ref.test`/`ref.cast` on concrete type indices when direct CLR subtype check fails.

Subtyping is represented by CLR inheritance where possible (intra-module). Cross-module structural equivalence uses `StructuralHash`.

### 11.2 struct.new / struct.new_default

```
// struct.new $T
// (stack: [field_0, field_1, ..., field_{n-1}])
spill fields to temps (reverse)
Newobj $T()
for each field: Dup; Ldloc temp; Stfld $T.field_i
// Result: object on internal CIL stack (no Value wrap)
```

`struct.new_default` just emits `Newobj` (CLR zero-init).

### 11.3 struct.get / struct.get_s / struct.get_u / struct.set

Ref on CIL stack as object. `Castclass $T`, then `Ldfld`/`Stfld`. Packed fields require sign/zero extension (`Conv.I1; Conv.I4` for signed, `Conv.U1` for unsigned).

### 11.4 array.new / array.new_default / array.new_fixed

Analogous to struct.new. `array.new_fixed` unrolls N element pushes into N stores. `array.new_default` zero-initializes the elements array.

### 11.5 array.new_data / array.new_elem

Dispatched through runtime helpers (`GcRuntimeHelpers.ArrayNewData`, `ArrayNewElem`). These helpers read the data segment or element segment and construct the typed array, respecting the element type. Element conversion between interpreter GC types (`StoreArray`, `StoreStruct`) and emitted CLR types happens here for mixed-mode scenarios.

### 11.6 array.get / array.set / array.len

- `array.get`: `Castclass $T`; `Ldfld elements`; `Ldloc idx`; `Ldelem element_clr_type`. Sign-extend for packed.
- `array.set`: similar with `Stelem`.
- `array.len`: field load through a helper that doesn't need to know the concrete type (walks to `length` field).

### 11.7 array.fill / array.copy / array.init_data / array.init_elem

Helper calls. Bounds checks inside the helper.

### 11.8 ref.test / ref.cast

Emit a helper call that dispatches through three layers:

- **Layer 0**: direct `IsInstanceOfType` on the target CLR type (handles intra-module subtyping via CLR inheritance).
- **Layer 1**: structural hash walk up the CLR type's base chain, comparing `StructuralHash` fields. Handles cross-module equivalence.
- **Layer 2**: funcref — compare `FuncTypeHashes[funcidx]` against the target function type's hash.
- Abstract heap types (`any`, `eq`, `struct`, `array`, `i31`, `func`, `extern`, `none*`) dispatch on `ValType` / `TypeCategory`.

`ref.cast` wraps `ref.test`: on failure, trap. On success, push the (possibly narrowed) ref.

### 11.9 br_on_cast / br_on_cast_fail

See §6.5.

### 11.10 ref.i31 / i31.get_s / i31.get_u

i31 refs are boxed `int` on the internal CIL stack (planned). Storage form is `Value` with `GcRef = I31Ref(masked_int)`. Sign/unsigned get extracts the 31-bit value and sign-extends (or zero-extends) to i32.

### 11.11 extern.convert_any / any.convert_extern

Re-tag the ref's `ValType`. The CLR object is the same; only the WASM type label changes. At internal stack this is effectively a no-op since `object` has no type tag — but the boundary wrap on return must re-tag the `Value`.

---

## 12. Reference ops

### 12.1 ref.null

See §2.5.

### 12.2 ref.is_null

- Funcref: `Value.IsNullRef` check via helper.
- GC ref (object on stack): `ldnull; ceq`.

### 12.3 ref.func funcidx

Pushes `Value` with `Type = FuncRef`, `Data.Ptr = funcidx`, and (when available) `GcRef = DelegateRef(ctx.FuncTable[funcidx])`. In standalone mode, the helper binds the delegate; in framework mode, it resolves through `ctx.Module.FuncAddrs`.

### 12.4 ref.eq

- Both null → 1.
- One null → 0.
- Both non-null: reference equality on the underlying CLR object (for GC refs) or `Data.Ptr` equality (for funcref).

### 12.5 ref.as_non_null

Pass-through for non-null; trap on null.

---

## 13. Exceptions

### 13.1 Identity

WASM exceptions are identified by their **tag**. Two exceptions share type iff they carry the same tag. The transpiler represents tag identity as CLR reference equality on `TagInstance`. Imported tags share the exporter's `TagInstance` (linker wires this).

### 13.2 Exception object

The runtime exception is `WasmException : Exception` with:

- `Tag : TagInstance`
- `Fields : Value[]` — the exception payload (one slot per tag param type)

### 13.3 throw tagidx

```
gather N field values from CIL stack into Value[N]
  (for ref-typed fields: wrap object → Value)
newobj WasmException(ctx.Tags[tagidx], fields)
throw
```

### 13.4 throw_ref

```
// stack: [exnref]
// exnref is WasmException on internal stack, or Value at boundary
null check → TrapException("null exception reference")
throw
```

### 13.5 try_table

Uses CLR native structural exception handling:

```
BeginExceptionBlock
  <body>
  Leave end
BeginCatchBlock(WasmException)
  Stloc exn
  for each catch clause:
    case catch / catch_ref:
      if exn.Tag == ctx.Tags[handler.X]:
        store clause index, Leave dispatch_i
    case catch_all / catch_all_ref:
      always match, Leave dispatch_i
  Rethrow  // no clause matched
EndExceptionBlock

dispatch_i:
  for catch/catch_ref: unpack exn.Fields onto stack (wrap refs as needed for the enclosing context)
  for catch_ref/catch_all_ref: push the WasmException (as exnref) last
  Br enclosing_catch_label
```

**Branches out of try-regions must use `Leave`, not `Br`.** The transpiler tracks `_tryDepth` to decide. Branches into a try-region are illegal (WASM doesn't allow them; CLR doesn't either).

### 13.6 Alignment with CLR

- WASM `throw` maps to `throw` on a `WasmException` constructed with the tag and fields.
- WASM's unwind semantics match CLR's — finally-ish behavior (not expressible in WASM) would require explicit construction.
- Nested try_tables nest as nested `BeginExceptionBlock`/`EndExceptionBlock`.
- Trap exceptions (`TrapException`) are **not** WASM exceptions — they are engine-level and must not be caught by `try_table`. The catch clause filters on `WasmException` only; traps propagate out.

---

## 14. Special cases and idiosyncrasies

### 14.1 Interpreter interop (mixed mode)

When `ctx.Module != null`, the module may have been loaded through the WACS framework and can coexist with interpreted modules. Implications:

- Table elements, global values, exception payloads may contain interpreter GC types (`StoreArray`, `StoreStruct`) instead of emitted CLR types. Conversion helpers (`ConvertStoreArray`, `ConvertStoreStruct`) bridge them at access points.
- Funcrefs stored in tables may point to interpreter functions; the delegate wrapper handles dispatch.
- The transpiler emits identical IL regardless of mode; helpers branch on `ctx.Module != null`.

### 14.2 Standalone mode

When `ctx.Module == null`, the module runs without an interpreter Store. All state is in `ThinContext` directly. Helpers that would have dispatched through `Store` must have a standalone path.

### 14.3 Functions that fall back to interpreter

If any instruction in a function cannot be emitted, `FunctionCodegen.TryEmit` returns false and the function is invoked via the interpreter's `TranspiledFunction` wrapper. The wrapper allocates an OpStack and calls the interpreter's evaluation loop for that function. Mixed emission across a module is allowed (some functions transpiled, others interpreted).

### 14.4 Singleton instructions

Many interpreter instructions are shared singleton instances (e.g., `InstReturn.Inst`, `InstDrop.Inst`). `StackAnalysis` keys info by instruction reference, so singletons across multiple emission sites would collapse — `StackAnalysis` therefore uses `Queue<InstructionInfo>` per instruction, dequeuing once per use. Emission order must match analysis order.

### 14.5 Labels for branches that cross ResultLocals

When a branch target has `ResultLocals`, the branch path must store the label's operands to those locals before emitting `Br`, leaving the eval stack empty at the label merge. The label header loads the locals back onto the stack for the downstream code.

### 14.6 Value.Equals / GetHashCode

The `Value` struct is used as a map/set key in some helpers. Its equality considers `Type`, `Data`, and `GcRef`. Transpiler code should avoid `Value.Equals` for ref identity — use `ReferenceEquals` on the underlying object instead.

---

## 15. Out of scope for this document

- Transpiler initialization (`InitializationHelper`, `ModuleInit`) — covered in source comments.
- `InterfaceGenerator` / `ModuleClassGenerator` — covered in source comments.
- Diagnostics and error reporting — covered in source.
- Cross-module linker details — see `ModuleLinker`.

These are documented in source because they don't influence per-instruction emission semantics.
