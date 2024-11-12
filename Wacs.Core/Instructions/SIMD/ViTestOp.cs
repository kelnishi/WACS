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
using Wacs.Core.Types;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst
    {
        public static readonly NumericInst I8x16AllTrue = new(SimdCode.I8x16AllTrue, ExecuteI8x16AllTrue,
            ValidateOperands(pop: ValType.V128, push: ValType.I32));

        public static readonly NumericInst I16x8AllTrue = new(SimdCode.I16x8AllTrue, ExecuteI16x8AllTrue,
            ValidateOperands(pop: ValType.V128, push: ValType.I32));

        public static readonly NumericInst I32x4AllTrue = new(SimdCode.I32x4AllTrue, ExecuteI32x4AllTrue,
            ValidateOperands(pop: ValType.V128, push: ValType.I32));

        public static readonly NumericInst I64x2AllTrue = new(SimdCode.I64x2AllTrue, ExecuteI64x2AllTrue,
            ValidateOperands(pop: ValType.V128, push: ValType.I32));

        private static void ExecuteI8x16AllTrue(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            bool allTrue =
                c[(byte)0] != 0
                && c[(byte)0x1] != 0
                && c[(byte)0x2] != 0
                && c[(byte)0x3] != 0
                && c[(byte)0x4] != 0
                && c[(byte)0x5] != 0
                && c[(byte)0x6] != 0
                && c[(byte)0x7] != 0
                && c[(byte)0x8] != 0
                && c[(byte)0x9] != 0
                && c[(byte)0xA] != 0
                && c[(byte)0xB] != 0
                && c[(byte)0xC] != 0
                && c[(byte)0xD] != 0
                && c[(byte)0xE] != 0
                && c[(byte)0xF] != 0;
            int result = allTrue ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI16x8AllTrue(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            bool allTrue =
                c[(short)0] != 0
                && c[(short)0x1] != 0
                && c[(short)0x2] != 0
                && c[(short)0x3] != 0
                && c[(short)0x4] != 0
                && c[(short)0x5] != 0
                && c[(short)0x6] != 0
                && c[(short)0x7] != 0;
            int result = allTrue ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI32x4AllTrue(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            bool allTrue = 
                c[(int)0x0] != 0
                && c[(int)0x1] != 0 
                && c[(int)0x2] != 0 
                && c[(int)0x3] != 0;
            int result = allTrue ? 1 : 0;
            context.OpStack.PushI32(result);
        }

        private static void ExecuteI64x2AllTrue(ExecContext context)
        {
            V128 c = context.OpStack.PopV128();
            bool allTrue = 
                c[(long)0x0] != 0
                && c[(long)0x1] != 0;
            int result = allTrue ? 1 : 0;
            context.OpStack.PushI32(result);
        }
    }
}