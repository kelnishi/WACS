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
    /// <para>Wraps either a managed <see cref="byte"/>[] backing array
    /// (~2 GiB cap per <c>Array.MaxLength</c>) or a native pointer +
    /// length (no 2 GiB cap; up to 4 GiB on wasm32 and 2^48 on
    /// memory64). Mode is decided at construction; consumers don't
    /// need to know which backing they got — every accessor
    /// dispatches automatically.</para>
    ///
    /// <para>24-byte <c>readonly struct</c> passed by value; the JIT
    /// (or NativeAOT) inlines the mode dispatch to a single branch
    /// per access. Every access bounds-checks through the public
    /// methods. Bindings that need raw <see cref="Span{T}"/> access
    /// (bulk memcpy-style I/O) call <see cref="AsSpan(int, int)"/>,
    /// which performs a single bounds check per slice.</para>
    ///
    /// <para>The struct does not own the backing storage — it's a view,
    /// valid only for the duration of the binding call. Don't squirrel
    /// it away across asynchronous boundaries; the underlying buffer
    /// can be reallocated by <c>memory.grow</c> and any cached
    /// reference becomes stale.</para>
    /// </summary>
    public readonly struct WacsHostMemory
    {
        // ManagedArray backing; null in NativePointer mode.
        private readonly byte[]? _data;

        // NativePointer backing; IntPtr.Zero in ManagedArray mode.
        // Stored as IntPtr so the struct stays usable in safe code;
        // methods that dereference cast to byte* in unsafe blocks.
        private readonly IntPtr _nativeBase;

        // wasm-visible byte length. int (not nuint) — bindings work
        // in int-bounded slices anyway (a >2 GiB native memory is
        // still sliceable in int chunks) and the public Length API
        // is int-typed for back-compat.
        private readonly int _length;

        /// <summary>
        /// Wraps a managed <c>byte[]</c> backing.
        /// </summary>
        public WacsHostMemory(byte[] data, int length)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _nativeBase = IntPtr.Zero;
            if ((uint)length > (uint)data.Length)
                throw new ArgumentOutOfRangeException(nameof(length),
                    "length exceeds backing array length");
            _length = length;
        }

        /// <summary>
        /// Wraps a native-pointer backing. <paramref name="nativeBase"/>
        /// must point to <paramref name="length"/> bytes of valid
        /// linear memory; the runtime guarantees the pointer outlives
        /// the binding call. Pass <c>IntPtr.Zero</c> only with
        /// <c>length == 0</c>.
        /// </summary>
        public WacsHostMemory(IntPtr nativeBase, int length)
        {
            _data = null;
            _nativeBase = nativeBase;
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length),
                    "length must be non-negative");
            if (length > 0 && nativeBase == IntPtr.Zero)
                throw new ArgumentNullException(nameof(nativeBase),
                    "nativeBase must be non-zero for non-empty memory");
            _length = length;
        }

        /// <summary>True when this view is backed by native memory
        /// (<see cref="MemoryStorageMode.NativePointer"/>) rather than
        /// a managed byte[].</summary>
        public bool IsNative
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _data == null;
        }

        /// <summary>Authoritative wasm-visible byte length.</summary>
        public int Length => _length;

        /// <summary>Read a single byte. Throws on out-of-range access.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe byte ReadByte(int offset)
        {
            CheckRange(offset, 1);
            return _data != null
                ? _data[offset]
                : ((byte*)_nativeBase)[offset];
        }

        /// <summary>Write a single byte. Throws on out-of-range access.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe void WriteByte(int offset, byte value)
        {
            CheckRange(offset, 1);
            if (_data != null)
                _data[offset] = value;
            else
                ((byte*)_nativeBase)[offset] = value;
        }

        /// <summary>
        /// Slice as a <see cref="Span{Byte}"/> — one bounds check per slice,
        /// then per-element accesses inside the span are unchecked. The
        /// returned span is live; reads see writes from concurrent wasm
        /// execution if any. The span is valid only for the duration of
        /// the binding call; <c>memory.grow</c> can reallocate the
        /// backing buffer and invalidate it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Span<byte> AsSpan(int offset, int length)
        {
            CheckRange(offset, length);
            return _data != null
                ? _data.AsSpan(offset, length)
                : new Span<byte>((byte*)_nativeBase + offset, length);
        }

        /// <summary>
        /// Convenience reader for a 4-byte little-endian int at the given
        /// offset. WASI uses these heavily for iov / nwritten / etc. pointers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32LE(int offset)
        {
            var span = AsSpan(offset, 4);
            return span[0]
                 | (span[1] << 8)
                 | (span[2] << 16)
                 | (span[3] << 24);
        }

        /// <summary>Convenience writer for a 4-byte little-endian int.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32LE(int offset, int value)
        {
            var span = AsSpan(offset, 4);
            span[0] = (byte)value;
            span[1] = (byte)(value >> 8);
            span[2] = (byte)(value >> 16);
            span[3] = (byte)(value >> 24);
        }

        /// <summary>Convenience reader for an 8-byte little-endian long.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64LE(int offset)
        {
            var span = AsSpan(offset, 8);
            uint lo = (uint)(span[0]
                          | (span[1] << 8)
                          | (span[2] << 16)
                          | (span[3] << 24));
            uint hi = (uint)(span[4]
                          | (span[5] << 8)
                          | (span[6] << 16)
                          | (span[7] << 24));
            return (long)((ulong)hi << 32 | lo);
        }

        /// <summary>Convenience writer for an 8-byte little-endian long.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt64LE(int offset, long value)
        {
            var span = AsSpan(offset, 8);
            span[0] = (byte)value;
            span[1] = (byte)(value >> 8);
            span[2] = (byte)(value >> 16);
            span[3] = (byte)(value >> 24);
            span[4] = (byte)(value >> 32);
            span[5] = (byte)(value >> 40);
            span[6] = (byte)(value >> 48);
            span[7] = (byte)(value >> 56);
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
            var span = AsSpan(offset, total);
#if NET5_0_OR_GREATER
            System.Text.Encoding.UTF8.GetBytes(value, span);
#else
            // netstandard2.0 fallback — encode to a byte[] first then
            // copy. The legacy path historically went straight through
            // _data; native-mode requires the copy regardless.
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            new ReadOnlySpan<byte>(bytes).CopyTo(span);
#endif
            if (nullTerminate) span[byteCount] = 0;
            return total;
        }

        /// <summary>
        /// Read a UTF-8 string from <paramref name="offset"/> spanning
        /// <paramref name="byteCount"/> bytes (no nul-terminator handling
        /// — pass the explicit length). Throws on out-of-range.
        /// </summary>
        public unsafe string ReadUtf8String(int offset, int byteCount)
        {
            CheckRange(offset, byteCount);
            if (_data != null)
                return System.Text.Encoding.UTF8.GetString(_data, offset, byteCount);
#if NET5_0_OR_GREATER
            return System.Text.Encoding.UTF8.GetString(
                new ReadOnlySpan<byte>((byte*)_nativeBase + offset, byteCount));
#else
            // netstandard2.0 has GetString(byte*, int).
            return System.Text.Encoding.UTF8.GetString(
                (byte*)_nativeBase + offset, byteCount);
#endif
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
            var span = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, T>(
                AsSpan(offset, sz * count));
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
            return System.Runtime.InteropServices.MemoryMarshal.Read<T>(
                AsSpan(offset, sz));
        }

        /// <summary>
        /// Write one <typeparamref name="T"/> struct at <paramref name="offset"/>.
        /// </summary>
        public void WriteStruct<T>(int offset, T value) where T : unmanaged
        {
            int sz = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            System.Runtime.InteropServices.MemoryMarshal.Write(
                AsSpan(offset, sz),
#if NET8_0_OR_GREATER
                in value);
#else
                ref value);
#endif
        }

        /// <summary>
        /// The underlying byte[] backing array, or
        /// <see cref="Array.Empty{T}"/> in NativePointer mode where no
        /// managed buffer exists. Use <see cref="AsSpan(int, int)"/> for
        /// mode-agnostic access; this getter exists only for legacy
        /// callers that need a byte[] handle (pinning, marshaling). In
        /// NativePointer mode legacy callers see an empty array — they
        /// must migrate to AsSpan to read native memory.
        /// </summary>
        public byte[] Data => _data ?? Array.Empty<byte>();

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
