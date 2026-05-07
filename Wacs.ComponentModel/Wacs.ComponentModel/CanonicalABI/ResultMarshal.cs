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
    /// Canonical-ABI <c>result&lt;Ok, Err&gt;</c> discriminant
    /// helpers. The discriminant is a single byte: <c>0x00</c>
    /// for <c>Ok</c>, <c>0x01</c> for <c>Err</c>. The payload
    /// slot follows, aligned to <c>max(Ok, Err)</c> alignment
    /// and sized to <c>max(Ok, Err)</c> size.
    ///
    /// <para>Like <see cref="OptionMarshal"/> these helpers
    /// cover the discriminant byte only. Payload marshaling
    /// runs through the payload type's own helper (string,
    /// list, primitive, …). The adapter's responsibility is
    /// sequencing: read discriminant → dispatch on tag →
    /// encode/decode payload.</para>
    /// </summary>
    public static class ResultMarshal
    {
        public const byte DiscriminantOk = 0x00;
        public const byte DiscriminantErr = 0x01;

        /// <summary>
        /// Read the discriminant at <paramref name="offset"/>,
        /// confirm it's a legal result tag, and return
        /// <c>true</c> iff <c>Ok</c>.
        /// </summary>
        public static bool IsOk(ReadOnlySpan<byte> source, int offset)
        {
            if (offset < 0 || offset >= source.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var disc = source[offset];
            if (disc > DiscriminantErr)
                throw new FormatException(
                    $"Invalid result discriminant 0x{disc:X2}; expected "
                    + "0x00 (Ok) or 0x01 (Err).");
            return disc == DiscriminantOk;
        }

        /// <summary>
        /// Write the <c>Ok</c> tag byte. The caller writes the
        /// Ok-payload separately at the payload slot.
        /// </summary>
        public static void WriteOkTag(Span<byte> dest, int offset)
        {
            if (offset < 0 || offset >= dest.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            dest[offset] = DiscriminantOk;
        }

        /// <summary>
        /// Write the <c>Err</c> tag byte. The caller writes the
        /// Err-payload separately at the payload slot.
        /// </summary>
        public static void WriteErrTag(Span<byte> dest, int offset)
        {
            if (offset < 0 || offset >= dest.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            dest[offset] = DiscriminantErr;
        }
    }
}
