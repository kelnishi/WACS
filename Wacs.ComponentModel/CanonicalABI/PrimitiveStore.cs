// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
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

        /// <summary>
        /// Store a <c>byte[]</c> (canon-ABI <c>list&lt;u8&gt;</c>)
        /// into wasm linear memory and write the (ptr, count) pair
        /// to the retArea slot. Same machinery as
        /// <see cref="StoreString"/> minus the encode step.
        /// Used by direct-linked aggregate-RETURN emit when the
        /// host returns a byte[].
        /// </summary>
        public static void StoreByteArray(byte[] dest, int retAreaOffset,
            byte[] value, Func<int, int, int, int, int> cabiRealloc)
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "byte[] returns require the component to "
                    + "export `cabi_realloc`.");
            var ptr = cabiRealloc(0, 0, 1, value.Length);
            Buffer.BlockCopy(value, 0, dest, ptr, value.Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset, 4), ptr);
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset + 4, 4), value.Length);
        }

        /// <summary>
        /// Store a <c>T[]</c> of unmanaged primitives (canon-ABI
        /// <c>list&lt;T&gt;</c> for T in {s8/u8/s16/u16/s32/u32/
        /// s64/u64/f32/f64}) into wasm linear memory and write the
        /// (ptr, count) pair to the retArea slot. Allocates a guest
        /// buffer via <c>cabi_realloc</c>, copies the raw bytes via
        /// <see cref="MemoryMarshal.AsBytes{T}(System.ReadOnlySpan{T})"/>,
        /// then writes <paramref name="value"/>.Length (NOT the byte
        /// count) into the len slot.
        ///
        /// <para>Canon-ABI requires little-endian; this relies on
        /// the .NET host running on a little-endian platform (.NET
        /// supports x64 / arm64 / arm / wasm — all LE).</para>
        ///
        /// <para>Used by direct-linked aggregate-RETURN emit when the
        /// host returns int[], long[], float[], etc.</para>
        /// </summary>
        public static void StorePrimitiveArray<T>(byte[] dest,
            int retAreaOffset, T[] value,
            Func<int, int, int, int, int> cabiRealloc)
            where T : unmanaged
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "list<T> returns require the component to "
                    + "export `cabi_realloc`.");
            int elementSize = MarshalSizeOf<T>.Size;
            int byteCount = value.Length * elementSize;
            var ptr = cabiRealloc(0, 0, elementSize, byteCount);
            var srcBytes = MemoryMarshal.AsBytes(
                new ReadOnlySpan<T>(value));
            srcBytes.CopyTo(dest.AsSpan(ptr, byteCount));
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset, 4), ptr);
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset + 4, 4), value.Length);
        }

        /// <summary>
        /// Store a <c>byte[][]</c> (canon-ABI <c>list&lt;list&lt;u8&gt;&gt;</c>)
        /// into wasm linear memory and write the (ptr, count) pair
        /// to the retArea slot.
        ///
        /// <para>Two-level allocation: cabi_realloc once for the
        /// outer (sub_ptr, sub_len)-pair array, then per-element
        /// for each raw byte buffer. Mirrors <see cref="StoreStringList"/>
        /// minus the UTF-8 encode step. Used by direct-linked
        /// aggregate-RETURN emit when the host returns byte[][].</para>
        /// </summary>
        public static void StoreByteArrayList(byte[] dest, int retAreaOffset,
            byte[][] value, Func<int, int, int, int, int> cabiRealloc)
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "list<list<u8>> returns require the component "
                    + "to export `cabi_realloc`.");
            int count = value.Length;
            int outerByteCount = count * 8;
            int outerPtr = cabiRealloc(0, 0, 4, outerByteCount);
            for (int i = 0; i < count; i++)
            {
                var sub = value[i];
                var subPtr = cabiRealloc(0, 0, 1, sub.Length);
                Buffer.BlockCopy(sub, 0, dest, subPtr, sub.Length);
                BinaryPrimitives.WriteInt32LittleEndian(
                    dest.AsSpan(outerPtr + i * 8, 4), subPtr);
                BinaryPrimitives.WriteInt32LittleEndian(
                    dest.AsSpan(outerPtr + i * 8 + 4, 4), sub.Length);
            }
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset, 4), outerPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset + 4, 4), count);
        }

        /// <summary>
        /// Store a <c>T[][]</c> of unmanaged primitives (canon-ABI
        /// <c>list&lt;list&lt;T&gt;&gt;</c>) into wasm linear memory and
        /// write the (ptr, count) pair to the retArea slot.
        ///
        /// <para>Two-level allocation: cabi_realloc once for the
        /// outer (sub_ptr, sub_len)-pair array (8 bytes per element),
        /// then per-element for each raw primitive buffer. Generic
        /// over T : unmanaged so the same helper closes over int[],
        /// long[], float[], etc.</para>
        ///
        /// <para>Used by direct-linked aggregate-RETURN emit when
        /// the host returns int[][], long[][], etc.</para>
        /// </summary>
        public static void StorePrimArrayList<T>(byte[] dest,
            int retAreaOffset, T[][] value,
            Func<int, int, int, int, int> cabiRealloc)
            where T : unmanaged
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "list<list<T>> returns require the component "
                    + "to export `cabi_realloc`.");
            int count = value.Length;
            int outerByteCount = count * 8;
            int outerPtr = cabiRealloc(0, 0, 4, outerByteCount);
            int elementSize = MarshalSizeOf<T>.Size;
            for (int i = 0; i < count; i++)
            {
                var sub = value[i];
                int subByteCount = sub.Length * elementSize;
                var subPtr = cabiRealloc(0, 0, elementSize, subByteCount);
                var srcBytes = MemoryMarshal.AsBytes(
                    new ReadOnlySpan<T>(sub));
                srcBytes.CopyTo(dest.AsSpan(subPtr, subByteCount));
                BinaryPrimitives.WriteInt32LittleEndian(
                    dest.AsSpan(outerPtr + i * 8, 4), subPtr);
                BinaryPrimitives.WriteInt32LittleEndian(
                    dest.AsSpan(outerPtr + i * 8 + 4, 4), sub.Length);
            }
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset, 4), outerPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset + 4, 4), count);
        }

        /// <summary>
        /// Store a <c>string[]</c> (canon-ABI <c>list&lt;string&gt;</c>)
        /// into wasm linear memory and write the (ptr, count) pair to
        /// the retArea slot.
        ///
        /// <para>Two-level allocation: cabi_realloc once for the
        /// outer array of (ptr, len) pairs (8 bytes per element),
        /// then once per element for the UTF-8 byte buffer. Each
        /// (sptr, slen) pair is written into its outer slot.</para>
        ///
        /// <para>Used by direct-linked aggregate-RETURN emit when the
        /// host returns string[].</para>
        /// </summary>
        public static void StoreStringList(byte[] dest, int retAreaOffset,
            string[] value, Func<int, int, int, int, int> cabiRealloc)
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "list<string> returns require the component to "
                    + "export `cabi_realloc`.");
            int count = value.Length;
            int outerByteCount = count * 8;
            int outerPtr = cabiRealloc(0, 0, 4, outerByteCount);
            for (int i = 0; i < count; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(value[i]);
                var sPtr = cabiRealloc(0, 0, 1, bytes.Length);
                Buffer.BlockCopy(bytes, 0, dest, sPtr, bytes.Length);
                BinaryPrimitives.WriteInt32LittleEndian(
                    dest.AsSpan(outerPtr + i * 8, 4), sPtr);
                BinaryPrimitives.WriteInt32LittleEndian(
                    dest.AsSpan(outerPtr + i * 8 + 4, 4),
                    bytes.Length);
            }
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset, 4), outerPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(retAreaOffset + 4, 4), count);
        }

        // sizeof(T) requires unsafe; cache the per-T size once via
        // Marshal.SizeOf to avoid the unsafe block in the hot path.
        // For canon-ABI primitives (i8/i16/i32/i64/f32/f64) this
        // matches sizeof(T) exactly; bool is excluded by callers.
        private static class MarshalSizeOf<T> where T : unmanaged
        {
            public static readonly int Size = ComputeSize();
            private static int ComputeSize()
            {
                if (typeof(T) == typeof(byte)
                    || typeof(T) == typeof(sbyte)) return 1;
                if (typeof(T) == typeof(short)
                    || typeof(T) == typeof(ushort)) return 2;
                if (typeof(T) == typeof(int)
                    || typeof(T) == typeof(uint)
                    || typeof(T) == typeof(float)) return 4;
                if (typeof(T) == typeof(long)
                    || typeof(T) == typeof(ulong)
                    || typeof(T) == typeof(double)) return 8;
                throw new InvalidOperationException(
                    "Unsupported list<T> element type: " + typeof(T));
            }
        }
    }
}
