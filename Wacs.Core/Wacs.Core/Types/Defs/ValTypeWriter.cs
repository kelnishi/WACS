// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using Wacs.Core.Utilities;

namespace Wacs.Core.Types.Defs
{
    /// <summary>
    /// Inverse of <see cref="ValTypeParser"/>: emits the binary wire form
    /// of a <see cref="ValType"/>. The dispatch below mirrors the parser's
    /// switch exactly so a parse/write pair round-trips byte-for-byte.
    /// </summary>
    public static class ValTypeWriter
    {
        /// <summary>
        /// Heap-type encoding (single byte for the named abstract types,
        /// s33 LEB128 for concrete type indices). Used after the
        /// <c>0x63</c>/<c>0x64</c> prefix for naked-ref valtypes.
        /// </summary>
        public static void WriteHeapType(BinaryWriter writer, ValType heapType)
        {
            // Concrete type index (incl. 0): emit as s33. The parser
            // treats a non-abstract token byte as the first byte of an
            // s33-encoded index, so writing the index as s33 round-trips.
            if (heapType.IsDefType())
            {
                writer.WriteLeb128_s33((uint)heapType.Index().Value);
                return;
            }

            // Abstract heap type — single byte from the HeapType enum.
            byte token = AbstractHeapByte(heapType);
            writer.Write(token);
        }

        /// <summary>Write a reference type (either an abstract nullable form
        /// like <c>0x6F func</c> or a <c>0x63/0x64 ht</c> sequence).</summary>
        public static void WriteRefType(BinaryWriter writer, ValType refType)
        {
            WriteValType(writer, refType, parseStorageType: false);
        }

        /// <summary>Write a defType — naked s33 index, no ref prefix.</summary>
        public static void WriteDefType(BinaryWriter writer, ValType defType)
        {
            WriteValType(writer, defType, parseStorageType: false);
        }

        /// <summary>
        /// Write the block-type immediate: <c>0x40</c> for empty,
        /// a single-byte primitive code, or an s33 type index.
        /// </summary>
        public static void WriteBlockType(BinaryWriter writer, ValType blockType)
        {
            if (blockType == ValType.Empty)
            {
                writer.Write((byte)TypePrefix.EmptyBlock);
                return;
            }
            if (blockType.IsDefType())
            {
                writer.WriteLeb128_s33((uint)blockType.Index().Value);
                return;
            }
            WriteValType(writer, blockType, parseStorageType: false);
        }

        /// <summary>
        /// General-purpose ValType emission. Set
        /// <paramref name="parseStorageType"/> to true to allow the
        /// packed <c>i8</c>/<c>i16</c> tokens that appear in
        /// <c>FieldType</c> only.
        /// </summary>
        public static void WriteValType(BinaryWriter writer, ValType type, bool parseStorageType)
        {
            switch (type)
            {
                // Numeric / vector — direct single-byte tokens.
                case ValType.I32:  writer.Write((byte)NumType.I32); return;
                case ValType.I64:  writer.Write((byte)NumType.I64); return;
                case ValType.F32:  writer.Write((byte)NumType.F32); return;
                case ValType.F64:  writer.Write((byte)NumType.F64); return;
                case ValType.V128: writer.Write((byte)VecType.V128); return;

                // Packed storage types — only when called from FieldType.
                case ValType.I8 when parseStorageType:  writer.Write((byte)PackedType.I8); return;
                case ValType.I16 when parseStorageType: writer.Write((byte)PackedType.I16); return;

                // Nullable abstract heap types: single-byte form.
                case ValType.NoFunc:   writer.Write((byte)HeapType.NoFunc); return;
                case ValType.NoExtern: writer.Write((byte)HeapType.NoExtern); return;
                case ValType.None:     writer.Write((byte)HeapType.None); return;
                case ValType.FuncRef:  writer.Write((byte)HeapType.Func); return;
                case ValType.ExternRef:writer.Write((byte)HeapType.Extern); return;
                case ValType.Any:      writer.Write((byte)HeapType.Any); return;
                case ValType.Eq:       writer.Write((byte)HeapType.Eq); return;
                case ValType.I31:      writer.Write((byte)HeapType.I31); return;
                case ValType.Struct:   writer.Write((byte)HeapType.Struct); return;
                case ValType.Array:    writer.Write((byte)HeapType.Array); return;
                case ValType.Exn:      writer.Write((byte)HeapType.Exn); return;
                case ValType.NoExn:    writer.Write((byte)HeapType.NoExn); return;
            }

            // Non-nullable abstract ref form: emit `0x64 ht`.
            switch (type)
            {
                case ValType.NoFuncNN:   writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.NoFunc); return;
                case ValType.NoExternNN: writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.NoExtern); return;
                case ValType.NoneNN:     writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.None); return;
                case ValType.Func:       writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.Func); return;
                case ValType.Extern:     writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.Extern); return;
                case ValType.AnyNN:      writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.Any); return;
                case ValType.EqNN:       writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.Eq); return;
                case ValType.I31NN:      writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.I31); return;
                case ValType.StructNN:   writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.Struct); return;
                case ValType.ArrayNN:    writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.Array); return;
                case ValType.ExnNN:      writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.Exn); return;
                case ValType.NoExnNN:    writer.Write((byte)TypePrefix.RefHt); writer.Write((byte)HeapType.NoExn); return;
            }

            // Concrete DefType reference. The parser yields one of
            // these from `0x63 ht` (nullable) or `0x64 ht` (non-null)
            // with `ht` being an s33-encoded index.
            if (type.IsDefType())
            {
                byte prefix = type.IsNullable() ? (byte)TypePrefix.RefNullHt : (byte)TypePrefix.RefHt;
                writer.Write(prefix);
                writer.WriteLeb128_s33((uint)type.Index().Value);
                return;
            }

            throw new InvalidDataException($"ValType {type} ({(int)type:X8}) has no binary encoding");
        }

        private static byte AbstractHeapByte(ValType heapType)
        {
            return heapType switch
            {
                ValType.NoFunc or ValType.NoFuncNN       => (byte)HeapType.NoFunc,
                ValType.NoExtern or ValType.NoExternNN   => (byte)HeapType.NoExtern,
                ValType.None or ValType.NoneNN           => (byte)HeapType.None,
                ValType.FuncRef or ValType.Func          => (byte)HeapType.Func,
                ValType.ExternRef or ValType.Extern      => (byte)HeapType.Extern,
                ValType.Any or ValType.AnyNN             => (byte)HeapType.Any,
                ValType.Eq or ValType.EqNN               => (byte)HeapType.Eq,
                ValType.I31 or ValType.I31NN             => (byte)HeapType.I31,
                ValType.Struct or ValType.StructNN       => (byte)HeapType.Struct,
                ValType.Array or ValType.ArrayNN         => (byte)HeapType.Array,
                ValType.Exn or ValType.ExnNN             => (byte)HeapType.Exn,
                ValType.NoExn or ValType.NoExnNN         => (byte)HeapType.NoExn,
                _ => throw new InvalidDataException($"HeapType {heapType} has no abstract encoding"),
            };
        }
    }
}
