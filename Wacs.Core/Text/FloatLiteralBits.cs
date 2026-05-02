// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Globalization;

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
                if (!double.TryParse(text, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var d))
                    throw new FormatException($"bad float literal '{text}'");
                f64Bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(d));
                f32Bits = unchecked((uint)BitConverter.SingleToInt32Bits((float)d));
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

            ulong intVal = 0;
            if (intPart.Length > 0
                && !ulong.TryParse(intPart, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out intVal))
                throw new FormatException(
                    $"bad hex-float integer part in '0x{body}'");
            ulong fracVal = 0;
            if (fracPart.Length > 0
                && !ulong.TryParse(fracPart, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out fracVal))
                throw new FormatException(
                    $"bad hex-float fraction in '0x{body}'");

            double value = (double)intVal;
            if (fracPart.Length > 0)
                value += (double)fracVal / Math.Pow(16, fracPart.Length);
            value *= Math.Pow(2, binaryExp);

            f64Bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            f32Bits = unchecked((uint)BitConverter.SingleToInt32Bits((float)value));
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
