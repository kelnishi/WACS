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
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public sealed class InstI32UnOp : InstructionBase, INodeComputer<uint, uint>
    {
        // @Spec 3.3.1.2. i.unop
        public static readonly InstI32UnOp I32Clz    = new(OpCode.I32Clz    , ExecuteI32Clz    , NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly InstI32UnOp I32Ctz    = new(OpCode.I32Ctz    , ExecuteI32Ctz    , NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32));
        public static readonly InstI32UnOp I32Popcnt = new(OpCode.I32Popcnt , ExecuteI32Popcnt , NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32));
        private readonly Func<uint, uint> _execute;

        private readonly NumericInst.ValidationDelegate _validate;

        private InstI32UnOp(ByteCode op, Func<uint, uint> execute, NumericInst.ValidationDelegate validate)
        {
            Op = op;
            _execute = execute;
            _validate = validate;
        }

        public override ByteCode Op { get; }

        public Func<ExecContext, uint, uint> GetFunc => (_, i1) => _execute(i1);

        public override void Validate(IWasmValidationContext context) => _validate(context); // +0

        public override void Execute(ExecContext context)
        {
            uint x = context.OpStack.PopU32();
            uint result = _execute(x);
            context.OpStack.PushU32(result);
        }

        // @Spec 4.3.2.20 iclz
        private static uint ExecuteI32Clz(uint x)
        {
            if (x != 0)
            {
                uint count = 0;
                // Shift bits to the left until the most significant bit is 1
                while ((x & 0x80000000) == 0)
                {
                    x <<= 1;
                    count++;
                }

                return count;
            }
            // Handle the case when x is 0 (all bits are 0)
            return 32;
        }

        // @Spec 4.3.2.21 ictz
        private static uint ExecuteI32Ctz(uint x)
        {
            if (x != 0)
            {
                uint count = 0;
                // Check each bit from the least significant bit (rightmost) upwards
                while ((x & 1) == 0)
                {
                    x >>= 1;
                    count++;
                }

                return count;
            }
            // Handle the case when x is 0 (all bits are 0)
            return 32;
        }

        // @Spec 4.3.2.22 ipopcnt
        private static uint ExecuteI32Popcnt(uint x)
        {
            uint count = 0;
            while (x != 0)
            {
                count += x & 1;  // Add 1 to count if the least significant bit is 1
                x >>= 1;         // Right shift x by 1 to process the next bit
            }
            return count;
        }
    }
}