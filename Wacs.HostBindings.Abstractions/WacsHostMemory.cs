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
