using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I32x4TruncSatF32x4S = new(SimdCode.I32x4TruncSatF32x4S, ExecuteI32x4TruncSatF32x4S, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4TruncSatF32x4U = new(SimdCode.I32x4TruncSatF32x4U, ExecuteI32x4TruncSatF32x4U, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4TruncSatF64x2SZero = new(SimdCode.I32x4TruncSatF64x2SZero, ExecuteI32x4TruncSatF64x2SZero, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4TruncSatF64x2UZero = new(SimdCode.I32x4TruncSatF64x2UZero, ExecuteI32x4TruncSatF64x2UZero, ValidateOperands(pop: ValType.V128, push: ValType.V128));

        private static int TruncSatF32S(float f32)
        {
            return (float)Math.Truncate(f32) switch
            {
                float.NaN => 0,
                float.PositiveInfinity => int.MaxValue,
                float.NegativeInfinity => int.MinValue,
                < int.MinValue => int.MinValue,
                > int.MaxValue => int.MaxValue,
                _ => (int)(float)Math.Truncate(f32)
            };
        }

        private static void ExecuteI32x4TruncSatF32x4S(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            V128 result = new V128(
                TruncSatF32S(c.F32x4_0),
                TruncSatF32S(c.F32x4_1),
                TruncSatF32S(c.F32x4_2),
                TruncSatF32S(c.F32x4_3)
                );
            context.OpStack.PushV128(result);
        }

        private static uint TruncSatF32U(float f32)
        {
            float truncated = (float)Math.Truncate(f32);
            return truncated switch
            {
                float.NaN => 0,
                float.PositiveInfinity => uint.MaxValue,
                float.NegativeInfinity => 0,
                < 0 => 0,
                > uint.MaxValue => uint.MaxValue,
                _ => (uint)truncated
            };
        }

        private static void ExecuteI32x4TruncSatF32x4U(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            V128 result = new V128(
                TruncSatF32U(c.F32x4_0),
                TruncSatF32U(c.F32x4_1),
                TruncSatF32U(c.F32x4_2),
                TruncSatF32U(c.F32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4TruncSatF64x2SZero(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            V128 result = new V128(
                TruncSatF32S((float)c.F64x2_0),
                TruncSatF32S((float)c.F64x2_1),
                0,
                0
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4TruncSatF64x2UZero(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            V128 result = new V128(
                TruncSatF32U((float)c.F64x2_0),
                TruncSatF32U((float)c.F64x2_1),
                0,
                0
            );
            context.OpStack.PushV128(result);
        }
    }
}