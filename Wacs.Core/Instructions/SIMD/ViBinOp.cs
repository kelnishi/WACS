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

        public static readonly NumericInst I8x16AvgrU = new(SimdCode.I8x16AvgrU, ExecuteI8x16AvgrU, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8AvgrU = new(SimdCode.I16x8AvgrU, ExecuteI16x8AvgrU, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8ExtAddPairwiseI8x16S = new (SimdCode.I16x8ExtAddPairwiseI8x16S, ExecuteI16x8ExtAddPairwiseI8x16S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8ExtAddPairwiseI8x16U = new (SimdCode.I16x8ExtAddPairwiseI8x16U, ExecuteI16x8ExtAddPairwiseI8x16U, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4ExtAddPairwiseI16x8S = new (SimdCode.I32x4ExtAddPairwiseI16x8S, ExecuteI32x4ExtAddPairwiseI16x8S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4ExtAddPairwiseI16x8U = new (SimdCode.I32x4ExtAddPairwiseI16x8U, ExecuteI32x4ExtAddPairwiseI16x8U, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8ExtMulLowI8x16S  = new (SimdCode.I16x8ExtMulLowI8x16S , ExecuteI16x8ExtMulLowI8x16S , ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8ExtMulHighI8x16S = new (SimdCode.I16x8ExtMulHighI8x16S, ExecuteI16x8ExtMulHighI8x16S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8ExtMulLowI8x16U  = new (SimdCode.I16x8ExtMulLowI8x16U , ExecuteI16x8ExtMulLowI8x16U , ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8ExtMulHighI8x16U = new (SimdCode.I16x8ExtMulHighI8x16U, ExecuteI16x8ExtMulHighI8x16U, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4ExtMulLowI16x8S  = new (SimdCode.I32x4ExtMulLowI16x8S , ExecuteI32x4ExtMulLowI16x8S , ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4ExtMulHighI16x8S = new (SimdCode.I32x4ExtMulHighI16x8S, ExecuteI32x4ExtMulHighI16x8S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4ExtMulLowI16x8U  = new (SimdCode.I32x4ExtMulLowI16x8U , ExecuteI32x4ExtMulLowI16x8U , ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4ExtMulHighI16x8U = new (SimdCode.I32x4ExtMulHighI16x8U, ExecuteI32x4ExtMulHighI16x8U, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2ExtMulLowI32x4S  = new (SimdCode. I64x2ExtMulLowI32x4S ,  ExecuteI64x2ExtMulLowI32x4S , ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2ExtMulHighI32x4S = new (SimdCode. I64x2ExtMulHighI32x4S,  ExecuteI64x2ExtMulHighI32x4S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2ExtMulLowI32x4U  = new (SimdCode. I64x2ExtMulLowI32x4U ,  ExecuteI64x2ExtMulLowI32x4U , ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2ExtMulHighI32x4U = new (SimdCode. I64x2ExtMulHighI32x4U,  ExecuteI64x2ExtMulHighI32x4U, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4DotI16x8S = new (SimdCode.I32x4DotI16x8S, ExecuteI32x4DotI16x8S, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8Q15MulRSatS = new(SimdCode.I16x8Q15MulRSatS, ExecuteI16x8Q15MulRSatS, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I8x16Swizzle = new(SimdCode.I8x16Swizzle, ExecuteI8x16Swizzle, ValidateOperands(pop1: ValType.V128, pop2: ValType.V128, push: ValType.V128));


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

        private static void ExecuteI16x8ExtAddPairwiseI8x16S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)((short)v1.I8x16_0 + (short)v2.I8x16_0),
                (short)((short)v1.I8x16_1 + (short)v2.I8x16_1),
                (short)((short)v1.I8x16_2 + (short)v2.I8x16_2),
                (short)((short)v1.I8x16_3 + (short)v2.I8x16_3),
                (short)((short)v1.I8x16_4 + (short)v2.I8x16_4),
                (short)((short)v1.I8x16_5 + (short)v2.I8x16_5),
                (short)((short)v1.I8x16_6 + (short)v2.I8x16_6),
                (short)((short)v1.I8x16_7 + (short)v2.I8x16_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ExtAddPairwiseI8x16U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)((ushort)v1.U8x16_0 + (ushort)v2.U8x16_0),
                (ushort)((ushort)v1.U8x16_1 + (ushort)v2.U8x16_1),
                (ushort)((ushort)v1.U8x16_2 + (ushort)v2.U8x16_2),
                (ushort)((ushort)v1.U8x16_3 + (ushort)v2.U8x16_3),
                (ushort)((ushort)v1.U8x16_4 + (ushort)v2.U8x16_4),
                (ushort)((ushort)v1.U8x16_5 + (ushort)v2.U8x16_5),
                (ushort)((ushort)v1.U8x16_6 + (ushort)v2.U8x16_6),
                (ushort)((ushort)v1.U8x16_7 + (ushort)v2.U8x16_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ExtAddPairwiseI16x8S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (int)((int)v1.I16x8_0 + (int)v2.I16x8_0),
                (int)((int)v1.I16x8_1 + (int)v2.I16x8_1),
                (int)((int)v1.I16x8_2 + (int)v2.I16x8_2),
                (int)((int)v1.I16x8_3 + (int)v2.I16x8_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ExtAddPairwiseI16x8U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (uint)((uint)v1.U16x8_0 + (uint)v2.U16x8_0),
                (uint)((uint)v1.U16x8_2 + (uint)v2.U16x8_2),
                (uint)((uint)v1.U16x8_4 + (uint)v2.U16x8_4),
                (uint)((uint)v1.U16x8_6 + (uint)v2.U16x8_6)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ExtMulLowI8x16S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)((short)v1.I8x16_0 * (short)v2.I8x16_0),
                (short)((short)v1.I8x16_1 * (short)v2.I8x16_1),
                (short)((short)v1.I8x16_2 * (short)v2.I8x16_2),
                (short)((short)v1.I8x16_3 * (short)v2.I8x16_3),
                (short)((short)v1.I8x16_4 * (short)v2.I8x16_4),
                (short)((short)v1.I8x16_5 * (short)v2.I8x16_5),
                (short)((short)v1.I8x16_6 * (short)v2.I8x16_6),
                (short)((short)v1.I8x16_7 * (short)v2.I8x16_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ExtMulHighI8x16S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (short)((short)v1.I8x16_8 * (short)v2.I8x16_8),
                (short)((short)v1.I8x16_9 * (short)v2.I8x16_9),
                (short)((short)v1.I8x16_A * (short)v2.I8x16_A),
                (short)((short)v1.I8x16_B * (short)v2.I8x16_B),
                (short)((short)v1.I8x16_C * (short)v2.I8x16_C),
                (short)((short)v1.I8x16_D * (short)v2.I8x16_D),
                (short)((short)v1.I8x16_E * (short)v2.I8x16_E),
                (short)((short)v1.I8x16_F * (short)v2.I8x16_F)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ExtMulLowI8x16U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)((ushort)v1.U8x16_0 * (ushort)v2.U8x16_0),
                (ushort)((ushort)v1.U8x16_1 * (ushort)v2.U8x16_1),
                (ushort)((ushort)v1.U8x16_2 * (ushort)v2.U8x16_2),
                (ushort)((ushort)v1.U8x16_3 * (ushort)v2.U8x16_3),
                (ushort)((ushort)v1.U8x16_4 * (ushort)v2.U8x16_4),
                (ushort)((ushort)v1.U8x16_5 * (ushort)v2.U8x16_5),
                (ushort)((ushort)v1.U8x16_6 * (ushort)v2.U8x16_6),
                (ushort)((ushort)v1.U8x16_7 * (ushort)v2.U8x16_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ExtMulHighI8x16U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)((ushort)v1.U8x16_8 * (ushort)v2.U8x16_8),
                (ushort)((ushort)v1.U8x16_9 * (ushort)v2.U8x16_9),
                (ushort)((ushort)v1.U8x16_A * (ushort)v2.U8x16_A),
                (ushort)((ushort)v1.U8x16_B * (ushort)v2.U8x16_B),
                (ushort)((ushort)v1.U8x16_C * (ushort)v2.U8x16_C),
                (ushort)((ushort)v1.U8x16_D * (ushort)v2.U8x16_D),
                (ushort)((ushort)v1.U8x16_E * (ushort)v2.U8x16_E),
                (ushort)((ushort)v1.U8x16_F * (ushort)v2.U8x16_F)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ExtMulLowI16x8S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (int)((int)v1.I8x16_0 * (int)v2.I8x16_0),
                (int)((int)v1.I8x16_1 * (int)v2.I8x16_1),
                (int)((int)v1.I8x16_2 * (int)v2.I8x16_2),
                (int)((int)v1.I8x16_3 * (int)v2.I8x16_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ExtMulHighI16x8S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (int)((int)v1.I8x16_4 * (int)v2.I8x16_4),
                (int)((int)v1.I8x16_5 * (int)v2.I8x16_5),
                (int)((int)v1.I8x16_6 * (int)v2.I8x16_6),
                (int)((int)v1.I8x16_7 * (int)v2.I8x16_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ExtMulLowI16x8U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (uint)((uint)v1.U8x16_0 * (uint)v2.U8x16_0),
                (uint)((uint)v1.U8x16_1 * (uint)v2.U8x16_1),
                (uint)((uint)v1.U8x16_2 * (uint)v2.U8x16_2),
                (uint)((uint)v1.U8x16_3 * (uint)v2.U8x16_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ExtMulHighI16x8U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (uint)((uint)v1.U8x16_4 * (uint)v2.U8x16_4),
                (uint)((uint)v1.U8x16_5 * (uint)v2.U8x16_5),
                (uint)((uint)v1.U8x16_6 * (uint)v2.U8x16_6),
                (uint)((uint)v1.U8x16_7 * (uint)v2.U8x16_7)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2ExtMulLowI32x4S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (long)((long)v1.I32x4_0 * (long)v2.I32x4_0),
                (long)((long)v1.I32x4_1 * (long)v2.I32x4_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2ExtMulHighI32x4S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (long)((long)v1.I32x4_2 * (long)v2.I32x4_2),
                (long)((long)v1.I32x4_3 * (long)v2.I32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2ExtMulLowI32x4U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (ulong)((ulong)v1.U32x4_0 * (ulong)v2.U32x4_0),
                (ulong)((ulong)v1.U32x4_1 * (ulong)v2.U32x4_1)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2ExtMulHighI32x4U(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                (ulong)((ulong)v1.U32x4_2 * (ulong)v2.U32x4_2),
                (ulong)((ulong)v1.U32x4_3 * (ulong)v2.U32x4_3)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4DotI16x8S(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();

            V128 result = new V128(
                ((int)v1.I16x8_0 * (int)v2.I16x8_0 + (int)v1.I16x8_1 * (int)v2.I16x8_1),
                ((int)v1.I16x8_2 * (int)v2.I16x8_2 + (int)v1.I16x8_3 * (int)v2.I16x8_3),
                ((int)v1.I16x8_4 * (int)v2.I16x8_4 + (int)v1.I16x8_5 * (int)v2.I16x8_5),
                ((int)v1.I16x8_6 * (int)v2.I16x8_6 + (int)v1.I16x8_7 * (int)v2.I16x8_7)
            );
            
            context.OpStack.PushV128(result);
        }

        private static short Q15MulRSat(short a, short b)
        {
            return (short)Math.Clamp((a * b + 16384) >> 15, short.MinValue, short.MaxValue);
        }

        private static void ExecuteI16x8Q15MulRSatS(ExecContext context)
        {
            V128 v2 = context.OpStack.PopV128();
            V128 v1 = context.OpStack.PopV128();
            V128 result = new V128(
                Q15MulRSat(v1.I16x8_0, v2.I16x8_0),
                Q15MulRSat(v1.I16x8_1, v2.I16x8_1),
                Q15MulRSat(v1.I16x8_2, v2.I16x8_2),
                Q15MulRSat(v1.I16x8_3, v2.I16x8_3),
                Q15MulRSat(v1.I16x8_4, v2.I16x8_4),
                Q15MulRSat(v1.I16x8_5, v2.I16x8_5),
                Q15MulRSat(v1.I16x8_6, v2.I16x8_6),
                Q15MulRSat(v1.I16x8_7, v2.I16x8_7)
            );
            context.OpStack.PushV128(result);
        }

        // @Spec 4.4.3.6. i8x16.swizzle
        private static void ExecuteI8x16Swizzle(ExecContext context)
        {
            V128 c2 = context.OpStack.PopV128();
            V128 c1 = context.OpStack.PopV128();
            MV128 result = new();
            for (byte i = 0; i < 16; ++i)
            {
                byte index = c2[i];
                result[i] = index >= 16 ? (byte)0 : c1[index];
            }
            context.OpStack.PushV128(result);
        }
    }
    
}