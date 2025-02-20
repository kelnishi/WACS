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

using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16Shl     = new (SimdCode.I8x16Shl     , ExecuteI8x16Shl    , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);
        public static readonly NumericInst I8x16ShrS    = new (SimdCode.I8x16ShrS    , ExecuteI8x16ShrS   , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);
        public static readonly NumericInst I8x16ShrU    = new (SimdCode.I8x16ShrU    , ExecuteI8x16ShrU   , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);

        public static readonly NumericInst I16x8Shl     = new (SimdCode.I16x8Shl     , ExecuteI16x8Shl    , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);
        public static readonly NumericInst I16x8ShrS    = new (SimdCode.I16x8ShrS    , ExecuteI16x8ShrS   , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);
        public static readonly NumericInst I16x8ShrU    = new (SimdCode.I16x8ShrU    , ExecuteI16x8ShrU   , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);

        public static readonly NumericInst I32x4Shl     = new (SimdCode.I32x4Shl     , ExecuteI32x4Shl    , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);
        public static readonly NumericInst I32x4ShrS    = new (SimdCode.I32x4ShrS    , ExecuteI32x4ShrS   , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);
        public static readonly NumericInst I32x4ShrU    = new (SimdCode.I32x4ShrU    , ExecuteI32x4ShrU   , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);

        public static readonly NumericInst I64x2Shl     = new (SimdCode.I64x2Shl     , ExecuteI64x2Shl    , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);
        public static readonly NumericInst I64x2ShrS    = new (SimdCode.I64x2ShrS    , ExecuteI64x2ShrS   , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);
        public static readonly NumericInst I64x2ShrU    = new (SimdCode.I64x2ShrU    , ExecuteI64x2ShrU   , ValidateOperands(pop1: ValType.V128, pop2: ValType.I32, push: ValType.V128), -1);

        private static void ExecuteI8x16Shl(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)(val.U8x16_0 << shiftAmount%8),
                (byte)(val.U8x16_1 << shiftAmount%8),
                (byte)(val.U8x16_2 << shiftAmount%8),
                (byte)(val.U8x16_3 << shiftAmount%8),
                (byte)(val.U8x16_4 << shiftAmount%8),
                (byte)(val.U8x16_5 << shiftAmount%8),
                (byte)(val.U8x16_6 << shiftAmount%8),
                (byte)(val.U8x16_7 << shiftAmount%8),
                (byte)(val.U8x16_8 << shiftAmount%8),
                (byte)(val.U8x16_9 << shiftAmount%8),
                (byte)(val.U8x16_A << shiftAmount%8),
                (byte)(val.U8x16_B << shiftAmount%8),
                (byte)(val.U8x16_C << shiftAmount%8),
                (byte)(val.U8x16_D << shiftAmount%8),
                (byte)(val.U8x16_E << shiftAmount%8),
                (byte)(val.U8x16_F << shiftAmount%8)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16ShrS(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (sbyte)(val.I8x16_0 >> shiftAmount%8),
                (sbyte)(val.I8x16_1 >> shiftAmount%8),
                (sbyte)(val.I8x16_2 >> shiftAmount%8),
                (sbyte)(val.I8x16_3 >> shiftAmount%8),
                (sbyte)(val.I8x16_4 >> shiftAmount%8),
                (sbyte)(val.I8x16_5 >> shiftAmount%8),
                (sbyte)(val.I8x16_6 >> shiftAmount%8),
                (sbyte)(val.I8x16_7 >> shiftAmount%8),
                (sbyte)(val.I8x16_8 >> shiftAmount%8),
                (sbyte)(val.I8x16_9 >> shiftAmount%8),
                (sbyte)(val.I8x16_A >> shiftAmount%8),
                (sbyte)(val.I8x16_B >> shiftAmount%8),
                (sbyte)(val.I8x16_C >> shiftAmount%8),
                (sbyte)(val.I8x16_D >> shiftAmount%8),
                (sbyte)(val.I8x16_E >> shiftAmount%8),
                (sbyte)(val.I8x16_F >> shiftAmount%8)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI8x16ShrU(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (byte)(val.U8x16_0 >> shiftAmount%8),
                (byte)(val.U8x16_1 >> shiftAmount%8),
                (byte)(val.U8x16_2 >> shiftAmount%8),
                (byte)(val.U8x16_3 >> shiftAmount%8),
                (byte)(val.U8x16_4 >> shiftAmount%8),
                (byte)(val.U8x16_5 >> shiftAmount%8),
                (byte)(val.U8x16_6 >> shiftAmount%8),
                (byte)(val.U8x16_7 >> shiftAmount%8),
                (byte)(val.U8x16_8 >> shiftAmount%8),
                (byte)(val.U8x16_9 >> shiftAmount%8),
                (byte)(val.U8x16_A >> shiftAmount%8),
                (byte)(val.U8x16_B >> shiftAmount%8),
                (byte)(val.U8x16_C >> shiftAmount%8),
                (byte)(val.U8x16_D >> shiftAmount%8),
                (byte)(val.U8x16_E >> shiftAmount%8),
                (byte)(val.U8x16_F >> shiftAmount%8)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8Shl(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)(val.U16x8_0 << shiftAmount%16),
                (ushort)(val.U16x8_1 << shiftAmount%16),
                (ushort)(val.U16x8_2 << shiftAmount%16),
                (ushort)(val.U16x8_3 << shiftAmount%16),
                (ushort)(val.U16x8_4 << shiftAmount%16),
                (ushort)(val.U16x8_5 << shiftAmount%16),
                (ushort)(val.U16x8_6 << shiftAmount%16),
                (ushort)(val.U16x8_7 << shiftAmount%16)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ShrS(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (short)(val.I16x8_0 >> shiftAmount%16),
                (short)(val.I16x8_1 >> shiftAmount%16),
                (short)(val.I16x8_2 >> shiftAmount%16),
                (short)(val.I16x8_3 >> shiftAmount%16),
                (short)(val.I16x8_4 >> shiftAmount%16),
                (short)(val.I16x8_5 >> shiftAmount%16),
                (short)(val.I16x8_6 >> shiftAmount%16),
                (short)(val.I16x8_7 >> shiftAmount%16)
            );
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI16x8ShrU(ExecContext context) 
        {
            int shiftAmount = context.OpStack.PopI32();
            V128 val = context.OpStack.PopV128();
            V128 result = new V128(
                (ushort)(val.U16x8_0 >> shiftAmount%16),
                (ushort)(val.U16x8_1 >> shiftAmount%16),
                (ushort)(val.U16x8_2 >> shiftAmount%16),
                (ushort)(val.U16x8_3 >> shiftAmount%16),
                (ushort)(val.U16x8_4 >> shiftAmount%16),
                (ushort)(val.U16x8_5 >> shiftAmount%16),
                (ushort)(val.U16x8_6 >> shiftAmount%16),
                (ushort)(val.U16x8_7 >> shiftAmount%16)
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