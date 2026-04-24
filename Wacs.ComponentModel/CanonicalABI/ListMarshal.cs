// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Runtime.InteropServices;

namespace Wacs.ComponentModel.CanonicalABI
{
    /// <summary>
    /// Canonical-ABI list lift / lower helpers for primitive
    /// element types — the byte-level encode/decode chokepoint
    /// parallel to <see cref="StringMarshal"/>. Every adapter
    /// that marshals <c>list&lt;T&gt;</c> for a primitive T
    /// routes through these; the byte-level reinterpretation
    /// (little-endian, tightly packed, aligned to <c>sizeof(T)</c>)
    /// lives in one place.
    ///
    /// <para><b>v0 scope:</b> primitive element types via
    /// <c>where T : unmanaged</c>. <c>list&lt;string&gt;</c>,
    /// <c>list&lt;record&gt;</c>, <c>list&lt;resource&gt;</c>
    /// (pinned handle buffer — different shape than the
    /// bitwise packed form) each land as separate helpers
    /// since their encoding diverges.</para>
    /// </summary>
    public static class ListMarshal
    {
        /// <summary>
        /// Lower a primitive-element array into a pinned byte
        /// buffer ready for the core call. Returns the pinned
        /// address, the element count, and the
        /// <see cref="GCHandle"/> the caller must Free() after
        /// the core call completes.
        /// </summary>
        public static GCHandle LowerPrim<T>(T[] values,
                                            out nint addr,
                                            out int count)
            where T : unmanaged
        {
            // Pin the array directly — its storage IS the
            // little-endian tightly-packed byte layout the
            // canonical ABI expects for primitives. No copy.
            var handle = GCHandle.Alloc(values, GCHandleType.Pinned);
            addr = handle.AddrOfPinnedObject();
            count = values.Length;
            return handle;
        }

        /// <summary>
        /// Lift a primitive-element list from a byte span of
        /// guest memory. <paramref name="source"/> is the core
        /// module's linear memory snapshot,
        /// <paramref name="byteOffset"/> is the pointer the
        /// canon return-area surfaced, and
        /// <paramref name="count"/> is the element count.
        /// </summary>
        public static T[] LiftPrim<T>(byte[] source, int byteOffset, int count)
            where T : unmanaged
        {
            var elemSize = global::System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            var byteLen = (long)count * elemSize;
            if (byteOffset < 0 || count < 0
                || byteOffset + byteLen > source.Length)
                throw new ArgumentOutOfRangeException(nameof(byteOffset),
                    $"List span (offset={byteOffset}, count={count}, "
                    + $"elemSize={elemSize}) out of range of provided "
                    + "memory buffer.");
            var result = new T[count];
            var src = MemoryMarshal.Cast<byte, T>(
                source.AsSpan(byteOffset, (int)byteLen));
            src.CopyTo(result);
            return result;
        }

        /// <summary>
        /// Lift a primitive-element list from a
        /// <see cref="ReadOnlySpan{T}"/> directly — for the
        /// caller that already holds a span over guest memory
        /// rather than a full byte[]. <paramref name="bytes"/>
        /// must be <c>count * sizeof(T)</c> bytes long and
        /// correctly aligned.
        /// </summary>
        public static T[] LiftPrim<T>(ReadOnlySpan<byte> bytes, int count)
            where T : unmanaged
        {
            var elemSize = global::System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            if (bytes.Length != count * elemSize)
                throw new ArgumentException(
                    $"Span length {bytes.Length} does not match "
                    + $"{count} * {elemSize} = {count * elemSize}.");
            var result = new T[count];
            MemoryMarshal.Cast<byte, T>(bytes).CopyTo(result);
            return result;
        }
    }
}
