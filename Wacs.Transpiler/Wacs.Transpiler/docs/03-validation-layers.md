# Doc 3 — Validation Layers

**Prerequisite:** doc 1 (mapping) and doc 2 (patterns). This document defines the responsibility split between the three validation tiers: the WASM validator (in Wacs.Core), the transpiler's emit-time CIL tracker (`CilValidator`), and the runtime trap checks the transpiler emits.

Understanding the split matters because it says what the transpiler can assume versus what it must verify. Over-verifying is waste; under-verifying is bugs.

---

## 1. Tier A — WASM validator (Wacs.Core)

### What runs

`Wacs.Core.Validation.ModuleValidator` plus instruction-level validators attached to each `InstructionBase`. Runs once at module load, before transpilation. Produces a pass/fail verdict; rejected modules never reach the transpiler.

### What it verifies

The full WASM 3.0 spec validation:

- **Type indices exist** — every `TypeIdx`, `FuncIdx`, `TableIdx`, `MemIdx`, `GlobalIdx`, `TagIdx`, `LocalIdx`, `LabelIdx` is in range.
- **Abstract stack types match** — every instruction's abstract stack effect is checked against declared types. Blocks, loops, ifs produce the right types.
- **Branch targets are valid** — label depths don't exceed block stack, and the label's carried arity matches what's on the stack.
- **Function types** — declared param and result types are well-formed.
- **Tables / memories / globals / data / element segments** — well-formed at module scope.
- **GC type definitions** — recursive groups resolve, subtype declarations are consistent, field types are valid.
- **Imports / exports** — types match declared import/export signatures.
- **Constant expressions** — initializers use only constant-eligible instructions.
- **Reachability / dead-code typing** — after an unconditional terminator, the stack is polymorphic (any type satisfies until the next label merge).

### What the transpiler inherits

All of the above. **The transpiler may assume**:

- Every instruction it sees in a reachable position has the right operands on the abstract stack.
- Every type index resolves.
- Every branch target is valid and carries the declared arity.
- Every block/loop/if is properly nested and terminated.
- Field types and subtyping are valid.

### What the transpiler must NOT assume

Tier A validates at the **abstract** WASM level. It does not verify:

- CIL representation decisions (e.g., Value vs object for a GC ref) — that's Tier B.
- Runtime bounds, null refs, division by zero, cast failures — that's Tier C.
- Interpreter interop correctness — not a validator concern.

---

## 2. Tier B — CilValidator (emit-time CIL type tracker)

### What it is

`CilValidator` is an **emit-time** tracker of the CIL evaluation stack's type state. It does not run at runtime; it runs inside emitters as they generate IL, asserting that the CIL types match what later instructions will expect.

### Purpose

The mechanical translation from WASM to CIL is not always type-preserving by construction. Bugs in the translator (wrong helper called, missing conversion, incorrect operand order, stale representation choice) produce IL that might fail at JIT time (`InvalidProgramException`) or — worse — that JITs successfully but executes wrong semantics.

`CilValidator` catches these in the emission step. If the emitter pushes the wrong CLR type, the next `Pop` with an expected type asserts, failing the build of the transpiled function rather than the runtime behavior.

### Height vs type

- **Height is authoritative** from `StackAnalysis` (the pre-pass). At every instruction boundary, `CilValidator.Reset(StackHeightBefore)` syncs the height.
- **Types are asserted within emitter scopes**. An emitter knows exactly what CLR types it pops and pushes; it calls `cv.Pop(typeof(X), "context")` before each consumption and `cv.Push(typeof(Y))` after each production.
- **Type preservation across Reset**. When the prior stack height already matches the requested reset height, `Reset` preserves the existing types rather than replacing with placeholders. This lets the next instruction's emitter consult `Peek()` to resolve representation-sensitive branches — notably `br_on_null` / `ref.is_null` / `ref.eq` / `br_on_cast` / `select` dispatch on whether the top is `typeof(object)`, `typeof(WasmException)`, or `typeof(Value)`.

Height stays consistent across compound instructions; types are re-established at each instruction's pre-pass reset.

### Unreachable state

After a terminator (return, br, unreachable, throw, return_call*), the validator enters unreachable state. Further pushes/pops are no-ops until the next instruction boundary resets from `StackAnalysis`. This matches Tier A's polymorphic-stack rule.

### What it verifies

1. **Stack never underflows within an emitter scope.** If an emitter tries to Pop with Height = 0, that's a real bug in the emitter.
2. **Popped type matches expected.** If an emitter says `Pop(typeof(int), "add.lhs")` and the top is `typeof(object)`, the emitter used the wrong opcode or representation.
3. **Height matches the pre-pass at each instruction boundary.** Drift signals an emission site that pushed or popped the wrong number of operands.

### What it does NOT verify

- **IL instruction validity** — does not check that `Add` applies to the types on the stack in CLR terms. The CLR JIT does that at load time, and any mismatch produces `InvalidProgramException`.
- **Type compatibility beyond equality** — `Pop(typeof(object))` doesn't know `typeof(WasmStruct_7)` is a subtype. Emitters are responsible for using the right assertion type at each level.
- **Reachability analysis** — trusts `StackAnalysis` and emitter-local terminator calls.

### Relationship to Tier A

Tier B is strictly **downstream** of Tier A. If Tier A rejected the module, Tier B never runs. Tier B verifies that our *translation* of a valid WASM module into CIL is itself well-typed at the CLR level.

### Failure mode

`CilValidator.Fail(msg)` throws `TranspilerException`, which is caught by `FunctionCodegen.TryEmit` and treated as "this function cannot be transpiled — fall back to interpreter." This lets translation bugs degrade gracefully rather than crashing compilation.

---

## 3. Tier C — Runtime trap checks (emitted IL)

### What it is

The WASM spec defines certain operations as trap-on-failure at runtime: memory out-of-bounds, table out-of-bounds, integer division by zero, integer overflow in `div_s` / `rem_s`, `unreachable` instruction, null dereference, cast failure, stack exhaustion. Tier A doesn't check these (they depend on runtime values); Tier B doesn't check them (they're not CLR type issues).

The transpiler emits explicit runtime guards as IL that raises `TrapException` on failure.

### What it emits

- **Memory bounds**: pre-check address + offset + access size against `MemoryInstance.Size`. Emitted inline or through a helper.
- **Table bounds**: `table.get`/`set`/`fill`/`copy`/`init` pre-check indices.
- **Division**: `i32.div_s` / `i64.div_s` emits an `i32.const INT32_MIN_OR_INT64_MIN; int_EQ; ... trap` sequence to catch overflow; all division variants check for zero.
- **Null refs**: `struct.get`/`set`, `array.get`/`set`/`len`/`fill`/`copy`/`init_*`, `call_ref`, `ref.as_non_null`, `throw_ref`, `i31.get_*` null-check the ref.
- **`array.new_data` / `array.new_elem`**: bounds-check the source segment.
- **`call_indirect`**: bounds-check the index, null-check the slot, type-check the function signature.
- **Cast failures**: `ref.cast` calls `RefTestValue`; on false, trap.
- **Unreachable**: emits `ldstr; newobj TrapException; throw`.
- **Stack guard**: function entry emits a `TryEnsureSufficientExecutionStack` check to trap before CLR StackOverflow kills the process.

### What it does NOT emit

- **Checks Tier A already did.** E.g., the transpiler does not emit a type check for `call funcidx` because Tier A verified the funcidx is in range and the function type matches.
- **Redundant null checks.** If a value flows from a non-null-producing operation (e.g., `ref.cast T` where T is non-nullable), later null-sensitive operations don't need to re-check. Currently the emitter does not track this and always checks; this is safe but potentially redundant.

### Trap identity

All runtime traps are `TrapException`. The transpiler never throws `WasmException` for traps (that would be catchable by `try_table`). `TrapException` is caught by the host (test runner or embedder) as the WASM trap return.

---

## 4. Tier B representation map

Tier B tracks the internal CIL stack types established by docs 1–2:

- **`typeof(object)`** — GC ref values: struct, array, i31, any, eq, none, concrete struct/array types (all via `MapValTypeInternal`).
- **`typeof(Value)`** — funcref, externref, v128, concrete function types. Also every boundary transition: call args/results wrap, signature entry before shadow-local unwrap, global/table storage, exception field Value[] elements.
- **`typeof(WasmException)`** — exnref. Pushed after catch_ref / catch_all_ref dispatch, and popped by throw_ref.
- **Typed CLR scalars** — i32/i64/f32/f64 as `int`/`long`/`float`/`double`.

Representation-sensitive emitters (`ref.is_null`, `ref.eq`, `ref.as_non_null`, `br_on_null`, `br_on_non_null`, `br_on_cast`, `select`) call `Peek()` to dispatch — object/WasmException use inline `Ldnull; Ceq` / `Dup; Brfalse` / direct `Throw`; Value uses the struct-safe spill-to-local path plus `RefIsNull`/`RefTestValue` helpers.

---

## 5. Invariants summary

1. **Tier A has already run.** The transpiler trusts every WASM-level type and structure.
2. **Tier B validates CLR types during emission.** If it trips, the emitter is wrong — don't weaken assertions, fix the emitter.
3. **Tier C emits the runtime traps WASM requires.** No more, no less. Skipping a required trap is a correctness bug; emitting an extra one is a performance bug.
4. **CilValidator height is driven by StackAnalysis.** Types are driven by emitter-local knowledge. These two sources don't overlap; they are composed.
5. **Failure in Tier B falls back, not crashes.** Transpile-time bugs should never abort the whole module.

---

## 6. Non-goals

- **No separate CIL-level verifier pass** — we rely on the CLR JIT to catch anything we missed. If the emitted IL is wrong and `CilValidator` didn't catch it, the JIT will.
- **No runtime type assertions in emitted IL** — other than traps mandated by the WASM spec. The CLR does its own checks; adding ours would duplicate.
- **No WASM validation redone in the transpiler** — Tier A is authoritative.
