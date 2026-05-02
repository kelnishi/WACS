// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Globalization;
using System.Numerics;

namespace Wacs.Core.Text
{
    /// <summary>
    /// Decodes a wasm spec float literal into IEEE-754 bit patterns.
    /// Handles every shape the spec admits — decimal floats, hex
    /// floats (<c>0x1.Ap+3</c>), <c>inf</c>, <c>nan</c>, and
    /// <c>nan:0xPAYLOAD</c> — and emits both f32 and f64 bit patterns
    /// independently so NaN payload widths (23 bits f32, 52 bits f64)
    /// match the spec's mantissa size for each precision.
    ///
    /// Shared by <see cref="TextScriptParser"/> (for value literals
    /// in assertions) and <see cref="TextModuleParser"/> (for
    /// <c>(f32.const …)</c> and <c>(f64.const …)</c> in module
    /// bodies). Pattern literals (<c>nan:canonical</c>,
    /// <c>nan:arithmetic</c>) are NOT handled here; the script parser
    /// recognizes those at a higher level since they're only valid
    /// in assertion-expected position.
    /// </summary>
    internal static class FloatLiteralBits
    {
        /// <summary>
        /// Parse <paramref name="text"/> (the literal body, possibly
        /// with leading <c>+</c>/<c>-</c>) into both f32 and f64 bit
        /// patterns. Throws <see cref="FormatException"/> on
        /// malformed input.
        /// </summary>
        public static void Parse(string text, out uint f32Bits, out ulong f64Bits)
        {
            f32Bits = 0;
            f64Bits = 0;

            text = text.Replace("_", string.Empty);

            bool negative = false;
            if (text.StartsWith("+", StringComparison.Ordinal))
                text = text.Substring(1);
            else if (text.StartsWith("-", StringComparison.Ordinal))
            {
                negative = true;
                text = text.Substring(1);
            }

            if (text == "nan")
            {
                f32Bits = 0x7FC00000u;
                f64Bits = 0x7FF8000000000000UL;
            }
            else if (text.StartsWith("nan:", StringComparison.Ordinal))
            {
                ulong payload = ParseUInt64(text.Substring(4));
                f32Bits = 0x7F800000u | (uint)(payload & 0x007FFFFFu);
                f64Bits = 0x7FF0000000000000UL | (payload & 0x000FFFFFFFFFFFFFUL);
            }
            else if (text == "inf")
            {
                f32Bits = 0x7F800000u;
                f64Bits = 0x7FF0000000000000UL;
            }
            else if (text.StartsWith("0x", StringComparison.Ordinal)
                     || text.StartsWith("0X", StringComparison.Ordinal))
            {
                ParseHexFloat(text.Substring(2), out f32Bits, out f64Bits);
            }
            else
            {
                // Parse decimal independently into each precision so f32
                // gets correct round-to-nearest-even directly from the
                // literal — going through (float)(double)x double-rounds
                // and breaks the round-half-to-even contract for halfway
                // decimals like 8.8817847263968443574e-16.
                if (!double.TryParse(text, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var d))
                    throw new FormatException($"bad float literal '{text}'");
                f64Bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(d));

                if (!float.TryParse(text, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var f))
                    f = (float)d; // fallback — shouldn't trigger after double succeeded
                f32Bits = unchecked((uint)BitConverter.SingleToInt32Bits(f));
            }

            if (negative)
            {
                f32Bits |= 0x80000000u;
                f64Bits |= 0x8000000000000000UL;
            }
        }

        private static void ParseHexFloat(
            string body, out uint f32Bits, out ulong f64Bits)
        {
            // Wasm permits arbitrarily long hex mantissa parts — tests like
            // `0x1.00000100000000001p-50` test round-to-nearest-even at
            // bits past f32's mantissa width. To get correct rounding we
            // must accumulate the full mantissa exactly (BigInteger), then
            // round at the destination precision rather than going through
            // double first.
            int pIdx = -1;
            for (int i = 0; i < body.Length; i++)
                if (body[i] == 'p' || body[i] == 'P') { pIdx = i; break; }

            string mantissa;
            int binaryExp = 0;
            if (pIdx >= 0)
            {
                mantissa = body.Substring(0, pIdx);
                if (!int.TryParse(body.Substring(pIdx + 1),
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out binaryExp))
                    throw new FormatException(
                        $"bad hex-float exponent in '0x{body}'");
            }
            else
            {
                mantissa = body;
            }

            int dotIdx = mantissa.IndexOf('.');
            string intPart = dotIdx >= 0 ? mantissa.Substring(0, dotIdx) : mantissa;
            string fracPart = dotIdx >= 0 ? mantissa.Substring(dotIdx + 1) : string.Empty;

            // Combine integer + fraction digits into a single hex string;
            // each fraction digit shifts the binary exponent by -4.
            BigInteger m = BigInteger.Zero;
            for (int i = 0; i < intPart.Length; i++)
            {
                if (!TryHexDigit(intPart[i], out var d))
                    throw new FormatException(
                        $"bad hex-float integer part in '0x{body}'");
                m = (m << 4) + d;
            }
            for (int i = 0; i < fracPart.Length; i++)
            {
                if (!TryHexDigit(fracPart[i], out var d))
                    throw new FormatException(
                        $"bad hex-float fraction in '0x{body}'");
                m = (m << 4) + d;
            }
            // Total binary exponent: shift left by binaryExp, right by 4 per
            // fractional hex digit.
            long exp = (long)binaryExp - 4L * fracPart.Length;

            f32Bits = EncodeIeee754(m, exp, mantissaBits: 23, expBits: 8);
            f64Bits = EncodeIeee754Long(m, exp, mantissaBits: 52, expBits: 11);
        }

        /// <summary>
        /// Round-and-encode <paramref name="m"/> · 2^<paramref name="exp"/>
        /// as an IEEE-754 binary value with the given mantissa width and
        /// biased-exponent width. Performs round-to-nearest-even with
        /// proper sticky-bit accounting; supports denormals and overflow.
        /// </summary>
        private static uint EncodeIeee754(BigInteger m, long exp, int mantissaBits, int expBits)
        {
            if (m.IsZero) return 0;
            // Normalize: find the high bit of m. Conceptually we want to
            // extract bits [bitLen-1 .. bitLen-1-mantissaBits] (the explicit
            // bit + mantissaBits) and round using the rest.
            int bitLen = BigIntBitLength(m);
            // Implicit-bit representation: target form is 1.xxx × 2^E.
            // The total bits needed are mantissaBits + 1 (the leading 1 +
            // the trailing fraction bits). Extract those bits, with proper
            // round-half-to-even using the bits below the kept range.
            int keep = mantissaBits + 1;
            long unbiasedExp = exp + (bitLen - 1); // power of the leading bit
            long biasedExp = unbiasedExp + ((1L << (expBits - 1)) - 1);

            BigInteger mantissa;
            int shift = bitLen - keep;
            if (biasedExp <= 0)
            {
                // Subnormal range: the leading bit isn't implicit anymore.
                // Adjust shift so the result has biasedExp = 0 and the
                // top mantissa bit sits at position (mantissaBits-1) — i.e.
                // we drop more bits to the right.
                long extraShift = 1 - biasedExp;
                shift = (int)(bitLen - keep + extraShift);
                biasedExp = 0;
            }

            if (shift > 0)
            {
                BigInteger truncated = m >> shift;
                BigInteger remainder = m - (truncated << shift);
                BigInteger half = BigInteger.One << (shift - 1);
                // Round half to even
                if (remainder > half ||
                    (remainder == half && (truncated & BigInteger.One) != 0))
                {
                    truncated += 1;
                }
                mantissa = truncated;
            }
            else if (shift < 0)
            {
                mantissa = m << (-shift);
            }
            else
            {
                mantissa = m;
            }

            // Mantissa-rounding overflow: e.g. 0x1.FFFFFF rounded to f32 →
            // 1.0 × 2^(E+1). The mantissa exceeds (2 << mantissaBits); shift
            // and bump the exponent.
            BigInteger mantissaCap = BigInteger.One << (mantissaBits + 1);
            if (mantissa >= mantissaCap)
            {
                mantissa >>= 1;
                biasedExp += 1;
            }
            // Subnormal that rounded up to normal: leading bit appeared.
            if (biasedExp == 0 && mantissa >= (BigInteger.One << mantissaBits))
            {
                biasedExp = 1;
            }

            // Overflow to infinity.
            long maxExp = (1L << expBits) - 1;
            if (biasedExp >= maxExp)
            {
                return (uint)((maxExp << mantissaBits));
            }

            uint mantissaBitsValue;
            if (biasedExp == 0)
            {
                // Subnormal: explicit mantissa with no implicit leading 1.
                mantissaBitsValue = (uint)(mantissa & ((BigInteger.One << mantissaBits) - 1));
            }
            else
            {
                // Normal: drop the implicit leading 1.
                BigInteger mantMask = (BigInteger.One << mantissaBits) - 1;
                mantissaBitsValue = (uint)(mantissa & mantMask);
            }

            return ((uint)biasedExp << mantissaBits) | mantissaBitsValue;
        }

        /// <summary>64-bit variant of <see cref="EncodeIeee754"/>.</summary>
        private static ulong EncodeIeee754Long(BigInteger m, long exp, int mantissaBits, int expBits)
        {
            if (m.IsZero) return 0;
            int bitLen = BigIntBitLength(m);
            int keep = mantissaBits + 1;
            long unbiasedExp = exp + (bitLen - 1);
            long biasedExp = unbiasedExp + ((1L << (expBits - 1)) - 1);

            BigInteger mantissa;
            int shift = bitLen - keep;
            if (biasedExp <= 0)
            {
                long extraShift = 1 - biasedExp;
                shift = (int)(bitLen - keep + extraShift);
                biasedExp = 0;
            }

            if (shift > 0)
            {
                BigInteger truncated = m >> shift;
                BigInteger remainder = m - (truncated << shift);
                BigInteger half = BigInteger.One << (shift - 1);
                if (remainder > half ||
                    (remainder == half && (truncated & BigInteger.One) != 0))
                {
                    truncated += 1;
                }
                mantissa = truncated;
            }
            else if (shift < 0)
            {
                mantissa = m << (-shift);
            }
            else
            {
                mantissa = m;
            }

            BigInteger mantissaCap = BigInteger.One << (mantissaBits + 1);
            if (mantissa >= mantissaCap)
            {
                mantissa >>= 1;
                biasedExp += 1;
            }
            if (biasedExp == 0 && mantissa >= (BigInteger.One << mantissaBits))
            {
                biasedExp = 1;
            }

            long maxExp = (1L << expBits) - 1;
            if (biasedExp >= maxExp)
            {
                return ((ulong)maxExp << mantissaBits);
            }

            ulong mantissaBitsValue;
            if (biasedExp == 0)
            {
                mantissaBitsValue = (ulong)(mantissa & ((BigInteger.One << mantissaBits) - 1));
            }
            else
            {
                BigInteger mantMask = (BigInteger.One << mantissaBits) - 1;
                mantissaBitsValue = (ulong)(mantissa & mantMask);
            }

            return ((ulong)biasedExp << mantissaBits) | mantissaBitsValue;
        }

        // BigInteger.GetBitLength is .NET 5+; polyfill for netstandard2.1.
        private static int BigIntBitLength(BigInteger v)
        {
            if (v.IsZero) return 0;
            // Always positive in our usage (mantissa).
            byte[] bytes = v.ToByteArray();
            // Trim trailing zeros (BigInteger little-endian; high bytes last).
            int high = bytes.Length - 1;
            while (high > 0 && bytes[high] == 0) high--;
            int bits = high * 8;
            byte top = bytes[high];
            while (top != 0) { bits++; top >>= 1; }
            return bits;
        }

        private static bool TryHexDigit(char c, out int value)
        {
            if (c >= '0' && c <= '9') { value = c - '0'; return true; }
            if (c >= 'a' && c <= 'f') { value = 10 + (c - 'a'); return true; }
            if (c >= 'A' && c <= 'F') { value = 10 + (c - 'A'); return true; }
            value = 0;
            return false;
        }

        private static ulong ParseUInt64(string text)
        {
            if (text.StartsWith("0x", StringComparison.Ordinal)
                || text.StartsWith("0X", StringComparison.Ordinal))
            {
                if (!ulong.TryParse(text.Substring(2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var hv))
                    throw new FormatException($"bad hex integer in '{text}'");
                return hv;
            }
            if (!ulong.TryParse(text, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var dv))
                throw new FormatException($"bad integer in '{text}'");
            return dv;
        }
    }
}
