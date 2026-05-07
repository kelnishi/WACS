# Doc 2 — Structural Patterns

**Prerequisite:** doc 1 (mapping reference). This document describes the recurring patterns used to reconcile WASM semantics with CIL's stricter rules. Each pattern has a stated problem, the solution shape, and invariants that implementations must preserve.

---

## 1. Object on the CIL stack for GC refs

### Problem

`Value` is a struct containing `IGcRef? GcRef` — a managed reference field. The CLR verifier rejects such structs on the evaluation stack **at merge points** (labels, block ends, catch handlers, etc.): ECMA-335 forbids byref-like or ref-containing structs in certain contexts. Empirically this manifests as `InvalidProgramException` or correctness bugs where the `GcRef` field is zeroed out during struct copies through the stack.

### Solution

On the **internal CIL stack** (inside a function body, between instructions) GC refs flow as plain `object`. This sidesteps the merge-point rule entirely because `object` is a reference, not a struct containing one.

At **boundaries** (function signatures, storage, calls, exceptions), GC refs are wrapped in `Value` so they interchange with interpreter-facing APIs.

### Invariants

1. **At every instruction emission, the CIL stack type for a GC ref is `object`.** Never `Value`.
2. **At a signature or storage boundary, the representation is `Value`.** The boundary emitter (call, return, global.set, table.set, exception throw) wraps; its counterpart (call result, function-entry shadow locals, global.get, table.get, exception field unpack) unwraps.
3. **`funcref` and `externref` stay as `Value` everywhere**, including on the internal stack. They do not have the merge-point issue because `Data.Ptr` (a long) is the primary carrier; the optional `DelegateRef` in `GcRef` is used for cross-module dispatch only.
4. **`v128` stays as `Value`** internally. It has the same merge-point issue technically but is rarely carried across merge points in the same way (SIMD ops unbox internally), and the current cost of separating it is not justified.

### Scope of "GC ref"

`ModuleTranspiler.IsGcRefType(ValType, ModuleInstance?)` enumerates them: `any`, `eq`, `i31`, `struct`, `array`, `none`, their non-null variants, and concrete **struct/array** type indices.

Explicitly excluded — these have their own representations (doc 1 §2.1):

- `funcref`, `func`, `nofunc`, `nofuncNN`, concrete **function** type indices — flow as `Value` (funcref encoding).
- `externref`, `extern`, `noextern`, `noexternNN` — flow as `Value`.
- `exnref`, `noexn` — flow as `WasmException` (caught by `IsExnRefType`, handled before `IsGcRefType`).

Concrete type indices require the `ModuleInstance` parameter to disambiguate: if the type's expansion is a `FunctionType`, `IsGcRefType` returns false. Without the module, concrete-type refs are conservatively classified as GC (object) — call sites should always pass the module when available. `FunctionCodegen` exposes the convenience wrappers `InternalType(ValType)` and `IsGcRef(ValType)` that bind `_moduleInst` automatically.

---

## 2. Label shuttle locals

### Problem

When a label carries one or more values (block results, loop params), multiple paths may branch to it. The CIL verifier merges the eval stack from each incoming path. If the carried type is a struct with a managed ref (`Value`) or any other verifier-problematic shape, the merge fails.

### Solution

A label with problematic carried types owns an array of **shuttle locals**, one per carried value, declared at the block's declaration point. Every branch path must:

1. Store the carried operands into the shuttle locals (top-of-stack first, so locals index 0 = first operand).
2. Pop any excess values below the carried operands.
3. Emit `Br` with an empty eval stack.

The label header then loads the shuttle locals back onto the stack in normal order.

### Triggers

1. **Value type at merge point.** `LabelShuttle.NeedsLocals(Type[] carriedTypes)` returns true when any carried type is `typeof(Value)` — e.g., v128 or funcref/externref labels.

2. **Cross-try label with arity > 0** (stage 3 WI-4). If the function body contains any `try_table`, branches from inside the try region to a label outside it emit `Leave`, which empties the eval stack. Shuttle locals rendezvous operands independently of the eval stack. `FunctionCodegen.TryEmit` scans for `InstTryTable` and passes `forceShuttle=true` to `ControlEmitter.EmitBlock`/`EmitIf` when found — over-allocating for cold IL simplicity, rather than per-label liveness analysis.

Loops are excluded from force-shuttle: their label arity represents params on the backward branch, and the back-edge is always within the loop body.

### Invariants

1. **Shuttle locals are allocated at the block's start**, shared by fall-through and all branches to that label.
2. **Every path reaching the label must either populate the shuttles or be provably unreachable.** Dead paths after an unconditional terminator (`return`, `br`, `unreachable`, `return_call*`) do not need to populate.
3. **The label header always reloads from shuttles** when present — downstream code reads carried operands from the eval stack unchanged.
4. **Loops never use shuttles** — loop labels carry params (backward branch), and the back edge has the same shape as the forward fall-through.

---

## 3. Boundary wrap / unwrap

### Problem

Signatures and storage use `Value`. The internal stack uses `object` for GC refs. Something must translate.

### Solution

At each boundary, emit:

- **object → Value (wrap)**: produce a `Value` whose `GcRef` is the object, with a type tag derived from the object's `TypeCategory` (or from the declared target type when known statically).
- **Value → object (unwrap)**: extract `Value.GcRef`, unwrap `GcObjectAdapter` (for plain CLR objects not implementing `IGcRef`) to the underlying object, return as `object`. Null `Value` → `null`.

These go through runtime helpers (`GcRuntimeHelpers.WrapRef`, `GcRuntimeHelpers.UnwrapRef`) which are tolerant of mixed-mode (interpreter GC types wrapped in adapter).

### Boundary sites (wrap direction)

- Function return for ref results: stack has object, need Value → wrap.
- Call arguments for ref params: stack has object, delegate expects Value → wrap per arg.
- `global.set` for ref globals: stack has object, global expects Value → wrap.
- `table.set` for ref tables: stack has object, table stores Value → wrap.
- `throw` field collection for ref fields: stack has object, `Value[]` expects Value → wrap per field.
- `struct.set` / `array.set` for a ref-typed field/element: stack has object, but the struct field is `Value` — **except** when we emit the field as the underlying CLR type directly (see §4).

### Boundary sites (unwrap direction)

- Function-entry shadow locals for ref params: arg is Value → unwrap to object once at entry, body reads shadow local.
- Call results for ref returns: delegate returned Value → unwrap to object.
- `global.get` for ref globals: global loaded Value → unwrap to object.
- `table.get` for ref tables: table returned Value → unwrap to object.
- `try_table` catch dispatch for ref fields: Value[] entry → unwrap to object before pushing.
- `struct.get` / `array.get` for ref-typed field/element: depends on field representation (see §4).

### Invariant

**Every boundary is symmetric.** If you wrap on one side of an edge, you must unwrap on the other side. If either side is missing, the stack type drifts and later instructions fail validation.

---

## 4. Storage representation choices

### GC struct/array fields and array elements

Emitted GC types have typed fields (`field_0 : int`, `field_1 : byte`, `field_2 : WasmStruct_7`, ...) and typed array element arrays (`elements : WasmArray_3[]`). Reference-typed fields can be stored **either** as `Value` (interop-compatible) or as the typed CLR reference directly.

The current choice: **typed CLR reference directly** when the emitted type is known. This avoids per-access wrap/unwrap for refs. Access emits `Ldfld`/`Stfld` with the field type directly.

Consequences:

- `struct.get` on a ref field: `Castclass $T; Ldfld field_i` pushes an object of the field's CLR type. No unwrap needed — the field is already an object.
- `struct.set` on a ref field: stack already has object; `Stfld field_i` stores it.
- If the set value's static type is broader than the field type, a `Castclass` before `Stfld` is required.
- When crossing to interpreter code that expects `Value`-backed fields (e.g., `StoreStruct`), conversion happens inside helpers, not in emitted IL.

### Table elements and global values

Stay as `Value` — these are interpreter-facing shared state. Tables and globals mix funcref, externref, and GC ref types; using `Value` uniformly simplifies the runtime layer and enables cross-engine compatibility.

---

## 5. Tag identity

### Problem

WASM exceptions are identified by the tag that threw them. Tags are addressable across modules: if module A imports a tag exported by module B, thrown exceptions from A must catch in B's handlers for the same tag. Tag identity must be stable across module boundaries.

### Solution

- Each tag is represented by a `TagInstance` reference (core interpreter type, reused per user direction).
- `ctx.Tags[tagidx]` resolves a tagidx to its `TagInstance`. The linker wires imported tags to point to the exporter's `TagInstance` — reference equality then is tag equality.
- `throw tagidx` constructs `new WasmException(ctx.Tags[tagidx], fields)`. `try_table` catch clauses compare `ex.Tag == ctx.Tags[expected_tagidx]` by reference.

### Invariants

1. **Never allocate a fresh `TagInstance` at a catch site or throw site** — always go through `ctx.Tags[tagidx]`.
2. **Imported tags share the exporter's `TagInstance`.** The linker guarantees this; the transpiler does not need to dedupe.
3. **A module's local tags have distinct `TagInstance` objects** allocated once at initialization.

---

## 6. Function type identity

### Problem

`ref.cast T` on a funcref must verify that the function's type matches `T`. Because funcrefs carry only a funcidx (plus an optional bound delegate), the identity check must reach into the function's type signature.

### Solution

`ctx.FuncTypeHashes : int[]` holds a structural hash per funcidx. Hashing canonicalizes over-the-wire type representations so two functionally equivalent types in different modules produce the same hash. `ref.cast T` computes the target type's hash and compares.

### Invariants

1. **The hash is computed from the `FunctionType` expansion**, not the `TypeIdx` number.
2. **Null funcrefs** never pass cast.
3. **Imported funcs** must populate `FuncTypeHashes` from the import's declared type at link time.

---

## 7. Excess cleanup for branches

### Problem

A branch targets a label at depth N. Between the branch's position and the label, there may be `excess` values on the CIL stack below the label's carried operands that must be popped — they are the residue from instructions that would have executed if fall-through continued.

Example:
```
block (result i32)
  i32.const 1   ;; stack: [1]
  i32.const 2   ;; stack: [1, 2]
  br 0          ;; target expects [i32] — the label arity is 1
                ;; excess = 1 (the 1 underneath must be popped)
```

### Solution

`StackAnalysis.Excess` precomputes the excess count per branch instruction. `EmitExcessCleanup` handles the three cases:

1. **Target uses shuttle locals**: store arity values to shuttles, pop excess, Br.
2. **No shuttle, excess > 0**: spill arity values to temps, pop excess, reload arity, Br (leaves operands on stack).
3. **No shuttle, excess = 0**: just Br.

### Invariant

**Never Br with the eval stack in a shape the target label won't accept.** The analysis is authoritative — emit code that exactly implements it.

---

## 8. Unconditional terminators and reachability

### Problem

After `return`, `br`, `unreachable`, or `return_call*`, control cannot reach the next instruction. WASM's validator treats subsequent code as validation-dead (any types allowed). CIL's verifier similarly doesn't require stack consistency after `Ret`/`Br`/`Throw`. But any fall-through into dead code that *would* be merged into a label requires correct handling.

### Solution

- `CilValidator.SetUnreachable()` marks the validator unreachable; subsequent pushes/pops are skipped until the next instruction boundary re-syncs from `StackAnalysis`.
- `BodyEndIsReachable(seq)` walks backward over a block body, ignoring structural `End`/`Else` markers, and checks whether the last real instruction is an unconditional terminator. Used to decide whether to emit the final shuttle-store + Br at a block's end.

### Invariant

**Don't emit a shuttle-store + Br for an unreachable fall-through.** Doing so inserts IL the verifier would reject or flagging a spurious branch into a stack shape that never actually arises.

---

## 9. Standalone vs interpreter-backed runtime

### Two modes

- **Standalone**: `ctx.Module == null`, `ctx.Store == null`, `ctx.ExecContext == null`. All state is directly in `ThinContext`. No interpreter is present.
- **Interpreter-backed (mixed mode)**: `ctx.Module != null`. The module can coexist and interoperate with interpreted modules via the WACS framework. `Store`, `ExecContext`, `Module` are populated.

### Pattern

Runtime helpers that can use interpreter features (e.g., `Module.FuncAddrs` for canonical funcref encoding) MUST check `ctx.Module != null` and have a standalone code path. Helpers that don't touch Module/Store/ExecContext work identically in both modes.

### Invariant

**No emitted IL uses `ctx.Module.X` or `ctx.Store.X` directly.** All such access goes through helpers that branch on `ctx.Module != null`. This keeps the emitted code mode-agnostic.

---

## 10. Shared singleton instruction handling

### Problem

The interpreter uses shared singleton instances for stateless opcodes (`InstReturn.Inst`, `InstDrop.Inst`, etc.). Multiple sites in a function refer to the same object. Dictionaries keyed by reference identity collapse these into a single entry.

### Solution

`StackAnalysis` stores info in `Dictionary<InstructionBase, Queue<InstructionInfo>>`. Each use dequeues. Emission order must match analysis order — the pre-pass and emit-pass walk the same tree in the same order, so the queue produces the right info at each site.

### Invariant

**Emission is single-pass and in-order.** Never skip an instruction during emission, or the queue desynchronizes.

---

## 11. Fallback to interpreter for unsupported instructions

### Pattern

`FunctionCodegen.TryEmit` first runs `CanEmitAllInstructions` to verify coverage. If any instruction lacks an emitter, returns false. The caller (`ModuleTranspiler`) then marks that function as a fallback — it is invoked via `TranspiledFunction`, a wrapper that sets up an interpreter frame and dispatches to the interpreter's evaluation loop.

### Invariant

**Transpiled and fallback functions in the same module must be callable from each other.** Direct calls from transpiled code to a fallback function should go through the standard delegate path (`ctx.FuncTable[funcidx]`). This is enforced naturally because indirect dispatch via delegate is always correct; direct dispatch via static method requires knowing the target is transpiled.

---

## 12. Type emission for GC types

### Pattern

`GcTypeEmitter.EmitTypes` walks the module's recursive type definitions and emits one CLR class per struct/array type. Key fields:

- `TypeIndex : int` (static) — WASM type index.
- `TypeCategory : int` (static) — 0 = struct, 1 = array. Used for abstract-type `ref.test`/`ref.cast`.
- `StructuralHash : int` (static) — canonical hash for cross-module subtype checks.
- Fields: named `field_0`, `field_1`, ... with the field's CLR type.
- Array element backing: `elements : T[]`, `length : int`.

Concrete subtyping (within a module) uses CLR inheritance when the WASM subtype relationship permits it. Recursive types are broken via placeholders and patched after all types are emitted.

### Invariant

**A GC type's CLR identity never changes once emitted.** All references to the emitted CLR type anywhere in a module's IL must use the same `Type` object.

---

## 13. Cross-engine GC type conversion

### Problem

In mixed mode, interpreter-facing APIs (table get, global value, exception fields) may contain `StoreArray`/`StoreStruct` (interpreter GC types) rather than the transpiler's emitted classes. Direct `Castclass WasmStruct_N` on such a value fails.

### Solution

`GcRuntimeHelpers.UnwrapRef` detects interpreter types and converts them on demand. `ConvertStoreArray`/`ConvertStoreStruct` create an instance of the target emitted CLR type and copy fields.

### Invariant

**Conversion happens at unwrap (ingress) points, not at emission.** The emitter never inspects whether a value came from the interpreter — it always unwraps and proceeds.

---

## 14. Exception handling with CLR native EH

### Pattern

- `throw` → construct `WasmException(tag, fields)`, `throw`.
- `try_table` → `BeginExceptionBlock` + `BeginCatchBlock(WasmException)` + tag-filtered dispatch via `Leave` to per-clause dispatch labels outside the try/catch.
- Catch clauses match on tag reference equality; non-matching catch clauses fall through to `Rethrow`.

### Special: leave vs br

Branches **out of** a try-region or catch-block must use `Leave`, not `Br`. `FunctionCodegen` tracks `_tryDepth` and the `BranchBridge` helper picks the opcode by comparing `_tryDepth` against each target label's `EmitBlock.OpeningTryDepth`.

```csharp
// BranchBridge
public static OpCode BranchOpFor(EmitBlock target, int currentTryDepth)
    => currentTryDepth > target.OpeningTryDepth ? OpCodes.Leave : OpCodes.Br;
```

Because `Leave` empties the eval stack, cross-try label targets must carry their operands via shuttle locals (see §2 trigger 2). `EmitExcessCleanup` handles both cases: when the target has `ResultLocals`, values are stored there before the branch opcode is emitted (leaving the stack empty); otherwise values stay on the stack (valid for plain `Br`).

### Invariant

**`WasmException` is the only CLR exception type the transpiler catches.** `TrapException` and any other exceptions propagate untouched.

---

## 15. Shadow locals for ref-typed params

### Pattern

At function entry, for each ref-typed param:

```
ldarg i+1                // Value on stack
call GcRuntimeHelpers.UnwrapRef(Value) → object
stloc shadow_i           // object stored in shadow local
```

Subsequent `local.get i` loads `shadow_i`. Subsequent `local.set i` stores to `shadow_i` (wrapping object back to Value if the CIL arg also needs updating for later re-reads — in practice we do not re-wrap to the arg because nothing reads the arg after the shadow local is live).

### Invariant

**After initialization, never `Ldarg i+1` for a ref-typed param again.** The shadow local is authoritative.

---

## 16. Runtime helpers are the sole cross-mode bridge

All `GcRuntimeHelpers.*`, `TableRefHelpers.*`, `SelectHelpers.*`, `ExceptionHelpers.*` functions are the single point where mode (standalone vs mixed) is handled. Emitters never branch on mode. This keeps emitted IL uniform and the mode-sensitive logic in one place.
