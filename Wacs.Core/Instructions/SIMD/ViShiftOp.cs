using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16Shl     = new (SimdCode.I8x16Shl     , ExecuteI8x16Shl    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I8x16ShrS    = new (SimdCode.I8x16ShrS    , ExecuteI8x16ShrS   , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I8x16ShrU    = new (SimdCode.I8x16ShrU    , ExecuteI8x16ShrU   , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I16x8Shl     = new (SimdCode.I16x8Shl     , ExecuteI16x8Shl    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8ShrS    = new (SimdCode.I16x8ShrS    , ExecuteI16x8ShrS   , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I16x8ShrU    = new (SimdCode.I16x8ShrU    , ExecuteI16x8ShrU   , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I32x4Shl     = new (SimdCode.I32x4Shl     , ExecuteI32x4Shl    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4ShrS    = new (SimdCode.I32x4ShrS    , ExecuteI32x4ShrS   , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I32x4ShrU    = new (SimdCode.I32x4ShrU    , ExecuteI32x4ShrU   , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        public static readonly NumericInst I64x2Shl     = new (SimdCode.I64x2Shl     , ExecuteI64x2Shl    , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2ShrS    = new (SimdCode.I64x2ShrS    , ExecuteI64x2ShrS   , ValidateOperands(pop: ValType.V128, push: ValType.V128));
        public static readonly NumericInst I64x2ShrU    = new (SimdCode.I64x2ShrU    , ExecuteI64x2ShrU   , ValidateOperands(pop: ValType.V128, push: ValType.V128));

        private static void ExecuteI8x16Shl(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)(val.U8x16_0 << shiftAmount),
                (byte)(val.U8x16_1 << shiftAmount),
                (byte)(val.U8x16_2 << shiftAmount),
                (byte)(val.U8x16_3 << shiftAmount),
                (byte)(val.U8x16_4 << shiftAmount),
                (byte)(val.U8x16_5 << shiftAmount),
                (byte)(val.U8x16_6 << shiftAmount),
                (byte)(val.U8x16_7 << shiftAmount),
                (byte)(val.U8x16_8 << shiftAmount),
                (byte)(val.U8x16_9 << shiftAmount),
                (byte)(val.U8x16_A << shiftAmount),
                (byte)(val.U8x16_B << shiftAmount),
                (byte)(val.U8x16_C << shiftAmount),
                (byte)(val.U8x16_D << shiftAmount),
                (byte)(val.U8x16_E << shiftAmount),
                (byte)(val.U8x16_F << shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16ShrS(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (sbyte)(val.I8x16_0 >> shiftAmount),
                (sbyte)(val.I8x16_1 >> shiftAmount),
                (sbyte)(val.I8x16_2 >> shiftAmount),
                (sbyte)(val.I8x16_3 >> shiftAmount),
                (sbyte)(val.I8x16_4 >> shiftAmount),
                (sbyte)(val.I8x16_5 >> shiftAmount),
                (sbyte)(val.I8x16_6 >> shiftAmount),
                (sbyte)(val.I8x16_7 >> shiftAmount),
                (sbyte)(val.I8x16_8 >> shiftAmount),
                (sbyte)(val.I8x16_9 >> shiftAmount),
                (sbyte)(val.I8x16_A >> shiftAmount),
                (sbyte)(val.I8x16_B >> shiftAmount),
                (sbyte)(val.I8x16_C >> shiftAmount),
                (sbyte)(val.I8x16_D >> shiftAmount),
                (sbyte)(val.I8x16_E >> shiftAmount),
                (sbyte)(val.I8x16_F >> shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16ShrU(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)(val.U8x16_0 >> shiftAmount),
                (byte)(val.U8x16_1 >> shiftAmount),
                (byte)(val.U8x16_2 >> shiftAmount),
                (byte)(val.U8x16_3 >> shiftAmount),
                (byte)(val.U8x16_4 >> shiftAmount),
                (byte)(val.U8x16_5 >> shiftAmount),
                (byte)(val.U8x16_6 >> shiftAmount),
                (byte)(val.U8x16_7 >> shiftAmount),
                (byte)(val.U8x16_8 >> shiftAmount),
                (byte)(val.U8x16_9 >> shiftAmount),
                (byte)(val.U8x16_A >> shiftAmount),
                (byte)(val.U8x16_B >> shiftAmount),
                (byte)(val.U8x16_C >> shiftAmount),
                (byte)(val.U8x16_D >> shiftAmount),
                (byte)(val.U8x16_E >> shiftAmount),
                (byte)(val.U8x16_F >> shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8Shl(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)(val.U16x8_0 << shiftAmount),
                (ushort)(val.U16x8_1 << shiftAmount),
                (ushort)(val.U16x8_2 << shiftAmount),
                (ushort)(val.U16x8_3 << shiftAmount),
                (ushort)(val.U16x8_4 << shiftAmount),
                (ushort)(val.U16x8_5 << shiftAmount),
                (ushort)(val.U16x8_6 << shiftAmount),
                (ushort)(val.U16x8_7 << shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ShrS(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (short)(val.I16x8_0 >> shiftAmount),
                (short)(val.I16x8_1 >> shiftAmount),
                (short)(val.I16x8_2 >> shiftAmount),
                (short)(val.I16x8_3 >> shiftAmount),
                (short)(val.I16x8_4 >> shiftAmount),
                (short)(val.I16x8_5 >> shiftAmount),
                (short)(val.I16x8_6 >> shiftAmount),
                (short)(val.I16x8_7 >> shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ShrU(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)(val.U16x8_0 >> shiftAmount),
                (ushort)(val.U16x8_1 >> shiftAmount),
                (ushort)(val.U16x8_2 >> shiftAmount),
                (ushort)(val.U16x8_3 >> shiftAmount),
                (ushort)(val.U16x8_4 >> shiftAmount),
                (ushort)(val.U16x8_5 >> shiftAmount),
                (ushort)(val.U16x8_6 >> shiftAmount),
                (ushort)(val.U16x8_7 >> shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4Shl(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (uint)(val.U32x4_0 << shiftAmount),
                (uint)(val.U32x4_1 << shiftAmount),
                (uint)(val.U32x4_2 << shiftAmount),
                (uint)(val.U32x4_3 << shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ShrS(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (int)(val.I32x4_0 >> shiftAmount),
                (int)(val.I32x4_1 >> shiftAmount),
                (int)(val.I32x4_2 >> shiftAmount),
                (int)(val.I32x4_3 >> shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI32x4ShrU(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (uint)(val.U32x4_0 >> shiftAmount),
                (uint)(val.U32x4_1 >> shiftAmount),
                (uint)(val.U32x4_2 >> shiftAmount),
                (uint)(val.U32x4_3 >> shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2Shl(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (ulong)(val.U64x2_0 << shiftAmount),
                (ulong)(val.U64x2_1 << shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2ShrS(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (long)(val.I64x2_0 >> shiftAmount),
                (long)(val.I64x2_1 >> shiftAmount)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2ShrU(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (ulong)(val.U64x2_0 >> shiftAmount),
                (ulong)(val.U64x2_1 >> shiftAmount)
            );
            context.OpStack.PushV128(result);
        }
    }
}