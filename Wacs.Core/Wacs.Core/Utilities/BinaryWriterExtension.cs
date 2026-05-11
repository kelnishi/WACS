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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Wacs.Core.Utilities
{
    /// <summary>
    /// Writer-side complement to <see cref="BinaryReaderExtension"/>.
    /// Each method here is the exact inverse of a Read counterpart so
    /// that <c>Parse(BinaryReader)</c> ↔ <c>RenderBinary(BinaryWriter)</c>
    /// pairs round-trip byte-for-byte.
    /// </summary>
    public static class BinaryWriterExtension
    {
        public static void WriteLeb128_u32(this BinaryWriter writer, uint value)
        {
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value == 0) { writer.Write(b); return; }
                writer.Write((byte)(b | 0x80));
            }
        }

        public static void WriteLeb128_u64(this BinaryWriter writer, ulong value)
        {
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value == 0) { writer.Write(b); return; }
                writer.Write((byte)(b | 0x80));
            }
        }

        public static void WriteLeb128_s32(this BinaryWriter writer, int value)
        {
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                int remaining = value >> 7;
                bool signBit = (b & 0x40) != 0;
                bool done = (remaining == 0 && !signBit) || (remaining == -1 && signBit);
                if (done) { writer.Write(b); return; }
                writer.Write((byte)(b | 0x80));
                value = remaining;
            }
        }

        public static void WriteLeb128_s64(this BinaryWriter writer, long value)
        {
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                long remaining = value >> 7;
                bool signBit = (b & 0x40) != 0;
                bool done = (remaining == 0 && !signBit) || (remaining == -1 && signBit);
                if (done) { writer.Write(b); return; }
                writer.Write((byte)(b | 0x80));
                value = remaining;
            }
        }

        /// <summary>
        /// LEB128_s33 — signed 33-bit encoding used for naked heap-type
        /// indices inside ValType. Non-negative inputs only (the parser
        /// rejects negative s33 indices); the encoding mirrors s64 but
        /// with the leading sign-bit guard fixed at bit 32 so a positive
        /// uint that happens to have bit 31 set still encodes with a
        /// trailing 0-payload byte.
        /// </summary>
        public static void WriteLeb128_s33(this BinaryWriter writer, uint value)
        {
            long v = value;
            while (true)
            {
                byte b = (byte)(v & 0x7F);
                long remaining = v >> 7;
                bool signBit = (b & 0x40) != 0;
                bool done = remaining == 0 && !signBit;
                if (done) { writer.Write(b); return; }
                writer.Write((byte)(b | 0x80));
                v = remaining;
            }
        }

        /// <summary>IEEE-754 32-bit float, little-endian — BinaryWriter
        /// is little-endian on every supported platform.</summary>
        public static void Write_f32(this BinaryWriter writer, float value) => writer.Write(value);

        /// <summary>IEEE-754 64-bit float, little-endian.</summary>
        public static void Write_f64(this BinaryWriter writer, double value) => writer.Write(value);

        /// <summary>
        /// Wasm UTF-8 string: <c>leb128_u32 length</c> + UTF-8 bytes.
        /// Symmetric with <see cref="BinaryReaderExtension.ReadUtf8String"/>.
        /// </summary>
        public static void WriteUtf8String(this BinaryWriter writer, string value)
        {
            value ??= string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            writer.WriteLeb128_u32((uint)bytes.Length);
            if (bytes.Length > 0)
                writer.Write(bytes);
        }

        public static void WriteVector<T>(
            this BinaryWriter writer,
            IReadOnlyList<T> items,
            Action<BinaryWriter, T> elementWriter)
        {
            writer.WriteLeb128_u32((uint)items.Count);
            for (int i = 0; i < items.Count; i++)
                elementWriter(writer, items[i]);
        }

        public static void WriteVector<T>(
            this BinaryWriter writer,
            T[] items,
            Action<BinaryWriter, T> elementWriter)
        {
            writer.WriteLeb128_u32((uint)items.Length);
            for (int i = 0; i < items.Length; i++)
                elementWriter(writer, items[i]);
        }

        /// <summary>
        /// Run <paramref name="emit"/> against an in-memory writer, then
        /// length-prefix the result with a <c>leb128_u32</c> and write
        /// both into <paramref name="outer"/>. Used by sections and
        /// length-prefixed function bodies.
        /// </summary>
        public static void WriteLengthPrefixed(this BinaryWriter outer, Action<BinaryWriter> emit)
        {
            using var ms = new MemoryStream();
            using (var inner = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
                emit(inner);
            outer.WriteLeb128_u32((uint)ms.Length);
            ms.Position = 0;
            ms.CopyTo(outer.BaseStream);
        }
    }
}
