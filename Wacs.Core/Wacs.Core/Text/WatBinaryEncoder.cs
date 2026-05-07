// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;

namespace Wacs.Core.Text
{
    /// <summary>
    /// Encode WAT-parsed immediates into the binary form expected by
    /// <c>InstructionBase.Parse(BinaryReader)</c>. This lets the text parser
    /// delegate immediate decoding to the battle-tested binary parser
    /// without duplicating per-instruction logic.
    ///
    /// <para>Scope: simple numeric/index immediates. Block instructions
    /// construct their <c>Block</c> object directly via their
    /// <c>Immediate(blockType, …)</c> overloads — they're not routed through
    /// this encoder.</para>
    /// </summary>
    internal static class WatBinaryEncoder
    {
        public static void WriteLeb128U32(this BinaryWriter w, uint value)
        {
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value == 0) { w.Write(b); return; }
                w.Write((byte)(b | 0x80));
            }
        }

        public static void WriteLeb128S32(this BinaryWriter w, int value)
        {
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                int remaining = value >> 7;
                bool signBit = (b & 0x40) != 0;
                bool done = (remaining == 0 && !signBit) || (remaining == -1 && signBit);
                if (done) { w.Write(b); return; }
                w.Write((byte)(b | 0x80));
                value = remaining;
            }
        }

        public static void WriteLeb128S64(this BinaryWriter w, long value)
        {
            while (true)
            {
                byte b = (byte)(value & 0x7F);
                long remaining = value >> 7;
                bool signBit = (b & 0x40) != 0;
                bool done = (remaining == 0 && !signBit) || (remaining == -1 && signBit);
                if (done) { w.Write(b); return; }
                w.Write((byte)(b | 0x80));
                value = remaining;
            }
        }

        /// <summary>
        /// IEEE-754 32-bit float, little-endian.
        /// </summary>
        public static void WriteF32(this BinaryWriter w, float value) => w.Write(value);

        /// <summary>
        /// IEEE-754 64-bit float, little-endian.
        /// </summary>
        public static void WriteF64(this BinaryWriter w, double value) => w.Write(value);

        /// <summary>
        /// Create a <see cref="BinaryReader"/> pointing at an in-memory byte
        /// sequence built by <paramref name="writerFn"/>.
        /// </summary>
        public static BinaryReader BuildReader(System.Action<BinaryWriter> writerFn)
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                writerFn(w);
            ms.Position = 0;
            return new BinaryReader(ms);
        }
    }
}
