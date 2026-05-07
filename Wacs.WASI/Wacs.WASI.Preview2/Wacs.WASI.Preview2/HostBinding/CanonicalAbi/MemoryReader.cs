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
    /// Decoders that lift canonical-ABI wire bytes into native
    /// C# values. Symmetric counterpart to
    /// <see cref="MemoryWriter"/>.
    ///
    /// <para>Every method here takes a <c>byte[] memory</c>
    /// (the linear-memory backing of the guest module) plus
    /// pointer/length wire slots. None of them allocate guest
    /// memory — that's <c>cabi_realloc</c>'s job (see
    /// <see cref="Realloc"/>).</para>
    /// </summary>
    public static class MemoryReader
    {
        /// <summary>UTF-8 decode a string from
        /// <paramref name="ptr"/> for <paramref name="len"/>
        /// bytes. Used for plain <c>string</c> params.</summary>
        public static string ReadUtf8String(byte[] memory,
            int ptr, int len)
            => Encoding.UTF8.GetString(memory, ptr, len);

        /// <summary>Copy a slice of guest memory into a fresh
        /// <c>byte[]</c>. Used for <c>list&lt;u8&gt;</c> params
        /// (the host gets a copy, not an alias into guest
        /// memory).</summary>
        public static byte[] ReadByteArray(byte[] memory,
            int ptr, int len)
        {
            var slice = new byte[len];
            if (len > 0)
                Array.Copy(memory, ptr, slice, 0, len);
            return slice;
        }

        /// <summary>Decode a <c>list&lt;list&lt;u8&gt;&gt;</c>
        /// param. <paramref name="listPtr"/> points at
        /// <paramref name="listLen"/> entries, each 8 bytes
        /// (bytes-ptr i32 + bytes-len i32). Returns a fresh
        /// <c>byte[][]</c> — host owns the elements.</summary>
        public static byte[][] ReadByteArrayList(byte[] memory,
            int listPtr, int listLen)
        {
            var result = new byte[listLen][];
            for (int i = 0; i < listLen; i++)
            {
                int eb = listPtr + i * 8;
                int dataPtr = BinaryPrimitives
                    .ReadInt32LittleEndian(memory.AsSpan(eb, 4));
                int dataLen = BinaryPrimitives
                    .ReadInt32LittleEndian(memory.AsSpan(eb + 4, 4));
                result[i] = ReadByteArray(memory, dataPtr, dataLen);
            }
            return result;
        }

        /// <summary>Read an i32 in little-endian at
        /// <paramref name="ptr"/>. Mirror of
        /// <see cref="MemoryWriter.WriteI32LE"/>.</summary>
        public static int ReadI32LE(byte[] memory, int ptr)
            => BinaryPrimitives.ReadInt32LittleEndian(
                memory.AsSpan(ptr, 4));

        /// <summary>Read a u16 in little-endian at
        /// <paramref name="ptr"/>.</summary>
        public static ushort ReadU16LE(byte[] memory, int ptr)
            => BinaryPrimitives.ReadUInt16LittleEndian(
                memory.AsSpan(ptr, 2));

        /// <summary>Read a u32 in little-endian at
        /// <paramref name="ptr"/>.</summary>
        public static uint ReadU32LE(byte[] memory, int ptr)
            => BinaryPrimitives.ReadUInt32LittleEndian(
                memory.AsSpan(ptr, 4));

        /// <summary>Read a u64 in little-endian at
        /// <paramref name="ptr"/>.</summary>
        public static ulong ReadU64LE(byte[] memory, int ptr)
            => BinaryPrimitives.ReadUInt64LittleEndian(
                memory.AsSpan(ptr, 8));
    }
}
