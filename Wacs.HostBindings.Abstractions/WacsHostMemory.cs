// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Runtime.CompilerServices;

namespace Wacs.HostBindings
{
    /// <summary>
    /// Lightweight accessor over wasm linear memory, passed as the first
    /// parameter to every <see cref="WacsImportAttribute"/>-annotated
    /// binding method.
    ///
    /// <para>Wraps the live <see cref="byte"/>[] backing array plus the
    /// authoritative byte length (which can grow via <c>memory.grow</c>).
    /// Designed to be passed by value — it's a 16-byte struct; the JIT
    /// (or NativeAOT) inlines accesses.</para>
    ///
    /// <para>Bounds-checks every access via the public methods. Bindings
    /// that need raw <see cref="Span{T}"/> access (e.g. for bulk
    /// memcpy-style I/O) call <see cref="AsSpan(int, int)"/>, which performs
    /// a single bounds check per slice.</para>
    ///
    /// <para>The struct does not own the byte[] — it's a view, valid only
    /// for the duration of the binding call. Don't squirrel it away across
    /// asynchronous boundaries; the underlying array can be reallocated
    /// by <c>memory.grow</c> and any cached reference becomes stale.</para>
    /// </summary>
    public readonly struct WacsHostMemory
    {
        private readonly byte[] _data;
        private readonly int _length;

        /// <summary>
        /// Wraps the given backing array. Pass the live <c>byte[]</c> from
        /// <c>MemoryInstance.Data</c> (or the equivalent in your runtime)
        /// plus the authoritative wasm-visible byte length (which may be
        /// less than <c>_data.Length</c> when the runtime over-allocates
        /// for grow headroom).
        /// </summary>
        public WacsHostMemory(byte[] data, int length)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if ((uint)length > (uint)data.Length)
                throw new ArgumentOutOfRangeException(nameof(length),
                    "length exceeds backing array length");
            _length = length;
        }

        /// <summary>Authoritative wasm-visible byte length.</summary>
        public int Length => _length;

        /// <summary>Read a single byte. Throws on out-of-range access.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte(int offset)
        {
            CheckRange(offset, 1);
            return _data[offset];
        }

        /// <summary>Write a single byte. Throws on out-of-range access.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(int offset, byte value)
        {
            CheckRange(offset, 1);
            _data[offset] = value;
        }

        /// <summary>
        /// Slice as a <see cref="Span{Byte}"/> — one bounds check per slice,
        /// then per-element accesses inside the span are unchecked. The
        /// returned span is live; reads see writes from concurrent wasm
        /// execution if any.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<byte> AsSpan(int offset, int length)
        {
            CheckRange(offset, length);
            return _data.AsSpan(offset, length);
        }

        /// <summary>
        /// Convenience reader for a 4-byte little-endian int at the given
        /// offset. WASI uses these heavily for iov / nwritten / etc. pointers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32LE(int offset)
        {
            CheckRange(offset, 4);
            return _data[offset]
                 | (_data[offset + 1] << 8)
                 | (_data[offset + 2] << 16)
                 | (_data[offset + 3] << 24);
        }

        /// <summary>Convenience writer for a 4-byte little-endian int.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32LE(int offset, int value)
        {
            CheckRange(offset, 4);
            _data[offset]     = (byte)value;
            _data[offset + 1] = (byte)(value >> 8);
            _data[offset + 2] = (byte)(value >> 16);
            _data[offset + 3] = (byte)(value >> 24);
        }

        /// <summary>Convenience reader for an 8-byte little-endian long.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64LE(int offset)
        {
            CheckRange(offset, 8);
            uint lo = (uint)(_data[offset]
                          | (_data[offset + 1] << 8)
                          | (_data[offset + 2] << 16)
                          | (_data[offset + 3] << 24));
            uint hi = (uint)(_data[offset + 4]
                          | (_data[offset + 5] << 8)
                          | (_data[offset + 6] << 16)
                          | (_data[offset + 7] << 24));
            return (long)((ulong)hi << 32 | lo);
        }

        /// <summary>Convenience writer for an 8-byte little-endian long.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64LE(int offset, long value)
        {
            CheckRange(offset, 8);
            _data[offset]     = (byte)value;
            _data[offset + 1] = (byte)(value >> 8);
            _data[offset + 2] = (byte)(value >> 16);
            _data[offset + 3] = (byte)(value >> 24);
            _data[offset + 4] = (byte)(value >> 32);
            _data[offset + 5] = (byte)(value >> 40);
            _data[offset + 6] = (byte)(value >> 48);
            _data[offset + 7] = (byte)(value >> 56);
        }

        /// <summary>True if [offset, offset+byteCount) lies within the
        /// memory's authoritative length. Doesn't throw.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int offset, int byteCount)
            => (uint)offset <= (uint)_length
            && (uint)byteCount <= (uint)(_length - offset);

        /// <summary>
        /// Encode a string as UTF-8 at <paramref name="offset"/>. Returns the
        /// number of bytes written (including the trailing nul if
        /// <paramref name="nullTerminate"/>). Throws on out-of-range.
        /// </summary>
        public int WriteUtf8String(int offset, string value, bool nullTerminate)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            int byteCount = System.Text.Encoding.UTF8.GetByteCount(value);
            int total = byteCount + (nullTerminate ? 1 : 0);
            CheckRange(offset, total);
            // Encoding.UTF8.GetBytes(string, byte[], int) is available on both
            // netstandard2.0 and net8.0; the (string, Span<byte>) overload is
            // net5+ only.
            System.Text.Encoding.UTF8.GetBytes(value, 0, value.Length, _data, offset);
            if (nullTerminate) _data[offset + byteCount] = 0;
            return total;
        }

        /// <summary>
        /// Read a UTF-8 string from <paramref name="offset"/> spanning
        /// <paramref name="byteCount"/> bytes (no nul-terminator handling
        /// — pass the explicit length). Throws on out-of-range.
        /// </summary>
        public string ReadUtf8String(int offset, int byteCount)
        {
            CheckRange(offset, byteCount);
            return System.Text.Encoding.UTF8.GetString(_data, offset, byteCount);
        }

        /// <summary>Alias for <see cref="ReadUtf8String(int,int)"/> matching
        /// the <c>MemoryInstance.ReadString</c> name used in existing
        /// WASI binding code.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadString(int offset, int byteCount)
            => ReadUtf8String(offset, byteCount);

        /// <summary>Alias for <see cref="WriteInt32LE"/> — wasm linear
        /// memory is always little-endian, so the LE/native distinction
        /// matches.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32(int offset, int value) => WriteInt32LE(offset, value);

        /// <summary>Alias for <see cref="WriteInt64LE"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64(int offset, long value) => WriteInt64LE(offset, value);

        /// <summary>Alias for <see cref="ReadInt32LE"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32(int offset) => ReadInt32LE(offset);

        /// <summary>Alias for <see cref="ReadInt64LE"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64(int offset) => ReadInt64LE(offset);

        /// <summary>
        /// Read an array of <typeparamref name="T"/> structs from contiguous
        /// memory at <paramref name="offset"/>. T must be unmanaged (no
        /// references); this constraint is enforced by the C# compiler at
        /// each call site.
        /// </summary>
        public T[] ReadStructs<T>(int offset, int count) where T : unmanaged
        {
            int sz = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            CheckRange(offset, sz * count);
            var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, T>(
                _data.AsSpan(offset, sz * count));
            var arr = new T[count];
            span.CopyTo(arr);
            return arr;
        }

        /// <summary>
        /// Read one <typeparamref name="T"/> struct at <paramref name="offset"/>.
        /// </summary>
        public T ReadStruct<T>(int offset) where T : unmanaged
        {
            int sz = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            CheckRange(offset, sz);
            return System.Runtime.InteropServices.MemoryMarshal.Read<T>(
                _data.AsSpan(offset, sz));
        }

        /// <summary>
        /// Write one <typeparamref name="T"/> struct at <paramref name="offset"/>.
        /// </summary>
        public void WriteStruct<T>(int offset, T value) where T : unmanaged
        {
            int sz = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            CheckRange(offset, sz);
            System.Runtime.InteropServices.MemoryMarshal.Write(
                _data.AsSpan(offset, sz),
#if NET8_0_OR_GREATER
                in value);
#else
                ref value);
#endif
        }

        /// <summary>The underlying backing array. Use with care; prefer
        /// the typed accessors above.</summary>
        public byte[] Data => _data;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckRange(int offset, int byteCount)
        {
            // Single comparison via uint cast handles negative offsets too.
            if ((uint)offset > (uint)_length || (uint)byteCount > (uint)(_length - offset))
                throw new WacsHostFault(
                    $"out of bounds memory access: offset={offset} count={byteCount} length={_length}");
        }
    }
}
