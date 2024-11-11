using System;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

// ReSharper disable InconsistentNaming
namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16Add = new(SimdCode.I8x16Add, ExecuteI8x16Add, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I8x16Sub = new(SimdCode.I8x16Sub, ExecuteI8x16Sub, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8Add = new(SimdCode.I16x8Add, ExecuteI16x8Add, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8Sub = new(SimdCode.I16x8Sub, ExecuteI16x8Sub, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4Add = new(SimdCode.I32x4Add, ExecuteI32x4Add, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4Sub = new(SimdCode.I32x4Sub, ExecuteI32x4Sub, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2Add = new(SimdCode.I64x2Add, ExecuteI64x2Add, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2Sub = new(SimdCode.I64x2Sub, ExecuteI64x2Sub, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8Mul = new(SimdCode.I16x8Mul, ExecuteI16x8Mul, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4Mul = new(SimdCode.I32x4Mul, ExecuteI32x4Mul, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2Mul = new(SimdCode.I64x2Mul, ExecuteI64x2Mul, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16NarrowI16x8S = new(SimdCode.I8x16NarrowI16x8S, ExecuteI8x16NarrowI16x8S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I8x16NarrowI16x8U = new(SimdCode.I8x16NarrowI16x8U, ExecuteI8x16NarrowI16x8U, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8NarrowI32x4S = new(SimdCode.I16x8NarrowI32x4S, ExecuteI16x8NarrowI32x4S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8NarrowI32x4U = new(SimdCode.I16x8NarrowI32x4U, ExecuteI16x8NarrowI32x4U, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I8x16AvgrU = new(SimdCode.I8x16AvgrU, ExecuteI8x16AvgrU, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8AvgrU = new(SimdCode.I16x8AvgrU, ExecuteI16x8AvgrU, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        // public static readonly NumericInst I8x16Swizzle = new(SimdCode.I8x16Swizzle, ExecuteI8x16Swizzle, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));


        //extadd_pairwise 8/16
        //extmul_half_sx
        //i32x4.dot

        // Execute methods
        private static void ExecuteI8x16Add(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)(v1.U8x16_0 + v2.U8x16_0),
                (byte)(v1.U8x16_1 + v2.U8x16_1),
                (byte)(v1.U8x16_2 + v2.U8x16_2),
                (byte)(v1.U8x16_3 + v2.U8x16_3),
                (byte)(v1.U8x16_4 + v2.U8x16_4),
                (byte)(v1.U8x16_5 + v2.U8x16_5),
                (byte)(v1.U8x16_6 + v2.U8x16_6),
                (byte)(v1.U8x16_7 + v2.U8x16_7),
                (byte)(v1.U8x16_8 + v2.U8x16_8),
                (byte)(v1.U8x16_9 + v2.U8x16_9),
                (byte)(v1.U8x16_A + v2.U8x16_A),
                (byte)(v1.U8x16_B + v2.U8x16_B),
                (byte)(v1.U8x16_C + v2.U8x16_C),
                (byte)(v1.U8x16_D + v2.U8x16_D),
                (byte)(v1.U8x16_E + v2.U8x16_E),
                (byte)(v1.U8x16_F + v2.U8x16_F)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16Sub(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)(v1.U8x16_0 - v2.U8x16_0),
                (byte)(v1.U8x16_1 - v2.U8x16_1),
                (byte)(v1.U8x16_2 - v2.U8x16_2),
                (byte)(v1.U8x16_3 - v2.U8x16_3),
                (byte)(v1.U8x16_4 - v2.U8x16_4),
                (byte)(v1.U8x16_5 - v2.U8x16_5),
                (byte)(v1.U8x16_6 - v2.U8x16_6),
                (byte)(v1.U8x16_7 - v2.U8x16_7),
                (byte)(v1.U8x16_8 - v2.U8x16_8),
                (byte)(v1.U8x16_9 - v2.U8x16_9),
                (byte)(v1.U8x16_A - v2.U8x16_A),
                (byte)(v1.U8x16_B - v2.U8x16_B),
                (byte)(v1.U8x16_C - v2.U8x16_C),
                (byte)(v1.U8x16_D - v2.U8x16_D),
                (byte)(v1.U8x16_E - v2.U8x16_E),
                (byte)(v1.U8x16_F - v2.U8x16_F)
            );
            context.OpStack.PushV128(result);
        }


        private static void ExecuteI16x8Add(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)(v1.I16x8_0 + v2.I16x8_0),
                (short)(v1.I16x8_1 + v2.I16x8_1),
                (short)(v1.I16x8_2 + v2.I16x8_2),
                (short)(v1.I16x8_3 + v2.I16x8_3),
                (short)(v1.I16x8_4 + v2.I16x8_4),
                (short)(v1.I16x8_5 + v2.I16x8_5),
                (short)(v1.I16x8_6 + v2.I16x8_6),
                (short)(v1.I16x8_7 + v2.I16x8_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8Sub(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)(v1.I16x8_0 - v2.I16x8_0),
                (short)(v1.I16x8_1 - v2.I16x8_1),
                (short)(v1.I16x8_2 - v2.I16x8_2),
                (short)(v1.I16x8_3 - v2.I16x8_3),
                (short)(v1.I16x8_4 - v2.I16x8_4),
                (short)(v1.I16x8_5 - v2.I16x8_5),
                (short)(v1.I16x8_6 - v2.I16x8_6),
                (short)(v1.I16x8_7 - v2.I16x8_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4Add(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                v1.I32x4_0 + v2.I32x4_0,
                v1.I32x4_1 + v2.I32x4_1,
                v1.I32x4_2 + v2.I32x4_2,
                v1.I32x4_3 + v2.I32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4Sub(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                v1.I32x4_0 - v2.I32x4_0,
                v1.I32x4_1 - v2.I32x4_1,
                v1.I32x4_2 - v2.I32x4_2,
                v1.I32x4_3 - v2.I32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2Add(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                v1.I64x2_0 + v2.I64x2_0,
                v1.I64x2_1 + v2.I64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2Sub(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                v1.I64x2_0 - v2.I64x2_0,
                v1.I64x2_1 - v2.I64x2_1
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8Mul(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)(v1.I16x8_0 * v2.I16x8_0),
                (short)(v1.I16x8_1 * v2.I16x8_1),
                (short)(v1.I16x8_2 * v2.I16x8_2),
                (short)(v1.I16x8_3 * v2.I16x8_3),
                (short)(v1.I16x8_4 * v2.I16x8_4),
                (short)(v1.I16x8_5 * v2.I16x8_5),
                (short)(v1.I16x8_6 * v2.I16x8_6),
                (short)(v1.I16x8_7 * v2.I16x8_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4Mul(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                v1.I32x4_0 * v2.I32x4_0,
                v1.I32x4_1 * v2.I32x4_1,
                v1.I32x4_2 * v2.I32x4_2,
                v1.I32x4_3 * v2.I32x4_3
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2Mul(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                v1.I64x2_0 * v2.I64x2_0,
                v1.I64x2_1 * v2.I64x2_1
            );
            context.OpStack.PushV128(result);
        }

        // @Spec 4.4.3.17. t2xN.narrow_t1xM_sx
        private static void ExecuteI8x16NarrowI16x8S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (sbyte)Math.Min(Math.Max(v1.I16x8_0, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_1, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_2, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_3, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_4, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_5, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_6, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v1.I16x8_7, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_0, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_1, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_2, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_3, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_4, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_5, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_6, byte.MinValue), byte.MaxValue),
                (sbyte)Math.Min(Math.Max(v2.I16x8_7, byte.MinValue), byte.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16NarrowI16x8U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)Math.Min(v1.U16x8_0, byte.MaxValue),
                (byte)Math.Min(v1.U16x8_1, byte.MaxValue),
                (byte)Math.Min(v1.U16x8_2, byte.MaxValue),
                (byte)Math.Min(v1.U16x8_3, byte.MaxValue),
                (byte)Math.Min(v1.U16x8_4, byte.MaxValue),
                (byte)Math.Min(v1.U16x8_5, byte.MaxValue),
                (byte)Math.Min(v1.U16x8_6, byte.MaxValue),
                (byte)Math.Min(v1.U16x8_7, byte.MaxValue),
                (byte)Math.Min(v2.U16x8_0, byte.MaxValue),
                (byte)Math.Min(v2.U16x8_1, byte.MaxValue),
                (byte)Math.Min(v2.U16x8_2, byte.MaxValue),
                (byte)Math.Min(v2.U16x8_3, byte.MaxValue),
                (byte)Math.Min(v2.U16x8_4, byte.MaxValue),
                (byte)Math.Min(v2.U16x8_5, byte.MaxValue),
                (byte)Math.Min(v2.U16x8_6, byte.MaxValue),
                (byte)Math.Min(v2.U16x8_7, byte.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8NarrowI32x4S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)Math.Min(Math.Max(v1.I32x4_0, sbyte.MinValue), sbyte.MaxValue),
                (short)Math.Min(Math.Max(v1.I32x4_1, sbyte.MinValue), sbyte.MaxValue),
                (short)Math.Min(Math.Max(v1.I32x4_2, sbyte.MinValue), sbyte.MaxValue),
                (short)Math.Min(Math.Max(v1.I32x4_3, sbyte.MinValue), sbyte.MaxValue),
                (short)Math.Min(Math.Max(v2.I32x4_0, sbyte.MinValue), sbyte.MaxValue),
                (short)Math.Min(Math.Max(v2.I32x4_1, sbyte.MinValue), sbyte.MaxValue),
                (short)Math.Min(Math.Max(v2.I32x4_2, sbyte.MinValue), sbyte.MaxValue),
                (short)Math.Min(Math.Max(v2.I32x4_3, sbyte.MinValue), sbyte.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8NarrowI32x4U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)Math.Min(v1.I32x4_0, ushort.MaxValue),
                (ushort)Math.Min(v1.I32x4_1, ushort.MaxValue),
                (ushort)Math.Min(v1.I32x4_2, ushort.MaxValue),
                (ushort)Math.Min(v1.I32x4_3, ushort.MaxValue),
                (ushort)Math.Min(v2.I32x4_0, ushort.MaxValue),
                (ushort)Math.Min(v2.I32x4_1, ushort.MaxValue),
                (ushort)Math.Min(v2.I32x4_2, ushort.MaxValue),
                (ushort)Math.Min(v2.I32x4_3, ushort.MaxValue)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16AvgrU(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)((v1.U8x16_0 + v2.U8x16_0 + 1) >> 1),
                (byte)((v1.U8x16_1 + v2.U8x16_1 + 1) >> 1),
                (byte)((v1.U8x16_2 + v2.U8x16_2 + 1) >> 1),
                (byte)((v1.U8x16_3 + v2.U8x16_3 + 1) >> 1),
                (byte)((v1.U8x16_4 + v2.U8x16_4 + 1) >> 1),
                (byte)((v1.U8x16_5 + v2.U8x16_5 + 1) >> 1),
                (byte)((v1.U8x16_6 + v2.U8x16_6 + 1) >> 1),
                (byte)((v1.U8x16_7 + v2.U8x16_7 + 1) >> 1),
                (byte)((v1.U8x16_8 + v2.U8x16_8 + 1) >> 1),
                (byte)((v1.U8x16_9 + v2.U8x16_9 + 1) >> 1),
                (byte)((v1.U8x16_A + v2.U8x16_A + 1) >> 1),
                (byte)((v1.U8x16_B + v2.U8x16_B + 1) >> 1),
                (byte)((v1.U8x16_C + v2.U8x16_C + 1) >> 1),
                (byte)((v1.U8x16_D + v2.U8x16_D + 1) >> 1),
                (byte)((v1.U8x16_E + v2.U8x16_E + 1) >> 1),
                (byte)((v1.U8x16_F + v2.U8x16_F + 1) >> 1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8AvgrU(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)((v1.U16x8_0 + v2.U16x8_0 + 1) >> 1),
                (ushort)((v1.U16x8_1 + v2.U16x8_1 + 1) >> 1),
                (ushort)((v1.U16x8_2 + v2.U16x8_2 + 1) >> 1),
                (ushort)((v1.U16x8_3 + v2.U16x8_3 + 1) >> 1),
                (ushort)((v1.U16x8_4 + v2.U16x8_4 + 1) >> 1),
                (ushort)((v1.U16x8_5 + v2.U16x8_5 + 1) >> 1),
                (ushort)((v1.U16x8_6 + v2.U16x8_6 + 1) >> 1),
                (ushort)((v1.U16x8_7 + v2.U16x8_7 + 1) >> 1)
            );
            context.OpStack.PushV128(result);
        }


        // @Spec 4.4.3.6. i8x16.swizzle
        private static void ExecuteI8x16Swizzle(ExecContext context)
        {
            // V128 c2 = context.OpStack.PopV128();
            // V128 c1 = context.OpStack.PopV128();
            // V128 result = new V128(
            //     c1[(byte)(c2.U8x16_0 & 0x0F)],
            //     c1[(byte)(c2.U8x16_1 & 0x0F)],
            //     c1[(byte)(c2.U8x16_2 & 0x0F)],
            //     c1[(byte)(c2.U8x16_3 & 0x0F)],
            //     c1[(byte)(c2.U8x16_4 & 0x0F)],
            //     c1[(byte)(c2.U8x16_5 & 0x0F)],
            //     c1[(byte)(c2.U8x16_6 & 0x0F)],
            //     c1[(byte)(c2.U8x16_7 & 0x0F)],
            //     c1[(byte)(c2.U8x16_8 & 0x0F)],
            //     c1[(byte)(c2.U8x16_9 & 0x0F)],
            //     c1[(byte)(c2.U8x16_A & 0x0F)],
            //     c1[(byte)(c2.U8x16_B & 0x0F)],
            //     c1[(byte)(c2.U8x16_C & 0x0F)],
            //     c1[(byte)(c2.U8x16_D & 0x0F)],
            //     c1[(byte)(c2.U8x16_E & 0x0F)],
            //     c1[(byte)(c2.U8x16_F & 0x0F)]
            // );
            // context.OpStack.PushV128(result);
        }
    }
    
}