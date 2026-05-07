// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.ComponentModel.CanonicalABI
{
    /// <summary>
    /// Canonical-ABI <c>option&lt;T&gt;</c> discriminant helpers.
    /// The discriminant is a single byte at the option's first
    /// slot: <c>0x00</c> for <c>None</c>, <c>0x01</c> for
    /// <c>Some</c>. The payload slot follows, aligned to T's
    /// natural alignment.
    ///
    /// <para>These helpers handle the one-byte discriminant only.
    /// Payload encode/decode is the caller's concern — it knows
    /// the payload type and uses the corresponding primitive /
    /// list / string / aggregate helper.</para>
    /// </summary>
    public static class OptionMarshal
    {
        public const byte DiscriminantNone = 0x00;
        public const byte DiscriminantSome = 0x01;

        /// <summary>
        /// Read the discriminant at <paramref name="offset"/>,
        /// confirm it's a legal option tag, and return
        /// <c>true</c> iff <c>Some</c>.
        /// </summary>
        public static bool IsSome(ReadOnlySpan<byte> source, int offset)
        {
            if (offset < 0 || offset >= source.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var disc = source[offset];
            if (disc > DiscriminantSome)
                throw new FormatException(
                    $"Invalid option discriminant 0x{disc:X2}; expected "
                    + "0x00 (None) or 0x01 (Some).");
            return disc == DiscriminantSome;
        }

        /// <summary>
        /// Write the <c>Some</c> tag byte at
        /// <paramref name="offset"/>. The caller writes the
        /// payload separately at the appropriate payload slot.
        /// </summary>
        public static void WriteSomeTag(Span<byte> dest, int offset)
        {
            if (offset < 0 || offset >= dest.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            dest[offset] = DiscriminantSome;
        }

        /// <summary>
        /// Write the <c>None</c> tag byte at
        /// <paramref name="offset"/>. No payload follows — the
        /// payload slot in the return area is left at whatever
        /// the caller allocated it with (typically zeros).
        /// </summary>
        public static void WriteNoneTag(Span<byte> dest, int offset)
        {
            if (offset < 0 || offset >= dest.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            dest[offset] = DiscriminantNone;
        }
    }
}
