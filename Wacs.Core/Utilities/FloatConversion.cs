// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;

namespace Wacs.Core.Utilities
{
    public class FloatConversion
    {
        public static float LongToFloat(long value)
        {
#if USE_STANDARD_CAST
            return (float)value; 
#else
            if (value == 0)
                return 0.0f;

            // Determine the sign
            bool isNegative = value < 0;
            ulong absValue = isNegative ? (ulong)(-value) : (ulong)value;

            // Find the highest set bit
            int highestBit = 63;
            while (highestBit > 0 && ((absValue & (1UL << highestBit)) == 0))
            {
                highestBit--;
            }

            // Calculate exponent
            int exponent = highestBit;
            int floatExponent = exponent + 127; // Bias for single-precision

            if (floatExponent >= 255)
            {
                // Overflow: return infinity
                return isNegative ? float.NegativeInfinity : float.PositiveInfinity;
            }

            if (floatExponent <= 0)
            {
                // Handle denormalized numbers or underflow
                if (floatExponent < -23)
                {
                    // Underflow to zero
                    return isNegative ? -0.0f : 0.0f;
                }

                // Shift to create a denormalized mantissa
                ulong mantissa = absValue >> (1 - floatExponent);
                // Mask to 23 bits
                mantissa &= 0x7FFFFF;

                // Construct the float bits
                uint floatBits = (isNegative ? (1U << 31) : 0) | (uint)mantissa;

                return BitConverter.ToSingle(BitConverter.GetBytes(floatBits), 0);
            }

            // Normalize mantissa to 23 bits
            int shift = highestBit - 23;
            ulong mantissaField = (shift > 0) ? (absValue >> shift) & 0x7FFFFF : (absValue << (-shift)) & 0x7FFFFF;

            // Handle rounding (round to nearest, ties to even)
            if (shift > 0)
            {
                ulong remainder = absValue & ((1UL << shift) - 1);
                ulong halfway = 1UL << (shift - 1);
                if (remainder > halfway || (remainder == halfway && (mantissaField & 1) != 0))
                {
                    mantissaField++;
                    if (mantissaField == (1 << 23))
                    {
                        // Mantissa overflow, increment exponent
                        mantissaField = 0;
                        floatExponent++;
                        if (floatExponent >= 255)
                        {
                            // Overflow to infinity
                            return isNegative ? float.NegativeInfinity : float.PositiveInfinity;
                        }
                    }
                }
            }

            // Construct the float bits
            uint floatBitsFinal = (isNegative ? (1U << 31) : 0) |
                                  ((uint)floatExponent << 23) |
                                  (uint)mantissaField;

            return BitConverter.ToSingle(BitConverter.GetBytes(floatBitsFinal), 0);
#endif
        }

        public static float ULongToFloat(ulong value)
        {
#if USE_STANDARD_CAST
            return (float)value;
#else
            if (value == 0)
                return 0.0f;

            // Since ulong is always non-negative, the sign bit is 0
            bool isNegative = false;

            // Find the highest set bit
            int highestBit = 63;
            while (highestBit > 0 && ((value & (1UL << highestBit)) == 0))
            {
                highestBit--;
            }

            // Calculate exponent
            int exponent = highestBit;
            int floatExponent = exponent + 127; // Bias for single-precision

            if (floatExponent >= 255)
            {
                // Overflow: return infinity
                return float.PositiveInfinity;
            }

            if (floatExponent <= 0)
            {
                // Handle denormalized numbers or underflow
                if (floatExponent < -23)
                {
                    // Underflow to zero
                    return 0.0f;
                }

                // Shift to create a denormalized mantissa
                ulong mantissa = value >> (1 - floatExponent);
                // Mask to 23 bits
                mantissa &= 0x7FFFFF;

                // Construct the float bits
                uint floatBits = (isNegative ? (1U << 31) : 0) | (uint)mantissa;

                return BitConverter.ToSingle(BitConverter.GetBytes(floatBits), 0);
            }

            // Normalize mantissa to 23 bits
            int shift = highestBit - 23;
            ulong mantissaField;
            if (shift > 0)
            {
                mantissaField = (value >> shift) & 0x7FFFFF;

                // Handle rounding (round to nearest, ties to even)
                ulong remainder = value & ((1UL << shift) - 1);
                ulong halfway = 1UL << (shift - 1);
                if (remainder > halfway || (remainder == halfway && (mantissaField & 1) != 0))
                {
                    mantissaField++;
                    if (mantissaField == (1 << 23))
                    {
                        // Mantissa overflow, increment exponent
                        mantissaField = 0;
                        floatExponent++;
                        if (floatExponent >= 255)
                        {
                            // Overflow to infinity
                            return float.PositiveInfinity;
                        }
                    }
                }
            }
            else
            {
                mantissaField = (value << (-shift)) & 0x7FFFFF;
            }

            // Construct the float bits
            uint floatBitsFinal = (isNegative ? (1U << 31) : 0) |
                                  ((uint)floatExponent << 23) |
                                  (uint)mantissaField;

            return BitConverter.ToSingle(BitConverter.GetBytes(floatBitsFinal), 0);
#endif
        }

        public static double LongToDouble(long value)
        {
#if USE_STANDARD_CAST
            return (double)value;
#else
            if (value == 0)
                return 0.0;

            // Determine the sign
            bool isNegative = value < 0;
            ulong absValue = isNegative ? (ulong)(-value) : (ulong)value;

            // Find the highest set bit
            int highestBit = 63;
            while (highestBit > 0 && ((absValue & (1UL << highestBit)) == 0))
            {
                highestBit--;
            }

            // Calculate exponent
            int exponent = highestBit;
            int doubleExponent = exponent + 1023; // Bias for double-precision

            if (doubleExponent >= 2047)
            {
                // Overflow: return infinity
                return isNegative ? double.NegativeInfinity : double.PositiveInfinity;
            }

            if (doubleExponent <= 0)
            {
                // Handle denormalized numbers or underflow
                if (doubleExponent < -52)
                {
                    // Underflow to zero
                    return isNegative ? -0.0 : 0.0;
                }

                // Shift to create a denormalized mantissa
                ulong mantissa = absValue >> (1 - doubleExponent);
                // Mask to 52 bits
                mantissa &= 0xFFFFFFFFFFFFF;

                // Construct the double bits
                ulong doubleBits = (isNegative ? (1UL << 63) : 0) | mantissa;

                return BitConverter.Int64BitsToDouble((long)doubleBits);
            }

            // Normalize mantissa to 52 bits
            int shift = highestBit - 52;
            ulong mantissaField;
            if (shift > 0)
            {
                mantissaField = (absValue >> shift) & 0xFFFFFFFFFFFFF;

                // Handle rounding (round to nearest, ties to even)
                ulong remainder = absValue & ((1UL << shift) - 1);
                ulong halfway = 1UL << (shift - 1);
                if (remainder > halfway || (remainder == halfway && (mantissaField & 1) != 0))
                {
                    mantissaField++;
                    if (mantissaField == (1UL << 52))
                    {
                        // Mantissa overflow, increment exponent
                        mantissaField = 0;
                        doubleExponent++;
                        if (doubleExponent >= 2047)
                        {
                            // Overflow to infinity
                            return isNegative ? double.NegativeInfinity : double.PositiveInfinity;
                        }
                    }
                }
            }
            else
            {
                mantissaField = (absValue << (-shift)) & 0xFFFFFFFFFFFFF;
            }

            // Construct the double bits
            ulong doubleBitsFinal = (isNegative ? (1UL << 63) : 0) |
                                    ((ulong)doubleExponent << 52) |
                                    mantissaField;

            return BitConverter.Int64BitsToDouble((long)doubleBitsFinal);
#endif
        }

        public static double ULongToDouble(ulong value)
        {
#if USE_STANDARD_CAST
            return (double)value;
#else
            if (value == 0)
                return 0.0;

            // Since ulong is always non-negative, the sign bit is 0
            bool isNegative = false;

            // Find the highest set bit
            int highestBit = 63;
            while (highestBit > 0 && ((value & (1UL << highestBit)) == 0))
            {
                highestBit--;
            }

            // Calculate exponent
            int exponent = highestBit;
            int doubleExponent = exponent + 1023; // Bias for double-precision

            if (doubleExponent >= 2047)
            {
                // Overflow: return infinity
                return double.PositiveInfinity;
            }

            if (doubleExponent <= 0)
            {
                // Handle denormalized numbers or underflow
                if (doubleExponent < -52)
                {
                    // Underflow to zero
                    return 0.0;
                }

                // Shift to create a denormalized mantissa
                ulong mantissa = value >> (1 - doubleExponent);
                // Mask to 52 bits
                mantissa &= 0xFFFFFFFFFFFFF;

                // Construct the double bits
                ulong doubleBits = (isNegative ? (1UL << 63) : 0) | mantissa;

                return BitConverter.Int64BitsToDouble((long)doubleBits);
            }

            // Normalize mantissa to 52 bits
            int shift = highestBit - 52;
            ulong mantissaField;
            if (shift > 0)
            {
                mantissaField = (value >> shift) & 0xFFFFFFFFFFFFF;

                // Handle rounding (round to nearest, ties to even)
                ulong remainder = value & ((1UL << shift) - 1);
                ulong halfway = 1UL << (shift - 1);
                if (remainder > halfway || (remainder == halfway && (mantissaField & 1) != 0))
                {
                    mantissaField++;
                    if (mantissaField == (1UL << 52))
                    {
                        // Mantissa overflow, increment exponent
                        mantissaField = 0;
                        doubleExponent++;
                        if (doubleExponent >= 2047)
                        {
                            // Overflow to infinity
                            return double.PositiveInfinity;
                        }
                    }
                }
            }
            else
            {
                mantissaField = (value << (-shift)) & 0xFFFFFFFFFFFFF;
            }

            // Construct the double bits
            ulong doubleBitsFinal = (isNegative ? (1UL << 63) : 0) |
                                    ((ulong)doubleExponent << 52) |
                                    mantissaField;

            return BitConverter.Int64BitsToDouble((long)doubleBitsFinal);
#endif
        }
    }
}