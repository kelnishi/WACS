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
    // VvBinOps - Bit-wise Logical Operators
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16Splat = new(SimdCode.I8x16Splat, ExecuteI8x16Splat, ValidateOperands(pop: ValType.I32, push: ValType.V128), 0);
        public static readonly NumericInst I16x8Splat = new(SimdCode.I16x8Splat, ExecuteI16x8Splat, ValidateOperands(pop: ValType.I32, push: ValType.V128), 0);
        public static readonly NumericInst I32x4Splat = new(SimdCode.I32x4Splat, ExecuteI32x4Splat, ValidateOperands(pop: ValType.I32, push: ValType.V128), 0);
        public static readonly NumericInst I64x2Splat = new(SimdCode.I64x2Splat, ExecuteI64x2Splat, ValidateOperands(pop: ValType.I64, push: ValType.V128), 0);

        public static readonly NumericInst I8x16Bitmask = new(SimdCode.I8x16Bitmask, ExecuteI8x16Bitmask, ValidateOperands(pop: ValType.V128, push: ValType.I32), 0);
        public static readonly NumericInst I16x8Bitmask = new(SimdCode.I16x8Bitmask, ExecuteI16x8Bitmask, ValidateOperands(pop: ValType.V128, push: ValType.I32), 0);
        public static readonly NumericInst I32x4Bitmask = new(SimdCode.I32x4Bitmask, ExecuteI32x4Bitmask, ValidateOperands(pop: ValType.V128, push: ValType.I32), 0);
        public static readonly NumericInst I64x2Bitmask = new(SimdCode.I64x2Bitmask, ExecuteI64x2Bitmask, ValidateOperands(pop: ValType.V128, push: ValType.I32), 0);

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
            uint v = context.OpStack.PopU32();
            V128 result = new V128(v, v, v, v);
            context.OpStack.PushV128(result);
        }

        private static void ExecuteI64x2Splat(ExecContext context)
        {
            ulong v = context.OpStack.PopU64();
            V128 result = new V128(v, v);
            context.OpStack.PushV128(result);
        }

        // @spec 4.4.3.16. txN.bitmask
        private static void ExecuteI8x16Bitmask(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            int mask =
                ((c[(byte)0x0] & 0x80) >> 7)
                | ((c[(byte)0x1] & 0x80) >> 6)
                | ((c[(byte)0x2] & 0x80) >> 5)
                | ((c[(byte)0x3] & 0x80) >> 4)
                | ((c[(byte)0x4] & 0x80) >> 3)
                | ((c[(byte)0x5] & 0x80) >> 2)
                | ((c[(byte)0x6] & 0x80) >> 1)
                | ((c[(byte)0x7] & 0x80) >> 0)
                | ((c[(byte)0x8] & 0x80) << 1)
                | ((c[(byte)0x9] & 0x80) << 2)
                | ((c[(byte)0xA] & 0x80) << 3)
                | ((c[(byte)0xB] & 0x80) << 4)
                | ((c[(byte)0xC] & 0x80) << 5)
                | ((c[(byte)0xD] & 0x80) << 6)
                | ((c[(byte)0xE] & 0x80) << 7)
                | ((c[(byte)0xF] & 0x80) << 8);
            context.OpStack.PushI32(mask);
        }

        private static void ExecuteI16x8Bitmask(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            int mask =
                ((c[(ushort)0x0] & 0x8000) >> 0xF)
                | ((c[(ushort)0x1] & 0x8000) >> 0xE)
                | ((c[(ushort)0x2] & 0x8000) >> 0xD)
                | ((c[(ushort)0x3] & 0x8000) >> 0xC)
                | ((c[(ushort)0x4] & 0x8000) >> 0xB)
                | ((c[(ushort)0x5] & 0x8000) >> 0xA)
                | ((c[(ushort)0x6] & 0x8000) >> 0x9)
                | ((c[(ushort)0x7] & 0x8000) >> 0x8);
            context.OpStack.PushI32(mask);
        }

        private static void ExecuteI32x4Bitmask(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            int mask =
                (int)(((c[(uint)0x0] & 0x8000_0000) >> 31) 
                    | ((c[(uint)0x1] & 0x8000_0000) >> 30)
                    | ((c[(uint)0x2] & 0x8000_0000) >> 29)
                    | ((c[(uint)0x3] & 0x8000_0000) >> 28));
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