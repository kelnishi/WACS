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
    /// Canonical-ABI <c>future&lt;T&gt;</c> handle helpers.
    /// Same wire shape as <see cref="StreamMarshal"/>: a 4-byte
    /// handle drawn from the per-component handle table.
    /// </summary>
    public static class FutureMarshal
    {
        public const int HandleSize = 4;
        public const int HandleAlign = 4;

        public static int ReadHandle(ReadOnlySpan<byte> source, int offset)
        {
            if (offset < 0 || offset + HandleSize > source.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            return BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset));
        }

        public static void WriteHandle(Span<byte> dest, int offset, int handle)
        {
            if (offset < 0 || offset + HandleSize > dest.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            BinaryPrimitives.WriteInt32LittleEndian(dest.Slice(offset), handle);
        }
    }
}
