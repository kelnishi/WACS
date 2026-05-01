# WebAssembly 3.0 Spec Coverage — WACS

Reference: WebAssembly Specification Release 3.0 (2026-04-09)

Legend: [x] implemented, [~] partial, [ ] not implemented, [n/a] not applicable

---

## 1. Introduction (Spec Ch. 1)

Not directly implementable — design goals and scope documentation.

---

## 2. Structure — Abstract Syntax (Spec Ch. 2)

### 2.2 Values
| Feature | Status | WACS Location |
|---------|--------|---------------|
| Bytes | [x] | `Utilities/BinaryReaderExtension.cs` |
| Integers (u32, u64, s32, s64, i8, i16, i32, i64) | [x] | `Utilities/BinaryReaderExtension.cs` (LEB128) |
| Floating-point (f32, f64) | [x] | `Runtime/Value.cs`, `Utilities/FloatConversion.cs` |
| Vectors (v128) | [x] | `Runtime/V128.cs` |
| Names (UTF-8) | [x] | `Utilities/BinaryReaderExtension.cs` |

### 2.3 Types
| Feature | Status | WACS Location |
|---------|--------|---------------|
| Number types (i32, i64, f32, f64) | [x] | `Types/Defs/NumType.cs` |
| Vector types (v128) | [x] | `Types/Defs/VecType.cs` |
| Heap types (func, extern, any, eq, i31, struct, array, none, nofunc, exn, noexn, noextern) | [x] | `Types/Defs/HeapType.cs` |
| Reference types (ref null? heaptype) | [x] | `Types/ValType.cs` |
| Value types (numtype, vectype, reftype) | [x] | `Types/ValType.cs` |
| Function types | [x] | `Types/FunctionType.cs` |
| Composite types (func, struct, array) | [x] | `Types/CompositeType.cs` |
| Recursive types | [x] | `Types/RecursiveType.cs` |
| Sub types | [x] | `Types/SubType.cs` |
| Storage types (val, packed i8/i16) | [x] | `Types/StorageType.cs`, `Types/Defs/PackedType.cs` |
| Field types | [x] | `Types/FieldType.cs` |
| Struct types | [x] | `Types/StructType.cs` |
| Array types | [x] | `Types/ArrayType.cs` |
| Limits | [x] | `Types/Limits.cs` |
| Memory types (incl. address type) | [x] | `Types/MemoryType.cs` |
| Table types (incl. address type) | [x] | `Types/TableType.cs` |
| Global types | [x] | `Types/GlobalType.cs` |
| Tag types | [x] | `Types/TagType.cs` |
| External types | [x] | `Types/Defs/ExternalKind.cs` |
| Address types (i32, i64) | [x] | `Types/Defs/AddrType.cs` |

### 2.4 Instructions
| Category | Status | WACS Location |
|----------|--------|---------------|
| Parametric (nop, unreachable, drop, select) | [x] | `Instructions/Parametric.cs` |
| Control (block, loop, if, br, br_if, br_table, call, return, etc.) | [x] | `Instructions/Control.cs` |
| br_on_null, br_on_non_null | [x] | `Instructions/Reference/Control.cs` |
| br_on_cast, br_on_cast_fail | [x] | `Instructions/GC/Control.cs` |
| Tail calls (return_call, return_call_indirect, return_call_ref) | [x] | `Instructions/TailCall.cs` |
| Exception handling (throw, throw_ref, try_table) | [x] | `Instructions/Exceptions.cs` |
| Variable (local.get/set/tee, global.get/set) | [x] | `Instructions/LocalVariable.cs`, `Instructions/GlobalVariable.cs` |
| Table (get, set, size, grow, fill, copy, init, elem.drop) | [x] | `Instructions/Table.cs` |
| Memory loads/stores (all widths) | [x] | `Instructions/Memory/` |
| Memory bulk (size, grow, fill, copy, init, data.drop) | [x] | `Instructions/MemoryBulk.cs` |
| Reference (ref.null, ref.func, ref.is_null, ref.as_non_null, ref.eq) | [x] | `Instructions/Reference/Reference.cs` |
| ref.test, ref.cast | [x] | `Instructions/GC/Reference.cs` |
| Aggregate — struct (new, new_default, get, set) | [x] | `Instructions/GC/Struct.cs` |
| Aggregate — array (new, new_default, new_fixed, new_data, new_elem, get, set, len, fill, copy, init_data, init_elem) | [x] | `Instructions/GC/Array.cs` |
| Aggregate — i31 (ref.i31, i31.get_s/u) | [x] | `Instructions/GC/I31.cs` |
| Aggregate — extern conversion (any.convert_extern, extern.convert_any) | [x] | `Instructions/GC/ConvertExtern.cs` |
| Numeric — constants (i32, i64, f32, f64) | [x] | `Instructions/Numeric/Const.cs` |
| Numeric — i32 ops (all unary, binary, relational, test) | [x] | `Instructions/Numeric/I32*.cs` |
| Numeric — i64 ops | [x] | `Instructions/Numeric/I64*.cs` |
| Numeric — f32 ops | [x] | `Instructions/Numeric/F32*.cs` |
| Numeric — f64 ops | [x] | `Instructions/Numeric/F64*.cs` |
| Numeric — conversions (wrap, extend, trunc, trunc_sat, convert, demote, promote, reinterpret) | [x] | `Instructions/Numeric/Conversion.cs`, `SatConversion.cs` |
| Numeric — sign extension (extend8_s, extend16_s, extend32_s) | [x] | `Instructions/Numeric/I32SignExtension.cs`, `I64SignExtension.cs` |
| Vector — v128 const | [x] | `Instructions/SIMD/VConst.cs` |
| Vector — v128 bitwise (not, and, andnot, or, xor, bitselect, any_true) | [x] | `Instructions/SIMD/VvUnOp.cs`, `VvBinOp.cs`, `VvTestOp.cs` |
| Vector — integer arithmetic per shape | [x] | `Instructions/SIMD/ViBinOp.cs`, `ViUnOp.cs` |
| Vector — float arithmetic per shape | [x] | `Instructions/SIMD/VfBinOp.cs`, `VfUnOp.cs` |
| Vector — comparisons | [x] | `Instructions/SIMD/ViRelOp.cs`, `VfRelOp.cs` |
| Vector — lane ops (extract, replace, shuffle, swizzle) | [x] | `Instructions/SIMD/VLaneOp.cs` |
| Vector — splat | [x] | `Instructions/SIMD/ViInjectOp.cs`, `VfInjectOp.cs` |
| Vector — memory loads/stores (incl. lane, splat, zero variants) | [x] | `Instructions/SIMD/VMemory.cs` |
| Vector — conversions & extends | [x] | `Instructions/SIMD/` (various) |
| Vector — saturating arithmetic | [x] | `Instructions/SIMD/ViSatBinOp.cs` |
| Vector — min/max | [x] | `Instructions/SIMD/ViMinMaxOp.cs` |
| Vector — relaxed SIMD (relaxed_min/max, relaxed_madd/nmadd, relaxed_laneselect, relaxed_swizzle, relaxed_trunc, relaxed_q15mulr, relaxed_dot) | [x] | `Instructions/SIMD/Relaxed*.cs` |

### 2.5 Modules
| Feature | Status | WACS Location |
|---------|--------|---------------|
| Index spaces (type, func, table, mem, global, tag, elem, data, local, label, field) | [x] | `Types/Indices.cs`, `Types/IndexSpace.cs` |
| Type section (recursive types) | [x] | `Modules/Sections/TypeSection.cs` |
| Tag section | [x] | `Modules/Sections/TagSection.cs` |
| Function section | [x] | `Modules/Sections/FunctionSection.cs` |
| Table section | [x] | `Modules/Sections/TableSection.cs` |
| Memory section | [x] | `Modules/Sections/MemorySection.cs` |
| Global section | [x] | `Modules/Sections/GlobalSection.cs` |
| Element section | [x] | `Modules/Sections/ElementSection.cs` |
| Data section | [x] | `Modules/Sections/DataSection.cs` |
| Start section | [x] | `Modules/Sections/StartSection.cs` |
| Import section | [x] | `Modules/Sections/ImportSection.cs` |
| Export section | [x] | `Modules/Sections/ExportSection.cs` |
| Code section | [x] | `Modules/Sections/CodeSection.cs` |
| Data count section | [x] | `SectionId.DataCount = 12` |
| Custom sections | [x] | `SectionId.Custom = 0` |

---

## 3. Validation (Spec Ch. 3)

| Feature | Status | WACS Location |
|---------|--------|---------------|
| Type validation | [x] | `Validation/Validation.cs` |
| Subtype matching | [x] | `Validation/WasmValidationContext.cs` |
| Instruction validation (type-checking stack) | [x] | `Validation/ValidationOpStack.cs` |
| Control flow validation | [x] | `Validation/ValidationControlStack.cs` |
| Module validation (all sections) | [x] | `Validation/Validation.cs` (FluentValidation) |
| Import/export type matching | [x] | `Validation/ValidationContextExtension.cs` |

---

## 4. Execution (Spec Ch. 4)

### 4.2 Runtime Structure
| Feature | Status | WACS Location |
|---------|--------|---------------|
| Store (funcs, tables, mems, globals, tags, elems, datas, structs, arrays, exns) | [x] | `Runtime/Store.cs` |
| Stack (values, labels, frames) | [x] | `Runtime/OpStack.cs`, `Runtime/Label.cs`, `Runtime/Frame.cs` |
| Values | [x] | `Runtime/Value.cs` |
| Module instances | [x] | `Runtime/Types/` |
| Function instances | [x] | `Runtime/Types/` |
| Table instances | [x] | `Runtime/Types/` |
| Memory instances | [x] | `Runtime/Types/` |
| Global instances | [x] | `Runtime/Types/` |
| Tag instances | [x] | `Runtime/Types/` |
| Element instances | [x] | `Runtime/Types/` |
| Data instances | [x] | `Runtime/Types/` |
| Export instances | [x] | `Runtime/Types/` |
| Structure instances (GC) | [x] | `Runtime/GC/StoreStruct.cs` |
| Array instances (GC) | [x] | `Runtime/GC/StoreArray.cs` |
| Exception instances | [x] | `Runtime/GC/ExnInstance.cs` |
| I31 references | [x] | `Runtime/GC/I31Ref.cs` |

### 4.3 Numerics
| Feature | Status | WACS Location |
|---------|--------|---------------|
| Integer operations (all) | [x] | `Instructions/Numeric/I32*.cs`, `I64*.cs` |
| Float operations (all, IEEE 754) | [x] | `Instructions/Numeric/F32*.cs`, `F64*.cs` |
| Conversions (all) | [x] | `Instructions/Numeric/Conversion.cs`, `SatConversion.cs` |

### 4.6 Instruction Execution
| Feature | Status | WACS Location |
|---------|--------|---------------|
| All core instructions | [x] | `Runtime/WasmRuntimeExecution.cs` |
| GC instructions | [x] | `Instructions/GC/` |
| SIMD instructions | [x] | `Instructions/SIMD/` |
| Exception instructions | [x] | `Instructions/Exceptions.cs` |

### 4.7 Module Instantiation
| Feature | Status | WACS Location |
|---------|--------|---------------|
| Module allocation | [x] | `Runtime/WasmRuntimeInstantiation.cs` |
| Function/table/memory/global allocation | [x] | `Runtime/WasmRuntimeInstantiation.cs` |
| Element/data segment initialization | [x] | `Runtime/WasmRuntimeInstantiation.cs` |
| Start function invocation | [x] | `Runtime/WasmRuntimeInstantiation.cs` |
| Host function binding | [x] | `Runtime/WasmRuntimeBinding.cs` |

---

## 5. Binary Format (Spec Ch. 5)

### 5.2 Values
| Feature | Status | WACS Location |
|---------|--------|---------------|
| LEB128 (u32, u64, s32, s64) | [x] | `Utilities/BinaryReaderExtension.cs` |
| Floating-point encoding | [x] | `Utilities/BinaryReaderExtension.cs` |
| Names (UTF-8) | [x] | `Utilities/BinaryReaderExtension.cs` |

### 5.3 Types
| Feature | Status | WACS Location |
|---------|--------|---------------|
| All type encodings | [x] | Binary deserialization across `Types/` |

### 5.4 Instructions
| Feature | Status | WACS Location |
|---------|--------|---------------|
| Single-byte opcodes (0x00-0xC4) | [x] | `OpCodes/OpCode.cs` |
| 0xFB prefix (GC) | [x] | `OpCodes/Gc.cs` |
| 0xFC prefix (extensions/bulk memory) | [x] | `OpCodes/Ext.cs` |
| 0xFD prefix (SIMD) | [x] | `OpCodes/Simd.cs` |
| 0xFE prefix (threads/atomics) | [~] | `OpCodes/Threads.cs` — **opcodes defined, no execution** |
| memarg encoding (incl. multi-memory index) | [x] | `Types/MemArg.cs` |

### 5.5 Modules
| Feature | Status | WACS Location |
|---------|--------|---------------|
| Section 0 — Custom | [x] | `SectionId.Custom` |
| Section 1 — Type | [x] | `Modules/Sections/TypeSection.cs` |
| Section 2 — Import | [x] | `Modules/Sections/ImportSection.cs` |
| Section 3 — Function | [x] | `Modules/Sections/FunctionSection.cs` |
| Section 4 — Table | [x] | `Modules/Sections/TableSection.cs` |
| Section 5 — Memory | [x] | `Modules/Sections/MemorySection.cs` |
| Section 6 — Global | [x] | `Modules/Sections/GlobalSection.cs` |
| Section 7 — Export | [x] | `Modules/Sections/ExportSection.cs` |
| Section 8 — Start | [x] | `Modules/Sections/StartSection.cs` |
| Section 9 — Element | [x] | `Modules/Sections/ElementSection.cs` |
| Section 10 — Code | [x] | `Modules/Sections/CodeSection.cs` |
| Section 11 — Data | [x] | `Modules/Sections/DataSection.cs` |
| Section 12 — Data Count | [x] | `SectionId.DataCount` |
| Section 13 — Tag | [x] | `Modules/Sections/TagSection.cs` |

---

## 6. Text Format — WAT (Spec Ch. 6)

| Feature | Status | WACS Location |
|---------|--------|---------------|
| WAT Parser (text -> module) | [ ] | **Not implemented** — no WAT-to-binary parser |
| WAT Renderer (module -> text) | [~] | `Modules/ModuleRenderer.cs`, `Modules/WatFormatter.cs` |
| Custom annotations (@id ...) | [ ] | Not implemented |

**Gap**: WACS can render modules to WAT-like text but cannot parse WAT input. The spec test suite uses pre-compiled JSON/binary, so this doesn't affect spec compliance testing, but it is a gap vs. the full spec.

---

## 7. Appendix (Spec Ch. 7)

### 7.1 Embedding Interface
| Feature | Status | Notes |
|---------|--------|-------|
| Formal embedding API (module_decode, module_validate, module_instantiate, etc.) | [~] | Functionality exists in `WasmRuntime` but not as a formalized embedding API matching spec function signatures |

### 7.2 Profiles
| Feature | Status | Notes |
|---------|--------|-------|
| Deterministic profile | [ ] | **Not implemented** — no mechanism to restrict non-deterministic behavior (NaN bit patterns, relaxed SIMD) |

### 7.3 Implementation Limitations
| Feature | Status | Notes |
|---------|--------|-------|
| Documented limits | [~] | Runtime has practical limits but they are not formally documented/configurable per spec recommendations |

### 7.6 Validation Algorithm
| Feature | Status | WACS Location |
|---------|--------|---------------|
| Type-checking algorithm | [x] | `Validation/ValidationOpStack.cs`, `ValidationControlStack.cs` |

### 7.7 Custom Sections and Annotations
| Feature | Status | WACS Location |
|---------|--------|---------------|
| Name section parsing | [x] | `Modules/Sections/NameSubsection.cs` |
| Arbitrary custom section pass-through | [x] | `SectionId.Custom` |
| Text format annotations | [ ] | Not applicable (no WAT parser) |

---

## Proposal-Level Coverage Summary

### In Spec 3.0 (all required for full compliance)
| Proposal | Status | Notes |
|----------|--------|-------|
| Sign extension ops | [x] | `I32SignExtension.cs`, `I64SignExtension.cs` |
| Non-trapping float-to-int conversions | [x] | `SatConversion.cs` |
| Multi-value | [x] | `FunctionType.cs` supports multiple results |
| Reference types (funcref, externref) | [x] | Full reference type support |
| Table instructions | [x] | `Instructions/Table.cs` |
| Multiple tables | [x] | Table index on all table instructions |
| Bulk memory and table ops | [x] | `Instructions/MemoryBulk.cs`, `Instructions/Table.cs` |
| SIMD (v128) | [x] | Full `Instructions/SIMD/` |
| Extended constant expressions | [x] | i32/i64 add/sub/mul + global.get in const exprs |
| Tail calls | [x] | `Instructions/TailCall.cs` |
| Exception handling | [x] | `Instructions/Exceptions.cs`, `TagSection.cs` |
| Multiple memories | [x] | Memory index on load/store/bulk ops |
| Memory64 (64-bit address space) | [x] | `Types/Defs/AddrType.cs`, i64 address support |
| Typed function references | [x] | `call_ref`, `return_call_ref`, typed ref |
| Garbage collection | [x] | Full `Instructions/GC/`, `Runtime/GC/` |
| Relaxed SIMD | [x] | `Instructions/SIMD/Relaxed*.cs` |
| Profiles (deterministic) | [ ] | Not implemented |
| Custom annotations | [ ] | No WAT parser |

### Beyond Spec 3.0 (future proposals)
| Proposal | Status | Notes |
|----------|--------|-------|
| Threads / shared memory / atomics | [~] | **Opcodes defined** (`OpCodes/Threads.cs`), **no execution** — factory throws `InvalidDataException` for all `0xFE` prefix instructions |
| Component model | [ ] | Not implemented |
| Branch hinting | [ ] | Not implemented |
| Stack switching | [ ] | Not implemented |

---

## Spec Test Coverage

Tests are under `Spec.Test/generated-json/` sourced from the official WebAssembly spec test suite.

### Core tests present (116 test suites):
address, align, annotations, binary, binary-leb128, block, br, br_if, br_on_non_null, br_on_null, br_table, bulk, call, call_indirect, call_ref, comments, const, conversions, custom, data, elem, endianness, exports, f32, f32_bitwise, f32_cmp, f64, f64_bitwise, f64_cmp, fac, float_exprs, float_literals, float_memory, float_misc, forward, func, func_ptrs, global, i32, i64, id, if, imports, inline-module, instance, int_exprs, int_literals, labels, left-to-right, linking, load, local_get, local_init, local_set, local_tee, loop, memory, memory_copy, memory_fill, memory_grow, memory_init, memory_redundancy, memory_size, memory_trap, names, nop, obsolete-keywords, ref, ref_as_non_null, ref_func, ref_is_null, ref_null, return, return_call, return_call_indirect, return_call_ref, select, skip-stack-guard-page, stack, start, store, switch, table, table-sub, table_copy, table_copy_mixed, table_fill, table_get, table_grow, table_init, table_set, table_size, tag, throw, throw_ref, token, traps, try_table, type, type-canon, type-equivalence, type-rec, unreachable, unreached-invalid, unreached-valid, unwind, utf8-*

### memory64 tests present (8 suites):
address64, align64, endianness64, float_memory64, load64, memory64, memory_grow64, memory_redundancy64, memory_trap64

### Multi-memory tests present (37 suites):
address, align, binary, data, data_drop, exports, float_exprs, float_memory, imports, linking, load, memory_copy, memory_fill, memory_init, memory_size, memory_trap, start, store, traps

### GC tests present (17 suites):
array, array_copy, array_fill, array_init_data, array_init_elem, array_new_data, array_new_elem, binary-gc, br_on_cast, br_on_cast_fail, extern, i31, ref_cast, ref_eq, ref_test, struct, type-subtyping

### SIMD tests present (55 suites):
Comprehensive coverage across all shapes and operations.

### Relaxed SIMD tests present (7 suites):
relaxed_dot_product, relaxed_laneselect, relaxed_madd_nmadd, relaxed_min_max, relaxed_q15mulr, relaxed_swizzle, relaxed_trunc

---

## Identified Gaps

### 1. Threads / Atomics (0xFE prefix) — MAJOR GAP
- **What**: 79 atomic instructions defined in `OpCodes/Threads.cs` but `SpecFactoryFE.cs` throws for all
- **Spec status**: Not in 3.0 core spec (separate threads proposal), but opcodes are reserved
- **Impact**: Low for spec 3.0 compliance; high for running multi-threaded wasm modules
- **Needed**: Shared memory, atomic load/store/rmw/cmpxchg, wait/notify, atomic fence

### 2. WAT Text Format Parser — MODERATE GAP
- **What**: No ability to parse `.wat` files into modules
- **Spec status**: Chapter 6 of the spec defines the full text format grammar
- **Impact**: Low for runtime usage (consumers provide `.wasm` binary); moderate for tooling/developer experience
- **Needed**: Lexer + recursive descent parser for full WAT grammar

### 3. Deterministic Profile — MINOR GAP
- **What**: No mode to enforce deterministic execution
- **Spec status**: Section 7.2 — optional profile restricting NaN bit patterns and relaxed SIMD behavior
- **Impact**: Low — most use cases don't require deterministic mode
- **Needed**: Configuration flag + NaN canonicalization + deterministic relaxed SIMD paths

### 4. Formal Embedding API — MINOR GAP
- **What**: `WasmRuntime` provides equivalent functionality but doesn't match the spec's named embedding functions exactly
- **Spec status**: Section 7.1 — informative, not normative
- **Impact**: Minimal — this is about API surface naming, not functionality

### 5. Implementation Limits Documentation — MINOR GAP
- **What**: No formal documentation of implementation-specific limits per Section 7.3
- **Spec status**: Recommendations for maximum sizes (types, functions, locals, memory pages, etc.)
- **Impact**: Minimal for correctness; useful for users to know bounds
