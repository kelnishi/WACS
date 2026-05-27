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

        /// <summary>Lift a canon-ABI list&lt;u8&gt; from a
        /// (ptr, len) pair into a fresh <c>byte[]</c>. Used
        /// by the sourcegen for byte[] return + ptr/len
        /// arms of option / result.</summary>
        public static byte[] LiftBytes(
            MemoryInstance memory, int ptr, int len)
        {
            var arr = new byte[len];
            if (len > 0)
                System.Buffer.BlockCopy(
                    memory.Data, ptr, arr, 0, len);
            return arr;
        }

        /// <summary>Lift a canon-ABI list&lt;T&gt; (where T
        /// is a primitive numeric / boolean type) from a
        /// (ptr, len) pair into a fresh <c>T[]</c>.
        /// <para><paramref name="elementSize"/> is the byte
        /// width of T (passed by the sourcegen so the helper
        /// stays AOT-friendly — no reflection-based size
        /// lookup). Buffer.BlockCopy is documented to handle
        /// any primitive array as source and destination, so
        /// the bulk-memmove is safe across the supported
        /// element types.</para></summary>
        public static T[] LiftPrimitiveArray<T>(
            MemoryInstance memory, int ptr, int len,
            int elementSize)
        {
            var arr = new T[len];
            if (len > 0)
                System.Buffer.BlockCopy(
                    memory.Data, ptr, arr, 0,
                    len * elementSize);
            return arr;
        }

        /// <summary>Lift a canon-ABI list&lt;string&gt; from
        /// a (ptr, len) pair into a fresh <c>string[]</c>.
        /// Each element occupies 8 bytes in the outer array
        /// (the (ptr, len) pair for its UTF-8 body). Used by
        /// the sourcegen as a single-expression lift when a
        /// `string[]` shows up in an option / result arm.
        /// </summary>
        public static string[] LiftStringList(
            MemoryInstance memory, int ptr, int len)
        {
            var arr = new string[len];
            for (int i = 0; i < len; i++)
            {
                int elemPtr = ReadI32LE(memory,
                    ptr + i * 8);
                int elemLen = ReadI32LE(memory,
                    ptr + i * 8 + 4);
                arr[i] = StringCoding.LiftUtf8(memory,
                    elemPtr, elemLen);
            }
            return arr;
        }

        /// <summary>Lift a canon-ABI list&lt;list&lt;T&gt;&gt;
        /// (jagged primitive array) from a (ptr, len) pair
        /// into a fresh <c>T[][]</c>. Outer (ptr, len)
        /// addresses a contiguous array of inner (ptr, len)
        /// pairs (8 bytes each); each inner pair addresses a
        /// flat block of N * elementSize bytes.
        /// <para><paramref name="innerElementSize"/> is the
        /// byte width of T (the innermost primitive). Used as
        /// a single-expression lift for `T[][]` in option /
        /// result arm contexts.</para></summary>
        public static T[][] LiftPrimitiveArrayList<T>(
            MemoryInstance memory, int ptr, int len,
            int innerElementSize)
        {
            var outer = new T[len][];
            for (int i = 0; i < len; i++)
            {
                int innerPtr = ReadI32LE(memory,
                    ptr + i * 8);
                int innerLen = ReadI32LE(memory,
                    ptr + i * 8 + 4);
                outer[i] = new T[innerLen];
                if (innerLen > 0)
                    System.Buffer.BlockCopy(memory.Data,
                        innerPtr, outer[i], 0,
                        innerLen * innerElementSize);
            }
            return outer;
        }

        /// <summary>Lift a canon-ABI
        /// list&lt;list&lt;string&gt;&gt; from a (ptr, len)
        /// pair into a fresh <c>string[][]</c>. Wraps
        /// <see cref="LiftStringList"/> per outer element so
        /// `string[][]` stays expressible as a single
        /// helper call in option / result arm contexts.
        /// </summary>
        public static string[][] LiftStringListList(
            MemoryInstance memory, int ptr, int len)
        {
            var outer = new string[len][];
            for (int i = 0; i < len; i++)
            {
                int innerPtr = ReadI32LE(memory,
                    ptr + i * 8);
                int innerLen = ReadI32LE(memory,
                    ptr + i * 8 + 4);
                outer[i] = LiftStringList(memory,
                    innerPtr, innerLen);
            }
            return outer;
        }
    }
}
