# Init-Data Binary Format v1

The `WACS.Transpiler` v0.2+ `.dll` output embeds a resource holding the
module's initialization data — the information the runtime needs to
construct the module's memories, tables, globals, tags, and deferred
initializers at load time, *without* re-parsing the original WASM.

This document specifies that format. **Forward-compatible**: v1
decoders reject v2+ files, but a v2 decoder reads v1 files.

## Resource location

The codec bytes live in a generated class's static `byte[]` field:

```csharp
public static class __WACSInitResource
{
    public static readonly byte[] Data = new byte[] { /* codec output */ };
}
```

`ModuleClassGenerator` emits this class per-module. The module
constructor reads the field and calls `InitDataCodec.Decode` to
reconstruct `ModuleInitData`.

## Wire format

All integers are little-endian. Variable-length integers (`varuint32`,
`varint32`, `varuint64`, `varint64`) use WASM-style LEB128.

### Header

```
"WACSINIT"          8 bytes   magic
version_major       uint8     currently 1
version_minor       uint8     currently 0
reserved            uint16    must be 0
```

A decoder rejects files whose `version_major` exceeds its highest
supported major. Minor increments are additive (new optional
sections); a decoder ignores unknown sections.

### Section stream

After the header, the file is a stream of TLV sections:

```
tag                 uint8     section tag (see below)
length              varuint32 payload length in bytes
payload             length bytes
```

A decoder reads sections until EOF. Sections may appear in any order.
Unknown tags are skipped (forward compatibility). Missing sections
default to their zero value.

### Section tags

| Tag  | Name                      | Payload layout (fields serialized in order)                                                                                                                                                                                                                                                                                                                          |
| ---- | ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0x01 | MEMORIES                  | `varuint32 count`, then `count × { varuint64 min, uint8 hasMax, varuint64? max }` (max present iff `hasMax == 1`).                                                                                                                                                                                                                                                    |
| 0x02 | TABLES                    | `varuint32 count`, then `count × { varuint64 min, varuint64 max, valtype elemType, uint8 hasInit, expression? initExpr }`.                                                                                                                                                                                                                                         |
| 0x03 | GLOBALS                   | `varuint32 count`, then `count × { valtype type, uint8 mutability, value init }`.                                                                                                                                                                                                                                                                                  |
| 0x04 | FUNC_TYPE_HASHES          | `uint8 present`, (if present) `varuint32 count`, `count × int32`.                                                                                                                                                                                                                                                                                                  |
| 0x05 | FUNC_TYPE_SUPER_HASHES    | `uint8 present`, (if present) `varuint32 count`, `count × { varuint32 innerCount, innerCount × int32 }`.                                                                                                                                                                                                                                                           |
| 0x06 | TYPE_HASHES               | `uint8 present`, (if present) `varuint32 count`, `count × int32`.                                                                                                                                                                                                                                                                                                  |
| 0x07 | TYPE_IS_FUNC              | `uint8 present`, (if present) `varuint32 count`, `count × uint8`.                                                                                                                                                                                                                                                                                                  |
| 0x08 | ACTIVE_DATA_SEGMENTS      | `varuint32 count`, then `count × { varint32 memIdx, varint32 offset, varint32 segId }`.                                                                                                                                                                                                                                                                            |
| 0x09 | ACTIVE_ELEMENT_SEGMENTS   | `varuint32 count`, then `count × { varint32 tableIdx, varint32 offset, varuint32 fCount, fCount × varint32 funcIdx }`.                                                                                                                                                                                                                                             |
| 0x0A | GC_ELEMENT_VALUES         | `varuint32 count`, then `count × { varint32 segIdx, varint32 slotIdx, value v }`.                                                                                                                                                                                                                                                                                  |
| 0x0B | DEFERRED_ELEM_GLOBALS     | `varuint32 count`, then `count × { varint32 elemSegIdx, varint32 slotIdx, varint32 globalIdx }`.                                                                                                                                                                                                                                                                   |
| 0x0C | START_FUNC_INDEX          | `varint32 index` (-1 = no start function).                                                                                                                                                                                                                                                                                                                          |
| 0x0D | SEGMENT_BASE_IDS          | `varint32 dataBase, varint32 elemBase` — legacy; overwritten by the load-time registration in the host. Retained for in-process parity.                                                                                                                                                                                                                           |
| 0x0E | ACTIVE_ELEM_INDICES       | `varuint32 count`, then `count × varint32`.                                                                                                                                                                                                                                                                                                                        |
| 0x0F | ACTIVE_DATA_INDICES       | `varuint32 count`, then `count × varint32`.                                                                                                                                                                                                                                                                                                                        |
| 0x10 | DEFERRED_GLOBAL_INITS     | `varuint32 count`, then `count × { varint32 globalIdx, expression initializer }`.                                                                                                                                                                                                                                                                                  |
| 0x11 | DEFERRED_DATA_OFFSETS     | `varuint32 count`, then `count × { varint32 dataSegIdx, expression offsetExpr }`.                                                                                                                                                                                                                                                                                  |
| 0x12 | SAVED_DATA_SEGMENTS       | `varuint32 count`, then `count × { varint32 segId, varuint32 byteCount, byteCount × uint8 }`.                                                                                                                                                                                                                                                                      |
| 0x13 | COUNTS                    | `varint32 importFuncCount, varint32 totalFuncCount, varint32 importedTagCount`.                                                                                                                                                                                                                                                                                    |
| 0x14 | GC_GLOBAL_INITS           | `varuint32 count`, then `count × { varint32 globalIndex, varint32 typeIndex, varint32 initKind, varuint32 pCount, pCount × varint64 param, varint32 elementValType }`.                                                                                                                                                                                             |
| 0x15 | LOCAL_TAG_TYPES           | `varuint32 count`, then `count × { uint8 present, deftypeRef? defType }` — `deftypeRef` is a 0-based index into the inline DEFTYPE_TABLE section.                                                                                                                                                                                                                   |
| 0x16 | DEFTYPE_TABLE             | Flat list of unique `DefType`s referenced by LOCAL_TAG_TYPES (or future DefType-bearing sections). Encoded topologically so each entry's `SuperTypes` index its predecessors. `varuint32 count`, then `count × { recursiveType, varuint32 projection, varint32 defIndex, varuint32 superCount, superCount × varuint32 superIdx }`. See DEFTYPE_ENCODING below.     |

### Primitive encodings

- `valtype`: single `int32` (serialized as `varint32`), matching
  `Wacs.Core.Types.Defs.ValType` raw values.
- `value`: `{ valtype type, kind-specific payload }`. Scalars (I32/
  I64/F32/F64) carry their raw `Data.Int64`/`Float64` bytes
  (`uint64`/`uint64`). Reference types carry `varint64 ptr` plus
  `uint8 hasGcRef`; if `hasGcRef == 1`, the payload includes a
  GC-object descriptor (`deftypeRef` plus field bytes). See
  `VALUE_ENCODING` below.
- `expression`: `varuint32 opCount`, then `opCount × { uint8 op,
  op-specific immediates }`. The subset is the WASM "constant
  expression" ops (§3.3.10) plus the GC-spec additions: `i32.const`,
  `i64.const`, `f32.const`, `f64.const`, `global.get`, `ref.null`,
  `ref.func`, `i32.add`/`sub`/`mul`, `struct.new`,
  `struct.new_default`, `array.new`, `array.new_default`,
  `array.new_fixed`, `ref.i31`, `any.convert_extern`,
  `extern.convert_any`, `end`. Unknown ops cause decode failure.

### DEFTYPE_ENCODING

`DefType` encodes its `RecType` inline, so two DefTypes that share a
RecType (same recursion group) don't duplicate the group body. A
`recursiveType` payload is:

```
hash                int32    RecursiveType.GetHashCode() (for identity checks)
subCount            varuint32
subCount × subtype:
  superCount        varuint32
  superCount × { varuint32 typeIdx }   // super type references (by module type index)
  body              compositeType
```

A `compositeType` is tagged:

```
kind                uint8    1 = FunctionType, 2 = StructType, 3 = ArrayType
kind-specific payload
```

FunctionType: `varuint32 paramCount, paramCount × valtype, varuint32 resultCount, resultCount × valtype`.

StructType: `varuint32 fieldCount, fieldCount × { valtype type, uint8 mutability, uint8 packedWidth }`.

ArrayType: `{ valtype elemType, uint8 mutability, uint8 packedWidth }`.

This is the minimal structural info needed for `ref.test` / `ref.cast`
hash equality and interpreter interop.

### VALUE_ENCODING

```
type        varint32       ValType raw
kind        uint8          0 = scalar, 1 = nullref, 2 = externref (opaque), 3 = v128 (16 bytes), 4 = struct, 5 = array
```

| kind | payload                                                                                                                    |
| ---- | -------------------------------------------------------------------------------------------------------------------------- |
| 0    | `uint64 bits` — raw bits of `Data.Int64`                                                                                   |
| 1    | none                                                                                                                       |
| 2    | `varint64 idx`                                                                                                             |
| 3    | `16 × uint8`                                                                                                               |
| 4    | `deftypeRef type, varuint32 fieldCount, fieldCount × value`                                                                |
| 5    | `deftypeRef type, varuint32 length, length × value`                                                                        |

Any other kind causes decode failure.

## Versioning policy

- Patch changes to encoding within a section keep the same
  `version_minor` (ties are rare and non-breaking).
- New sections (`tag >= 0x17`) bump `version_minor`. v1.N decoders
  ignore tags they don't recognize. v2.0 is a break.
- Field reordering within an existing section's payload is a
  breaking change. Don't.

## Why a private codec (not WASM-binary serialization)

`ModuleInitData` holds *post-instantiation* state — evaluated global
defaults, per-segment dropped flags, pre-computed type hashes. Round-
tripping through WASM binary would require re-running the interpreter
at load time. That's correct but slow; it also means any transpiler
logic change between versions silently alters loaded .dlls' behavior.

A private codec gives us: (1) deterministic round-trip, (2) per-
section version/backcompat control, (3) a stable contract the test
suite can assert against.
