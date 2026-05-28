// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;

namespace Wacs.ComponentModel.CanonicalABI
{
    /// <summary>
    /// Canonical-ABI <c>stream&lt;T&gt;</c> handle helpers. A
    /// stream is wire-equivalent to a 4-byte handle (i32) drawn
    /// from the same per-component handle table that owns
    /// <c>own&lt;R&gt;</c> / <c>borrow&lt;R&gt;</c>. Buffer +
    /// dispatcher state live in the host's stream-dispatcher
    /// (Phase 3); the canonical ABI only carries the handle
    /// integer across lift/lower boundaries.
    ///
    /// <para>Read/write here is a plain little-endian i32 — the
    /// helpers exist to give callers a marshaling site they can
    /// point at when documenting "stream handles flow through
    /// here" rather than burying it in a generic
    /// <c>MemoryMarshal.Read&lt;int&gt;</c>.</para>
    /// </summary>
    public static class StreamMarshal
    {
        /// <summary>Wire size of a stream handle in canonical-ABI bytes.</summary>
        public const int HandleSize = 4;

        /// <summary>Wire alignment of a stream handle in canonical-ABI bytes.</summary>
        public const int HandleAlign = 4;

        /// <summary>Read the handle stored at <paramref name="offset"/>.</summary>
        public static int ReadHandle(ReadOnlySpan<byte> source, int offset)
        {
            if (offset < 0 || offset + HandleSize > source.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            return BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset));
        }

        /// <summary>Write the handle at <paramref name="offset"/>.</summary>
        public static void WriteHandle(Span<byte> dest, int offset, int handle)
        {
            if (offset < 0 || offset + HandleSize > dest.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(offset), handle);
        }
    }
}
