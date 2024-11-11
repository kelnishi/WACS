using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    // VvBinOps - Bit-wise Logical Operators
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16Splat = new(SimdCode.I8x16Splat, ExecuteI8x16Splat, ValidateOperands(pop: ValType.I32, push: ValType.V128));
        public static readonly NumericInst I16x8Splat = new(SimdCode.I16x8Splat, ExecuteI16x8Splat, ValidateOperands(pop: ValType.I32, push: ValType.V128));
        public static readonly NumericInst I32x4Splat = new(SimdCode.I32x4Splat, ExecuteI32x4Splat, ValidateOperands(pop: ValType.I32, push: ValType.V128));
        public static readonly NumericInst I64x2Splat = new(SimdCode.I64x2Splat, ExecuteI64x2Splat, ValidateOperands(pop: ValType.I64, push: ValType.V128));

        public static readonly NumericInst I8x16Bitmask = new(SimdCode.I8x16Bitmask, ExecuteI8x16Bitmask, ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I16x8Bitmask = new(SimdCode.I16x8Bitmask, ExecuteI16x8Bitmask, ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I32x4Bitmask = new(SimdCode.I32x4Bitmask, ExecuteI32x4Bitmask, ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly NumericInst I64x2Bitmask = new(SimdCode.I64x2Bitmask, ExecuteI64x2Bitmask, ValidateOperands(pop: ValType.I64, push: ValType.I32));

        // @Spec 4.4.3.8. shape.splat
        private static void ExecuteI8x16Splat(ExecContext context)
        {
            byte v = (byte)(uint)context.OpStack.PopI32();
            V128 result = new V128(v, v, v, v, v, v, v, v, v, v, v, v, v, v, v, v);
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8Splat(ExecContext context)
        {
            ushort v = (ushort)(uint)context.OpStack.PopI32();
            V128 result = new V128(v, v, v, v, v, v, v, v);
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4Splat(ExecContext context)
        {
            uint v = context.OpStack.PopI32();
            V128 result = new V128(v, v, v, v);
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2Splat(ExecContext context)
        {
            ulong v = context.OpStack.PopI64();
            V128 result = new V128(v, v);
            context.OpStack.PushV128(result);
        }

        // @spec 4.4.3.16. txN.bitmask
        private static void ExecuteI8x16Bitmask(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            int mask =
                (c[(byte)0x0] >> 7) << 0x0
                | (c[(byte)0x1] >> 7) << 0x1
                | (c[(byte)0x2] >> 7) << 0x2
                | (c[(byte)0x3] >> 7) << 0x3
                | (c[(byte)0x4] >> 7) << 0x4
                | (c[(byte)0x5] >> 7) << 0x5
                | (c[(byte)0x6] >> 7) << 0x6
                | (c[(byte)0x7] >> 7) << 0x7
                | (c[(byte)0x8] >> 7) << 0x8
                | (c[(byte)0x9] >> 7) << 0x9
                | (c[(byte)0xA] >> 7) << 0xA
                | (c[(byte)0xB] >> 7) << 0xB
                | (c[(byte)0xC] >> 7) << 0xC
                | (c[(byte)0xD] >> 7) << 0xD
                | (c[(byte)0xE] >> 7) << 0xE
                | (c[(byte)0xF] >> 7) << 0xF;
            context.OpStack.PushI32(mask);
        }

        private static void ExecuteI16x8Bitmask(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            int mask =
                (c[(short)0x0] >> 15) << 0x0
                | (c[(short)0x1] >> 15) << 0x1
                | (c[(short)0x2] >> 15) << 0x2
                | (c[(short)0x3] >> 15) << 0x3
                | (c[(short)0x4] >> 15) << 0x4
                | (c[(short)0x5] >> 15) << 0x5
                | (c[(short)0x6] >> 15) << 0x6
                | (c[(short)0x7] >> 15) << 0x7;
            context.OpStack.PushI32(mask);
        }

        private static void ExecuteI32x4Bitmask(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            int mask =
                (c[(int)0x0] >> 31) << 0x0
                | (c[(int)0x1] >> 31) << 0x1
                | (c[(int)0x2] >> 31) << 0x2
                | (c[(int)0x3] >> 31) << 0x3;
            context.OpStack.PushI32(mask);
        }

        private static void ExecuteI64x2Bitmask(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            int mask = (c.I64x2_0 < 0 ? 0b1 : 0) | (c.I64x2_1 < 0 ? 0b10 : 0);
            context.OpStack.PushI32(mask);
        }
    }
}
