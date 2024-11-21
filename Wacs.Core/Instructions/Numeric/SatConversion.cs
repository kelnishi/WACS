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
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I32TruncSatF32S = new(ExtCode.I32TruncSatF32S, ExecuteI32TruncSatF32S,
            ValidateOperands(pop: ValType.F32, push: ValType.I32));

        public static readonly NumericInst I32TruncSatF32U = new(ExtCode.I32TruncSatF32U, ExecuteI32TruncSatF32U,
            ValidateOperands(pop: ValType.F32, push: ValType.I32));

        public static readonly NumericInst I32TruncSatF64S = new(ExtCode.I32TruncSatF64S, ExecuteI32TruncSatF64S,
            ValidateOperands(pop: ValType.F64, push: ValType.I32));

        public static readonly NumericInst I32TruncSatF64U = new(ExtCode.I32TruncSatF64U, ExecuteI32TruncSatF64U,
            ValidateOperands(pop: ValType.F64, push: ValType.I32));

        public static readonly NumericInst I64TruncSatF32S = new(ExtCode.I64TruncSatF32S, ExecuteI64TruncSatF32S,
            ValidateOperands(pop: ValType.F32, push: ValType.I64));

        public static readonly NumericInst I64TruncSatF32U = new(ExtCode.I64TruncSatF32U, ExecuteI64TruncSatF32U,
            ValidateOperands(pop: ValType.F32, push: ValType.I64));

        public static readonly NumericInst I64TruncSatF64S = new(ExtCode.I64TruncSatF64S, ExecuteI64TruncSatF64S,
            ValidateOperands(pop: ValType.F64, push: ValType.I64));

        public static readonly NumericInst I64TruncSatF64U = new(ExtCode.I64TruncSatF64U, ExecuteI64TruncSatF64U,
            ValidateOperands(pop: ValType.F64, push: ValType.I64));

        // https://github.com/WebAssembly/spec/blob/master/proposals/nontrapping-float-to-int-conversion/Overview.md
        private static void ExecuteI32TruncSatF32S(ExecContext context)
        {
            float value = context.OpStack.PopF32();
            int result = TruncSatF32S(value);
            context.OpStack.PushI32(result);
        }

        public static int TruncSatF32S(float value)
        {
            // Handle special cases first
            if (float.IsNaN(value)) return 0;
            if (float.IsPositiveInfinity(value)) return int.MaxValue;
            if (float.IsNegativeInfinity(value)) return int.MinValue;
            // Handle regular values with explicit range checks
            // Use double for intermediate calculations to maintain precision
            double doubleValue = value;
            if (doubleValue < int.MinValue) return int.MinValue;
            if (doubleValue > int.MaxValue) return int.MaxValue;
            // For values within range, perform truncation
            // Convert to double first to maintain consistency across platforms
            return (int)Math.Truncate(doubleValue);
        }

        private static void ExecuteI32TruncSatF32U(ExecContext context)
        {
            float value = context.OpStack.PopF32();
            uint result = TruncSatF32U(value);
            context.OpStack.PushU32(result);
        }

        public static uint TruncSatF32U(float value)
        {
            // Handle special cases first
            if (float.IsNaN(value)) return 0;
            if (float.IsPositiveInfinity(value)) return uint.MaxValue;
            if (float.IsNegativeInfinity(value)) return 0;
            // Handle regular values with explicit range checks
            // Use double for intermediate calculations to maintain precision
            double doubleValue = value;
            // Note: using <= instead of < to handle negative zero
            if (doubleValue <= 0) return 0;
            // Note: using >= for consistency
            if (doubleValue >= uint.MaxValue) return uint.MaxValue;
            // For values within range, perform truncation
            // Convert to double first to maintain consistency across platforms
            return (uint)Math.Truncate(doubleValue);
        }

        private static void ExecuteI32TruncSatF64S(ExecContext context)
        {
            double value = context.OpStack.PopF64();
            int result = TruncSatF64S(value);
            context.OpStack.PushI32(result);
        }

        public static int TruncSatF64S(double value)
        {
            // Handle special cases first
            if (double.IsNaN(value)) return 0;
            if (double.IsPositiveInfinity(value)) return int.MaxValue;
            if (double.IsNegativeInfinity(value)) return int.MinValue;
            // Handle regular values with explicit range checks
            if (value < int.MinValue) return int.MinValue;
            if (value > int.MaxValue) return int.MaxValue;
            // For values within range, perform truncation
            return (int)Math.Truncate(value);
        }

        private static void ExecuteI32TruncSatF64U(ExecContext context)
        {
            double value = context.OpStack.PopF64();
            uint result = TruncSatF64U(value);
            context.OpStack.PushU32(result);
        }

        public static uint TruncSatF64U(double value)
        {
            // Handle special cases first
            if (double.IsNaN(value)) return 0;
            if (double.IsPositiveInfinity(value)) return uint.MaxValue;
            if (double.IsNegativeInfinity(value)) return 0;
            // Handle regular values with explicit range checks
            // Note: Handle negative values
            if (value < 0) return 0;
            // Note: Handle overflow beyond uint range
            if (value > uint.MaxValue) return uint.MaxValue;
            // Perform truncation for values within the valid range
            return (uint)Math.Truncate(value);
        }

        private static void ExecuteI64TruncSatF32S(ExecContext context)
        {
            float value = context.OpStack.PopF32();

            // Handle special cases first
            if (float.IsNaN(value))
            {
                context.OpStack.PushI64(0);
                return;
            }

            if (float.IsPositiveInfinity(value))
            {
                context.OpStack.PushI64(long.MaxValue);
                return;
            }

            if (float.IsNegativeInfinity(value))
            {
                context.OpStack.PushI64(long.MinValue);
                return;
            }

            // Since float has less precision than long, we need to be careful with boundary checks
            // float's mantissa is 23 bits, which means it can't precisely represent all long values
            // The maximum exact integer representable by float32 is 2^24 = 16777216
    
            if (Math.Abs(value) >= 9223372036854775808.0f) // 2^63
            {
                // If the absolute value is >= 2^63, we need to saturate
                context.OpStack.PushI64(value > 0 ? long.MaxValue : long.MinValue);
                return;
            }

            // For values within range, perform truncation using double for higher precision
            double doubleValue = value;
            long result = (long)Math.Truncate(doubleValue);
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64TruncSatF32U(ExecContext context)
        {
            float value = context.OpStack.PopF32();

            // Handle special cases first
            if (float.IsNaN(value))
            {
                context.OpStack.PushI64(0);
                return;
            }

            if (float.IsPositiveInfinity(value))
            {
                context.OpStack.PushI64(-1); // This is ulong.MaxValue when interpreted as unsigned
                return;
            }

            if (float.IsNegativeInfinity(value))
            {
                context.OpStack.PushI64(0);
                return;
            }

            // Since float has less precision than ulong, we need to handle large values carefully
            // Check if the value is too large for float32 to represent precisely in the ulong range
            if (value >= 18446744073709551616.0f) // 2^64
            {
                context.OpStack.PushI64(-1); // ulong.MaxValue when interpreted as unsigned
                return;
            }

            if (value <= 0) // Handle negative values and negative zero
            {
                context.OpStack.PushI64(0);
                return;
            }

            // For values within range, perform truncation using double for higher precision
            double doubleValue = value;
            ulong result = (ulong)Math.Truncate(doubleValue);
            context.OpStack.PushI64((long)result);
        }

        private static void ExecuteI64TruncSatF64S(ExecContext context)
        {
            double value = context.OpStack.PopF64();

            // Handle special cases first
            if (double.IsNaN(value))
            {
                context.OpStack.PushI64(0);
                return;
            }

            if (double.IsPositiveInfinity(value))
            {
                context.OpStack.PushI64(long.MaxValue);
                return;
            }

            if (double.IsNegativeInfinity(value))
            {
                context.OpStack.PushI64(long.MinValue);
                return;
            }

            // For f64, we need to handle values near 2^63 carefully
            if (Math.Abs(value) >= 9223372036854775808.0) // 2^63
            {
                context.OpStack.PushI64(value > 0 ? long.MaxValue : long.MinValue);
                return;
            }

            // For values within range, we can directly truncate
            // No need for intermediate double conversion since we're already using double
            long result = (long)Math.Truncate(value);
            context.OpStack.PushI64(result);
        }

        private static void ExecuteI64TruncSatF64U(ExecContext context)
        {
            double value = context.OpStack.PopF64();

            // Handle special cases first
            if (double.IsNaN(value))
            {
                context.OpStack.PushI64(0);
                return;
            }

            if (double.IsPositiveInfinity(value))
            {
                context.OpStack.PushI64(-1); // ulong.MaxValue when interpreted as unsigned
                return;
            }

            if (double.IsNegativeInfinity(value))
            {
                context.OpStack.PushI64(0);
                return;
            }

            // Check for values too large to represent in ulong
            if (value >= 18446744073709551616.0) // 2^64
            {
                context.OpStack.PushI64(-1); // ulong.MaxValue when interpreted as unsigned
                return;
            }

            if (value <= 0) // Handle negative values and negative zero
            {
                context.OpStack.PushI64(0);
                return;
            }

            // For values within range, direct truncation is safe
            ulong result = (ulong)Math.Truncate(value);
            context.OpStack.PushI64((long)result);
        }
    }
}