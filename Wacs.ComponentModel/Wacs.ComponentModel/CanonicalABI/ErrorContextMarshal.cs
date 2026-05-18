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
    /// Canonical-ABI <c>error-context</c> handle helpers. Same
    /// 4-byte handle shape as <see cref="StreamMarshal"/> /
    /// <see cref="FutureMarshal"/>; the carried debug message
    /// crosses the boundary via the
    /// <c>error-context.debug-message</c> canon builtin, not via
    /// the handle's static encoding.
    /// </summary>
    public static class ErrorContextMarshal
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
