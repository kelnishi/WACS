// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Text;

namespace Wacs.WASI.Preview2.HostBinding.CanonicalAbi
{
    /// <summary>
    /// Encoders that lower native C# values into canonical-ABI
    /// wire bytes. Symmetric counterpart to
    /// <see cref="MemoryReader"/>.
    ///
    /// <para>String / list / record encoders that need to
    /// allocate guest memory take an <see cref="Realloc"/> for
    /// that purpose. Bare primitive writers don't allocate.</para>
    /// </summary>
    internal static class MemoryWriter
    {
        /// <summary>Write an i32 in little-endian at
        /// <paramref name="ptr"/>.</summary>
        public static void WriteI32LE(byte[] memory, int ptr, int value)
        {
            memory[ptr]     = (byte)(value & 0xFF);
            memory[ptr + 1] = (byte)((value >> 8) & 0xFF);
            memory[ptr + 2] = (byte)((value >> 16) & 0xFF);
            memory[ptr + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>Write a u16 in little-endian at
        /// <paramref name="ptr"/>.</summary>
        public static void WriteU16LE(byte[] memory, int ptr, ushort value)
            => BinaryPrimitives.WriteUInt16LittleEndian(
                memory.AsSpan(ptr, 2), value);

        /// <summary>Write a u32 in little-endian at
        /// <paramref name="ptr"/>.</summary>
        public static void WriteU32LE(byte[] memory, int ptr, uint value)
            => BinaryPrimitives.WriteUInt32LittleEndian(
                memory.AsSpan(ptr, 4), value);

        /// <summary>Write a u64 in little-endian at
        /// <paramref name="ptr"/>.</summary>
        public static void WriteU64LE(byte[] memory, int ptr, ulong value)
            => BinaryPrimitives.WriteUInt64LittleEndian(
                memory.AsSpan(ptr, 8), value);

        /// <summary>Write a primitive value at
        /// <paramref name="ptr"/> in little-endian — the
        /// canonical-ABI default (and only) byte ordering for
        /// record fields.</summary>
        public static void WritePrimitiveLE(byte[] memory, int ptr,
            Type t, object value)
        {
            if (t == typeof(bool))
                memory[ptr] = (bool)value ? (byte)1 : (byte)0;
            else if (t == typeof(byte))
                memory[ptr] = (byte)value;
            else if (t == typeof(sbyte))
                memory[ptr] = unchecked((byte)(sbyte)value);
            else if (t == typeof(short))
                BinaryPrimitives.WriteInt16LittleEndian(
                    memory.AsSpan(ptr, 2), (short)value);
            else if (t == typeof(ushort))
                BinaryPrimitives.WriteUInt16LittleEndian(
                    memory.AsSpan(ptr, 2), (ushort)value);
            else if (t == typeof(int))
                BinaryPrimitives.WriteInt32LittleEndian(
                    memory.AsSpan(ptr, 4), (int)value);
            else if (t == typeof(uint))
                BinaryPrimitives.WriteUInt32LittleEndian(
                    memory.AsSpan(ptr, 4), (uint)value);
            else if (t == typeof(long))
                BinaryPrimitives.WriteInt64LittleEndian(
                    memory.AsSpan(ptr, 8), (long)value);
            else if (t == typeof(ulong))
                BinaryPrimitives.WriteUInt64LittleEndian(
                    memory.AsSpan(ptr, 8), (ulong)value);
            else if (t == typeof(float))
                BinaryPrimitives.WriteInt32LittleEndian(
                    memory.AsSpan(ptr, 4),
                    BitConverter.SingleToInt32Bits((float)value));
            else if (t == typeof(double))
                BinaryPrimitives.WriteInt64LittleEndian(
                    memory.AsSpan(ptr, 8),
                    BitConverter.DoubleToInt64Bits((double)value));
            else
                throw new NotSupportedException(
                    "WritePrimitiveLE for " + t + " is unsupported.");
        }

        /// <summary>UTF-8 encode <paramref name="s"/>, allocate
        /// a buffer through <paramref name="alloc"/>, copy into
        /// it, and return the (ptr, byteCount) pair the canon-
        /// lowered <c>string</c> wire form expects. Empty string
        /// uses ptr=0; non-empty allocates with align=1.</summary>
        public static (int ptr, int len) WriteUtf8StringAllocated(
            byte[] memory, string s, Realloc alloc)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            int dataPtr = bytes.Length == 0 ? 0
                : alloc.Allocate(1, bytes.Length);
            if (bytes.Length > 0)
                Array.Copy(bytes, 0, memory, dataPtr, bytes.Length);
            return (dataPtr, bytes.Length);
        }

        /// <summary>Write the canon-lowered <c>option&lt;string&gt;</c>
        /// retArea form at <paramref name="offset"/>: 1B disc + 3B
        /// padding + 4B ptr + 4B len. Total 12 bytes.</summary>
        public static void WriteOptionString(byte[] memory, int offset,
            string? value, Realloc alloc)
        {
            if (value == null)
            {
                memory[offset] = 0;
                return;
            }
            memory[offset] = 1;
            memory[offset + 1] = 0;
            memory[offset + 2] = 0;
            memory[offset + 3] = 0;
            var (ptr, len) = WriteUtf8StringAllocated(memory, value, alloc);
            WriteI32LE(memory, offset + 4, ptr);
            WriteI32LE(memory, offset + 8, len);
        }

        /// <summary>Write the canon-lowered <c>result&lt;_, X&gt;</c>
        /// retArea Ok side at <paramref name="offset"/>: just the
        /// outer disc byte = 0. Caller is responsible for zeroing
        /// any padding the variant alignment requires.</summary>
        public static void WriteResultUnitOk(byte[] memory, int offset)
        {
            memory[offset] = 0;
        }

        /// <summary>Zero <paramref name="length"/> bytes at
        /// <paramref name="offset"/>. Useful for clearing variant
        /// padding so stale bytes don't leak across reused
        /// retArea allocations.</summary>
        public static void ZeroRange(byte[] memory, int offset,
            int length)
        {
            for (int i = 0; i < length; i++)
                memory[offset + i] = 0;
        }
    }
}
