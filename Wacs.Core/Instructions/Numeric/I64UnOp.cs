// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using Wacs.Core.Compilation;
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public sealed class InstI64UnOp : InstructionBase, INodeComputer<ulong, ulong>
    {
        // @Spec 3.3.1.2. i.unop
        public static readonly InstI64UnOp I64Clz    = new(OpCode.I64Clz    , ExecuteI64Clz    , NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I64));
        public static readonly InstI64UnOp I64Ctz    = new(OpCode.I64Ctz    , ExecuteI64Ctz    , NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I64));
        public static readonly InstI64UnOp I64Popcnt = new(OpCode.I64Popcnt , ExecuteI64Popcnt , NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I64));
        private readonly Func<ulong, ulong> _execute;

        private readonly NumericInst.ValidationDelegate _validate;

        private InstI64UnOp(ByteCode op, Func<ulong, ulong> execute, NumericInst.ValidationDelegate validate) : base(op)
        {
            _execute = execute;
            _validate = validate;
        }
        
        public int LinkStackDiff => StackDiff;

        public Func<ExecContext, ulong, ulong> GetFunc => (_, i1) => _execute(i1);

        public override void Validate(IWasmValidationContext context) => _validate(context); // +0

        public override void Execute(ExecContext context)
        {
            ulong x = context.OpStack.PopU64();
            ulong result = _execute(x);
            context.OpStack.PushU64(result);
        }

        // @Spec 4.3.2.20 iclz
        [OpSource(OpCode.I64Clz)]
        private static ulong ExecuteI64Clz(ulong x)
        {
            if (x != 0)
            {
                ulong count = 0;
                while ((x & 0x8000000000000000) == 0)
                {
                    x <<= 1;
                    count++;
                }

                return count;
            }
            return 64;
        }

        // @Spec 4.3.2.21 ictz
        [OpSource(OpCode.I64Ctz)]
        private static ulong ExecuteI64Ctz(ulong x)
        {
            if (x != 0)
            {
                ulong count = 0;
                while ((x & 1) == 0)
                {
                    x >>= 1;
                    count++;
                }
                return count;
            }
            return 64;
        }

        // @Spec 4.3.2.22 ipopcnt
        [OpSource(OpCode.I64Popcnt)]
        private static ulong ExecuteI64Popcnt(ulong x)
        {
            ulong count = 0;
            while (x != 0)
            {
                count += x & 1;  // Add 1 to count if the least significant bit is 1
                x >>= 1;         // Right shift x by 1 to process the next bit
            }
            return count;
        }
    }
}