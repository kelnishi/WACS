// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Wacs.Core.Runtime;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Aggressively-inlined helpers for reading pre-decoded immediates out of the annotated
    /// bytecode stream. Every call advances <paramref name="pc"/> by the read width.
    ///
    /// The stream is always little-endian and always fixed-width per immediate kind — no
    /// LEB128 at runtime. Reads use <see cref="BinaryPrimitives"/>, which lowers to a single
    /// unaligned load on x64 / ARM64 (BinaryPrimitives is implemented via MemoryMarshal +
    /// Unsafe.ReadUnaligned internally) and is AOT-safe. Works identically on net8.0 and
    /// netstandard2.1.
    /// </summary>
    internal static class StreamReader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte ReadU8(ReadOnlySpan<byte> code, ref int pc)
        {
            byte v = code[pc];
            pc += 1;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadS32(ReadOnlySpan<byte> code, ref int pc)
        {
            int v = BinaryPrimitives.ReadInt32LittleEndian(code.Slice(pc));
            pc += 4;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ReadU32(ReadOnlySpan<byte> code, ref int pc)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(code.Slice(pc));
            pc += 4;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long ReadS64(ReadOnlySpan<byte> code, ref int pc)
        {
            long v = BinaryPrimitives.ReadInt64LittleEndian(code.Slice(pc));
            pc += 8;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadU64(ReadOnlySpan<byte> code, ref int pc)
        {
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(code.Slice(pc));
            pc += 8;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ReadF32(ReadOnlySpan<byte> code, ref int pc)
        {
            // BinaryPrimitives.ReadSingleLittleEndian exists in net5.0+; for netstandard2.1
            // compatibility, reinterpret the int bits instead.
            int bits = BinaryPrimitives.ReadInt32LittleEndian(code.Slice(pc));
            pc += 4;
            return BitConverter.Int32BitsToSingle(bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ReadF64(ReadOnlySpan<byte> code, ref int pc)
        {
            long bits = BinaryPrimitives.ReadInt64LittleEndian(code.Slice(pc));
            pc += 8;
            return BitConverter.Int64BitsToDouble(bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static V128 ReadV128(ReadOnlySpan<byte> code, ref int pc)
        {
            V128 v = MemoryMarshal.Read<V128>(code.Slice(pc, 16));
            pc += 16;
            return v;
        }
    }
}
