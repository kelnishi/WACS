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

        public static readonly NumericInst I8x16NarrowI16x8S = new(SimdCode.I8x16NarrowI16x8S, ExecuteI8x16NarrowI16x8S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I8x16NarrowI16x8U = new(SimdCode.I8x16NarrowI16x8U, ExecuteI8x16NarrowI16x8U, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8NarrowI32x4S = new(SimdCode.I16x8NarrowI32x4S, ExecuteI16x8NarrowI32x4S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8NarrowI32x4U = new(SimdCode.I16x8NarrowI32x4U, ExecuteI16x8NarrowI32x4U, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8ExtendLowI8x16S  = new (SimdCode.I16x8ExtendLowI8x16S , ExecuteI16x8ExtendLowI8x16S , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8ExtendHighI8x16S = new (SimdCode.I16x8ExtendHighI8x16S, ExecuteI16x8ExtendHighI8x16S, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8ExtendLowI8x16U  = new (SimdCode.I16x8ExtendLowI8x16U , ExecuteI16x8ExtendLowI8x16U , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8ExtendHighI8x16U = new (SimdCode.I16x8ExtendHighI8x16U, ExecuteI16x8ExtendHighI8x16U, ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4ExtendLowI16x8S  = new(SimdCode.I32x4ExtendLowI16x8S,  ExecuteI32x4ExtendLowI16x8S,  ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4ExtendHighI16x8S = new(SimdCode.I32x4ExtendHighI16x8S, ExecuteI32x4ExtendHighI16x8S, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4ExtendLowI16x8U  = new(SimdCode.I32x4ExtendLowI16x8U,  ExecuteI32x4ExtendLowI16x8U,  ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4ExtendHighI16x8U = new(SimdCode.I32x4ExtendHighI16x8U, ExecuteI32x4ExtendHighI16x8U, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2ExtendLowI32x4S  = new(SimdCode.I64x2ExtendLowI32x4S,  ExecuteI64x2ExtendLowI32x4S,  ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2ExtendHighI32x4S = new(SimdCode.I64x2ExtendHighI32x4S, ExecuteI64x2ExtendHighI32x4S, ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2ExtendLowI32x4U  = new(SimdCode.I64x2ExtendLowI32x4U,  ExecuteI64x2ExtendLowI32x4U,  ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2ExtendHighI32x4U = new(SimdCode.I64x2ExtendHighI32x4U, ExecuteI64x2ExtendHighI32x4U, ValidateOperands(pop: ValType.V128, push: ValType.V128));

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

        // @Spec 4.4.3.17. t2xN.narrow_t1xM_sx
        private static void ExecuteI8x16NarrowI16x8S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (sbyte)Math.Min(Math.Max(v1.I16x8_0, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_1, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_2, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_3, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_4, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_5, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_6, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_7, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_0, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_1, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_2, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_3, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_4, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_5, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_6, sbyte.MinValue), sbyte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_7, sbyte.MinValue), sbyte.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16NarrowI16x8U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)Math.Min(Math.Max(v1.I16x8_0, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v1.I16x8_1, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v1.I16x8_2, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v1.I16x8_3, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v1.I16x8_4, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v1.I16x8_5, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v1.I16x8_6, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v1.I16x8_7, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v2.I16x8_0, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v2.I16x8_1, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v2.I16x8_2, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v2.I16x8_3, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v2.I16x8_4, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v2.I16x8_5, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v2.I16x8_6, byte.MinValue), byte.MaxValue),
                (byte)Math.Min(Math.Max(v2.I16x8_7, byte.MinValue), byte.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8NarrowI32x4S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)Math.Min(Math.Max(v1.I32x4_0, short.MinValue), short.MaxValue),
                (short)Math.Min(Math.Max(v1.I32x4_1, short.MinValue), short.MaxValue),
                (short)Math.Min(Math.Max(v1.I32x4_2, short.MinValue), short.MaxValue),
                (short)Math.Min(Math.Max(v1.I32x4_3, short.MinValue), short.MaxValue),
                (short)Math.Min(Math.Max(v2.I32x4_0, short.MinValue), short.MaxValue),
                (short)Math.Min(Math.Max(v2.I32x4_1, short.MinValue), short.MaxValue),
                (short)Math.Min(Math.Max(v2.I32x4_2, short.MinValue), short.MaxValue),
                (short)Math.Min(Math.Max(v2.I32x4_3, short.MinValue), short.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8NarrowI32x4U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)Math.Min(Math.Max(v1.I32x4_0, ushort.MinValue), ushort.MaxValue),
                (ushort)Math.Min(Math.Max(v1.I32x4_1, ushort.MinValue), ushort.MaxValue),
                (ushort)Math.Min(Math.Max(v1.I32x4_2, ushort.MinValue), ushort.MaxValue),
                (ushort)Math.Min(Math.Max(v1.I32x4_3, ushort.MinValue), ushort.MaxValue),
                (ushort)Math.Min(Math.Max(v2.I32x4_0, ushort.MinValue), ushort.MaxValue),
                (ushort)Math.Min(Math.Max(v2.I32x4_1, ushort.MinValue), ushort.MaxValue),
                (ushort)Math.Min(Math.Max(v2.I32x4_2, ushort.MinValue), ushort.MaxValue),
                (ushort)Math.Min(Math.Max(v2.I32x4_3, ushort.MinValue), ushort.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ExtendLowI8x16S(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (short)v.I8x16_0,
                (short)v.I8x16_1,
                (short)v.I8x16_2,
                (short)v.I8x16_3,
                (short)v.I8x16_4,
                (short)v.I8x16_5,
                (short)v.I8x16_6,
                (short)v.I8x16_7
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ExtendHighI8x16S(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (short)v.I8x16_8,
                (short)v.I8x16_9,
                (short)v.I8x16_A,
                (short)v.I8x16_B,
                (short)v.I8x16_C,
                (short)v.I8x16_D,
                (short)v.I8x16_E,
                (short)v.I8x16_F
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ExtendLowI8x16U(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)v.U8x16_0,
                (ushort)v.U8x16_1,
                (ushort)v.U8x16_2,
                (ushort)v.U8x16_3,
                (ushort)v.U8x16_4,
                (ushort)v.U8x16_5,
                (ushort)v.U8x16_6,
                (ushort)v.U8x16_7
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ExtendHighI8x16U(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)v.U8x16_8,
                (ushort)v.U8x16_9,
                (ushort)v.U8x16_A,
                (ushort)v.U8x16_B,
                (ushort)v.U8x16_C,
                (ushort)v.U8x16_D,
                (ushort)v.U8x16_E,
                (ushort)v.U8x16_F
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ExtendLowI16x8S(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (int)v.I16x8_0,
                (int)v.I16x8_1,
                (int)v.I16x8_2,
                (int)v.I16x8_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ExtendHighI16x8S(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (int)v.I16x8_4,
                (int)v.I16x8_5,
                (int)v.I16x8_6,
                (int)v.I16x8_7
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ExtendLowI16x8U(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (uint)v.U16x8_0,
                (uint)v.U16x8_1,
                (uint)v.U16x8_2,
                (uint)v.U16x8_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ExtendHighI16x8U(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (uint)v.U16x8_4,
                (uint)v.U16x8_5,
                (uint)v.U16x8_6,
                (uint)v.U16x8_7
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2ExtendLowI32x4S(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (long)v.I32x4_0,
                (long)v.I32x4_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2ExtendHighI32x4S(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (long)v.I32x4_2,
                (long)v.I32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2ExtendLowI32x4U(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (ulong)v.U32x4_0,
                (ulong)v.U32x4_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2ExtendHighI32x4U(ExecContext context)
        {
            V128 v = context.OpStack.PopV128();
            V128 result = new V128(
                (ulong)v.U32x4_2,
                (ulong)v.U32x4_3
            );
            context.OpStack.PushV128(result);
        }
    }
}