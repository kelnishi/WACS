// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Text;

namespace Wacs.ComponentModel.CanonicalABI
{
    /// <summary>
    /// Primitive scalar writers for the canonical-ABI store
    /// direction. Each helper writes a single value at the given
    /// byte offset of <paramref name="dest"/> in little-endian
    /// (the canonical-ABI byte order).
    ///
    /// <para>Used by direct-linked aggregate-return emit when the
    /// host returns a record / tuple / option / result whose
    /// fields the IL must serialize back into linear memory at
    /// the wasm-supplied retArea pointer.</para>
    /// </summary>
    public static class PrimitiveStore
    {
        public static void StoreI8(byte[] dest, int offset, sbyte v)
            => dest[offset] = (byte)v;
        public static void StoreU8(byte[] dest, int offset, byte v)
            => dest[offset] = v;
        public static void StoreI16(byte[] dest, int offset, short v)
            => BinaryPrimitives.WriteInt16LittleEndian(
                dest.AsSpan(offset, 2), v);
        public static void StoreU16(byte[] dest, int offset, ushort v)
            => BinaryPrimitives.WriteUInt16LittleEndian(
                dest.AsSpan(offset, 2), v);
        public static void StoreI32(byte[] dest, int offset, int v)
            => BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(offset, 4), v);
        public static void StoreU32(byte[] dest, int offset, uint v)
            => BinaryPrimitives.WriteUInt32LittleEndian(
                dest.AsSpan(offset, 4), v);
        public static void StoreI64(byte[] dest, int offset, long v)
            => BinaryPrimitives.WriteInt64LittleEndian(
                dest.AsSpan(offset, 8), v);
        public static void StoreU64(byte[] dest, int offset, ulong v)
            => BinaryPrimitives.WriteUInt64LittleEndian(
                dest.AsSpan(offset, 8), v);
        // WriteSingleLittleEndian / WriteDoubleLittleEndian are
        // .NET 5+; bit-cast through int/long for netstandard2.1
        // compatibility.
        public static void StoreF32(byte[] dest, int offset, float v)
            => BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(v));
        public static void StoreF64(byte[] dest, int offset, double v)
            => BinaryPrimitives.WriteInt64LittleEndian(
                dest.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(v));
        public static void StoreBool(byte[] dest, int offset, bool v)
            => dest[offset] = v ? (byte)1 : (byte)0;

        /// <summary>
        /// Store a UTF-8 string into wasm linear memory and write
        /// the (ptr, len) pair to the retArea slot. Allocates a
        /// guest-side buffer via the component's <c>cabi_realloc</c>
        /// export and copies the encoded bytes into it.
        ///
        /// <para><paramref name="dest"/> is the wasm linear memory
        /// (typically <c>ctx.Memories[0].Data</c>).
        /// <paramref name="retAreaOffset"/> is the absolute byte
        /// offset in <paramref name="dest"/> where the (ptr, len)
        /// pair should land — the IL has already done
        /// <c>retArea + fieldOffset</c>.</para>
        ///
        /// <para>Used by direct-linked aggregate-RETURN emit when
        /// the host returns a string. The CLR-side string is
        /// encoded with the canon-ABI default UTF-8; UTF-16 /
        /// Latin1+UTF-16 encoders ride incrementally.</para>
        /// </summary>
        public static void StoreString(byte[] dest, int retAreaOffset,
            string value, Func<int, int, int, int, int> cabiRealloc)
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "String returns require the component to "
                    + "export `cabi_realloc`.");
            // The dest buffer can be re-bound by cabi_realloc if
            // the underlying wasm memory grows; re-fetch by lookup
            // through the same memory we got passed (in WACS today
            // the byte[] reference is stable per memory instance,
            // but defensive re-bind is cheap).
            var bytes = Encoding.UTF8.GetBytes(value);
            var ptr = cabiRealloc(0, 0, 1, bytes.Length);
            Buffer.BlockCopy(bytes, 0, dest, ptr, bytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset, 4), ptr);
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset + 4, 4), bytes.Length);
        }
    }
}
