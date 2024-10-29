using System;
using System.Text;

namespace Wacs.Core.Utilities
{
    public static class FloatFormatter
    {
        /// <summary>
        /// Formats a floating-point number according to WebAssembly specification 6.3.2
        /// using hexadecimal format
        /// </summary>
        /// <param name="value">The floating-point value to format</param>
        /// <returns>A string representation conforming to WebAssembly spec</returns>
        public static string FormatFloat(float value)
        {
            // Handle special cases first
            if (float.IsNaN(value))
                return "nan";
        
            if (float.IsPositiveInfinity(value))
                return "inf";
            
            if (float.IsNegativeInfinity(value))
                return "-inf";
            
            if (value == 0f)
            {
                // Detect negative zero
                if (BitConverter.GetBytes(value)[3] == 0x80)
                    return "-0x0p+0";
                return "0x0p+0";
            }

            // Get the bits of the float
            uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
            int sign = (int)((bits >> 31) & 1);
            int exp = (int)((bits >> 23) & 0xFF) - 127; // Remove bias
            uint mantissa = bits & 0x7FFFFF;

            // Build the hexadecimal representation
            string signStr = sign == 1 ? "-" : "";
        
            if (mantissa == 0)
            {
                return $"{signStr}0x1p{(exp >= 0 ? "+" : "")}{exp}";
            }

            // Convert mantissa to hex, handling leading zeros properly
            StringBuilder mantissaHex = new StringBuilder();
            uint remainingMantissa = mantissa;
            bool foundNonZero = false;

            // Process 4 bits at a time from most significant to least
            for (int i = 20; i >= 0; i -= 4)
            {
                uint digit = (remainingMantissa >> i) & 0xF;
                if (digit != 0 || foundNonZero)
                {
                    mantissaHex.Append(digit.ToString("x"));
                    foundNonZero = true;
                }
            }

            return $"{signStr}0x1.{mantissaHex.ToString().TrimEnd('0')}p{(exp >= 0 ? "+" : "")}{exp}";
        }

        // <summary>
        /// Formats a double-precision number according to WebAssembly specification 6.3.2
        /// using hexadecimal format
        /// </summary>
        /// <param name="value">The double-precision value to format</param>
        /// <returns>A string representation conforming to WebAssembly spec</returns>
        public static string FormatDouble(double value)
        {
            // Handle special cases first
            if (double.IsNaN(value))
                return "nan";
            
            if (double.IsPositiveInfinity(value))
                return "inf";
            
            if (double.IsNegativeInfinity(value))
                return "-inf";
            
            if (value == 0d)
            {
                // Detect negative zero
                if (BitConverter.GetBytes(value)[7] == 0x80)
                    return "-0x0p+0";
                return "0x0p+0";
            }

            // Get the bits of the double
            ulong bits = BitConverter.ToUInt64(BitConverter.GetBytes(value), 0);
            int sign = (int)((bits >> 63) & 1);
            int exp = (int)((bits >> 52) & 0x7FF) - 1023; // Remove bias
            ulong mantissa = bits & 0xFFFFFFFFFFFFF;

            // Build the hexadecimal representation
            string signStr = sign == 1 ? "-" : "";
        
            if (mantissa == 0)
            {
                return $"{signStr}0x1p{(exp >= 0 ? "+" : "")}{exp}";
            }

            // Convert mantissa to hex
            string fullMantissa = mantissa.ToString("x").PadLeft(13, '0');
        
            // Find last non-zero character
            int lastNonZeroIndex = fullMantissa.Length - 1;
            while (lastNonZeroIndex >= 0 && fullMantissa[lastNonZeroIndex] == '0')
            {
                lastNonZeroIndex--;
            }

            // If all zeros, return without mantissa
            if (lastNonZeroIndex < 0)
            {
                return $"{signStr}0x1p{(exp >= 0 ? "+" : "")}{exp}";
            }

            // Include all digits up to the last non-zero digit
            string trimmedMantissa = fullMantissa.Substring(0, lastNonZeroIndex + 1);

            return $"{signStr}0x1.{trimmedMantissa}p{(exp >= 0 ? "+" : "")}{exp}";
        }
    }
}
//uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
//ulong bits = BitConverter.ToUInt64(BitConverter.GetBytes(value), 0);