// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime.Types;

namespace Wacs.ComponentModel.Harness
{
    /// <summary>
    /// Little-endian readers / writers over a wasm
    /// <see cref="MemoryInstance"/>'s backing <c>byte[]</c>.
    /// The canonical ABI mandates LE on every numeric width;
    /// these methods own that detail so emitted harness IL
    /// (and hand-written harnesses) can express a memory access
    /// without inlining the bit-twiddle. Fully AOT-safe — no
    /// reflection, no allocation, callable from IL2CPP-transpiled
    /// code.
    /// </summary>
    public static class MemoryHelpers
    {
        public static int ReadI32LE(MemoryInstance memory, int offset)
        {
            var data = memory.Data;
            return data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24);
        }

        public static void WriteI32LE(MemoryInstance memory, int offset, int value)
        {
            var data = memory.Data;
            data[offset]     = unchecked((byte)value);
            data[offset + 1] = unchecked((byte)(value >> 8));
            data[offset + 2] = unchecked((byte)(value >> 16));
            data[offset + 3] = unchecked((byte)(value >> 24));
        }

        public static byte ReadU8(MemoryInstance memory, int offset)
        {
            return memory.Data[offset];
        }

        public static void WriteU8(MemoryInstance memory, int offset, byte value)
        {
            memory.Data[offset] = value;
        }

        public static short ReadI16LE(MemoryInstance memory, int offset)
        {
            var data = memory.Data;
            return (short)(data[offset] | (data[offset + 1] << 8));
        }

        public static void WriteI16LE(MemoryInstance memory, int offset, short value)
        {
            var data = memory.Data;
            data[offset]     = unchecked((byte)value);
            data[offset + 1] = unchecked((byte)(value >> 8));
        }

        public static long ReadI64LE(MemoryInstance memory, int offset)
        {
            var data = memory.Data;
            return ((long)data[offset])
                 | ((long)data[offset + 1] << 8)
                 | ((long)data[offset + 2] << 16)
                 | ((long)data[offset + 3] << 24)
                 | ((long)data[offset + 4] << 32)
                 | ((long)data[offset + 5] << 40)
                 | ((long)data[offset + 6] << 48)
                 | ((long)data[offset + 7] << 56);
        }

        public static void WriteI64LE(MemoryInstance memory, int offset, long value)
        {
            var data = memory.Data;
            data[offset]     = unchecked((byte)value);
            data[offset + 1] = unchecked((byte)(value >> 8));
            data[offset + 2] = unchecked((byte)(value >> 16));
            data[offset + 3] = unchecked((byte)(value >> 24));
            data[offset + 4] = unchecked((byte)(value >> 32));
            data[offset + 5] = unchecked((byte)(value >> 40));
            data[offset + 6] = unchecked((byte)(value >> 48));
            data[offset + 7] = unchecked((byte)(value >> 56));
        }

        public static float ReadF32LE(MemoryInstance memory, int offset)
        {
            return System.BitConverter.Int32BitsToSingle(ReadI32LE(memory, offset));
        }

        public static void WriteF32LE(MemoryInstance memory, int offset, float value)
        {
            WriteI32LE(memory, offset, System.BitConverter.SingleToInt32Bits(value));
        }

        public static double ReadF64LE(MemoryInstance memory, int offset)
        {
            return System.BitConverter.Int64BitsToDouble(ReadI64LE(memory, offset));
        }

        public static void WriteF64LE(MemoryInstance memory, int offset, double value)
        {
            WriteI64LE(memory, offset, System.BitConverter.DoubleToInt64Bits(value));
        }
    }
}
