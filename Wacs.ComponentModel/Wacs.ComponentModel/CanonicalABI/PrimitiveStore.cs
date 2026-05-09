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
using Wacs.Core.Runtime.Types;

namespace Wacs.ComponentModel.CanonicalABI
{
    /// <summary>
    /// Primitive scalar writers for the canonical-ABI store
    /// direction. Each helper writes a single value at the given
    /// byte offset of <paramref name="dest"/> in little-endian
    /// (the canonical-ABI byte order). Routes through
    /// <see cref="MemoryInstance.AsSpan"/> so both
    /// <see cref="MemoryStorageMode.ManagedArray"/> and
    /// <see cref="MemoryStorageMode.NativePointer"/> backings work.
    ///
    /// <para>Used by direct-linked aggregate-return emit when the
    /// host returns a record / tuple / option / result whose
    /// fields the IL must serialize back into linear memory at
    /// the wasm-supplied retArea pointer.</para>
    /// </summary>
    public static class PrimitiveStore
    {
        public static void StoreI8(MemoryInstance dest, int offset, sbyte v)
            => dest.AsSpan(offset, 1)[0] = (byte)v;
        public static void StoreU8(MemoryInstance dest, int offset, byte v)
            => dest.AsSpan(offset, 1)[0] = v;
        public static void StoreI16(MemoryInstance dest, int offset, short v)
            => BinaryPrimitives.WriteInt16LittleEndian(
                dest.AsSpan(offset, 2), v);
        public static void StoreU16(MemoryInstance dest, int offset, ushort v)
            => BinaryPrimitives.WriteUInt16LittleEndian(
                dest.AsSpan(offset, 2), v);
        public static void StoreI32(MemoryInstance dest, int offset, int v)
            => BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(offset, 4), v);
        public static void StoreU32(MemoryInstance dest, int offset, uint v)
            => BinaryPrimitives.WriteUInt32LittleEndian(
                dest.AsSpan(offset, 4), v);
        public static void StoreI64(MemoryInstance dest, int offset, long v)
            => BinaryPrimitives.WriteInt64LittleEndian(
                dest.AsSpan(offset, 8), v);
        public static void StoreU64(MemoryInstance dest, int offset, ulong v)
            => BinaryPrimitives.WriteUInt64LittleEndian(
                dest.AsSpan(offset, 8), v);
        // WriteSingleLittleEndian / WriteDoubleLittleEndian are
        // .NET 5+; bit-cast through int/long for netstandard2.1
        // compatibility.
        public static void StoreF32(MemoryInstance dest, int offset, float v)
            => BinaryPrimitives.WriteInt32LittleEndian(
                dest.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(v));
        public static void StoreF64(MemoryInstance dest, int offset, double v)
            => BinaryPrimitives.WriteInt64LittleEndian(
                dest.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(v));
        public static void StoreBool(MemoryInstance dest, int offset, bool v)
            => dest.AsSpan(offset, 1)[0] = v ? (byte)1 : (byte)0;

        // === Reader sibling family ===
        // Used at IL-emit time wherever guest-memory bytes are decoded
        // for the lift path (string ptrs, list lengths, variant disc
        // bytes). Mode-agnostic — every method routes through
        // MemoryInstance.AsSpan, which dispatches on storage mode.

        /// <summary>Read a single byte from <paramref name="mem"/>
        /// at <paramref name="offset"/>.</summary>
        public static byte ReadU8(MemoryInstance mem, int offset)
            => mem.AsSpan(offset, 1)[0];

        /// <summary>Read a little-endian unsigned 16-bit integer
        /// from <paramref name="mem"/> at <paramref name="offset"/>.</summary>
        public static ushort ReadU16LE(MemoryInstance mem, int offset)
            => BinaryPrimitives.ReadUInt16LittleEndian(
                mem.AsSpan(offset, 2));

        /// <summary>Read a little-endian unsigned 32-bit integer
        /// from <paramref name="mem"/> at <paramref name="offset"/>.</summary>
        public static uint ReadU32LE(MemoryInstance mem, int offset)
            => BinaryPrimitives.ReadUInt32LittleEndian(
                mem.AsSpan(offset, 4));

        /// <summary>Read a little-endian signed 32-bit integer
        /// from <paramref name="mem"/> at <paramref name="offset"/>.
        /// Used wherever guest-memory pointer/length pairs are
        /// decoded for the lift path.</summary>
        public static int ReadI32LE(MemoryInstance mem, int offset)
            => BinaryPrimitives.ReadInt32LittleEndian(
                mem.AsSpan(offset, 4));

        /// <summary>
        /// Store a UTF-8 string into wasm linear memory and write
        /// the (ptr, len) pair to the retArea slot. Allocates a
        /// guest-side buffer via the component's <c>cabi_realloc</c>
        /// export and copies the encoded bytes into it.
        ///
        /// <para><paramref name="mem"/> is the wasm linear memory.
        /// <paramref name="retAreaOffset"/> is the absolute byte
        /// offset where the (ptr, len) pair should land — the IL
        /// has already done <c>retArea + fieldOffset</c>.</para>
        ///
        /// <para>Used by direct-linked aggregate-RETURN emit when
        /// the host returns a string. The CLR-side string is
        /// encoded with the canon-ABI default UTF-8; UTF-16 /
        /// Latin1+UTF-16 encoders ride incrementally.</para>
        /// </summary>
        public static void StoreString(MemoryInstance mem, int retAreaOffset,
            string value, Func<int, int, int, int, int> cabiRealloc)
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "String returns require the component to "
                    + "export `cabi_realloc`.");
            // cabi_realloc can call memory.grow; AsSpan reads the
            // live backing each call so post-grow stale references
            // can't form (round-4 gap-11 invariant, mode-aware).
            var bytes = Encoding.UTF8.GetBytes(value);
            var ptr = cabiRealloc(0, 0, 1, bytes.Length);
            bytes.AsSpan().CopyTo(mem.AsSpan(ptr, bytes.Length));
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset, 4), ptr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset + 4, 4), bytes.Length);
        }

        /// <summary>
        /// Store a <see cref="string"/> as canon-ABI
        /// <c>string-encoding=utf16</c>. Encoding is UTF-16LE
        /// bytes (2 bytes per code unit); cabi_realloc gets
        /// align=2 and byte_count = bytes.Length. The retArea
        /// receives (ptr, codeUnitCount) — note <b>length is in
        /// u16 code units, not bytes</b> per CanonicalABI.md.
        /// </summary>
        public static void StoreStringUtf16(MemoryInstance mem,
            int retAreaOffset, string value,
            Func<int, int, int, int, int> cabiRealloc)
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "String returns require the component to "
                    + "export `cabi_realloc`.");
            var bytes = Encoding.Unicode.GetBytes(value);
            var ptr = cabiRealloc(0, 0, 2, bytes.Length);
            bytes.AsSpan().CopyTo(mem.AsSpan(ptr, bytes.Length));
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset, 4), ptr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset + 4, 4), bytes.Length / 2);
        }

        /// <summary>
        /// Store a <see cref="string"/> as canon-ABI
        /// <c>string-encoding=latin1+utf16</c>. We always pick the
        /// UTF-16 branch (high-bit set) — the Latin-1 fast path
        /// would need a per-string scan and isn't free; UTF-16 is
        /// always correct. The retArea receives
        /// (ptr, taggedCodeUnits) where the tagged value =
        /// codeUnits | <see cref="StringMarshal.Latin1OrUtf16Tag"/>.
        /// </summary>
        public static void StoreStringLatin1OrUtf16(MemoryInstance mem,
            int retAreaOffset, string value,
            Func<int, int, int, int, int> cabiRealloc)
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "String returns require the component to "
                    + "export `cabi_realloc`.");
            var bytes = Encoding.Unicode.GetBytes(value);
            var ptr = cabiRealloc(0, 0, 2, bytes.Length);
            bytes.AsSpan().CopyTo(mem.AsSpan(ptr, bytes.Length));
            int codeUnits = bytes.Length / 2;
            uint tagged = (uint)codeUnits
                | StringMarshal.Latin1OrUtf16Tag;
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset, 4), ptr);
            BinaryPrimitives.WriteUInt32LittleEndian(
                mem.AsSpan(retAreaOffset + 4, 4), tagged);
        }

        /// <summary>
        /// Store a <c>byte[]</c> (canon-ABI <c>list&lt;u8&gt;</c>)
        /// into wasm linear memory and write the (ptr, count) pair
        /// to the retArea slot. Same machinery as
        /// <see cref="StoreString"/> minus the encode step.
        /// Used by direct-linked aggregate-RETURN emit when the
        /// host returns a byte[].
        /// </summary>
        public static void StoreByteArray(MemoryInstance mem, int retAreaOffset,
            byte[] value, Func<int, int, int, int, int> cabiRealloc)
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "byte[] returns require the component to "
                    + "export `cabi_realloc`.");
            var ptr = cabiRealloc(0, 0, 1, value.Length);
            // AsSpan re-reads the live backing post-cabi_realloc;
            // any earlier reference would be stale after a
            // memory.grow inside the realloc call.
            value.AsSpan().CopyTo(mem.AsSpan(ptr, value.Length));
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset, 4), ptr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset + 4, 4), value.Length);
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
        public static void StorePrimitiveArray<T>(MemoryInstance mem,
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
            srcBytes.CopyTo(mem.AsSpan(ptr, byteCount));
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset, 4), ptr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset + 4, 4), value.Length);
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
        public static void StoreByteArrayList(MemoryInstance mem,
            int retAreaOffset, byte[][] value,
            Func<int, int, int, int, int> cabiRealloc)
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
                sub.AsSpan().CopyTo(mem.AsSpan(subPtr, sub.Length));
                BinaryPrimitives.WriteInt32LittleEndian(
                    mem.AsSpan(outerPtr + i * 8, 4), subPtr);
                BinaryPrimitives.WriteInt32LittleEndian(
                    mem.AsSpan(outerPtr + i * 8 + 4, 4), sub.Length);
            }
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset, 4), outerPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset + 4, 4), count);
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
        public static void StorePrimArrayList<T>(MemoryInstance mem,
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
                srcBytes.CopyTo(mem.AsSpan(subPtr, subByteCount));
                BinaryPrimitives.WriteInt32LittleEndian(
                    mem.AsSpan(outerPtr + i * 8, 4), subPtr);
                BinaryPrimitives.WriteInt32LittleEndian(
                    mem.AsSpan(outerPtr + i * 8 + 4, 4), sub.Length);
            }
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset, 4), outerPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset + 4, 4), count);
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
        public static void StoreStringList(MemoryInstance mem,
            int retAreaOffset, string[] value,
            Func<int, int, int, int, int> cabiRealloc)
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
                bytes.AsSpan().CopyTo(mem.AsSpan(sPtr, bytes.Length));
                BinaryPrimitives.WriteInt32LittleEndian(
                    mem.AsSpan(outerPtr + i * 8, 4), sPtr);
                BinaryPrimitives.WriteInt32LittleEndian(
                    mem.AsSpan(outerPtr + i * 8 + 4, 4),
                    bytes.Length);
            }
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset, 4), outerPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset + 4, 4), count);
        }

        /// <summary>
        /// Store a <c>string[][]</c> (canon-ABI
        /// <c>list&lt;list&lt;string&gt;&gt;</c>) into wasm linear memory
        /// and write the (ptr, count) pair to the retArea slot.
        ///
        /// <para>Three-level allocation: cabi_realloc for the outer
        /// (sub_ptr, sub_count)-pair array, then per-outer-element
        /// <see cref="StoreStringList"/> (which itself allocates the
        /// inner string-pair array + per-string UTF-8 buffers).
        /// Mirrors realistic shapes like HTTP's list-of-header-lists.</para>
        /// </summary>
        public static void StoreListOfStringList(MemoryInstance mem,
            int retAreaOffset, string[][] value,
            Func<int, int, int, int, int> cabiRealloc)
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "list<list<string>> returns require the component "
                    + "to export `cabi_realloc`.");
            int count = value.Length;
            int outerByteCount = count * 8;
            int outerPtr = cabiRealloc(0, 0, 4, outerByteCount);
            for (int i = 0; i < count; i++)
            {
                StoreStringList(mem, outerPtr + i * 8, value[i],
                    cabiRealloc);
            }
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset, 4), outerPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset + 4, 4), count);
        }

        /// <summary>
        /// Store a <c>byte[][][]</c> (canon-ABI
        /// <c>list&lt;list&lt;list&lt;u8&gt;&gt;&gt;</c>) into wasm linear
        /// memory. Three-level allocation parallel to
        /// <see cref="StoreListOfStringList"/> but with raw bytes
        /// instead of UTF-8 strings.
        /// </summary>
        public static void StoreListOfByteArrayList(MemoryInstance mem,
            int retAreaOffset, byte[][][] value,
            Func<int, int, int, int, int> cabiRealloc)
        {
            if (cabiRealloc == null)
                throw new InvalidOperationException(
                    "list<list<list<u8>>> returns require the component "
                    + "to export `cabi_realloc`.");
            int count = value.Length;
            int outerByteCount = count * 8;
            int outerPtr = cabiRealloc(0, 0, 4, outerByteCount);
            for (int i = 0; i < count; i++)
            {
                StoreByteArrayList(mem, outerPtr + i * 8, value[i],
                    cabiRealloc);
            }
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset, 4), outerPtr);
            BinaryPrimitives.WriteInt32LittleEndian(
                mem.AsSpan(retAreaOffset + 4, 4), count);
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
                    || typeof(T) == typeof(sbyte)
                    || typeof(T) == typeof(bool)) return 1;
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
