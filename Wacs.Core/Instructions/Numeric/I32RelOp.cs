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
using Wacs.Core.Instructions.Transpiler;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public abstract class InstI32RelOp : InstructionBase
    {
        // @Spec 3.3.1.5. i.relop
        public static readonly InstI32RelOp I32Eq = new Signed(OpCode.I32Eq, ExecuteI32Eq,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32RelOp I32Ne = new Signed(OpCode.I32Ne, ExecuteI32Ne,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32RelOp I32LtS = new Signed(OpCode.I32LtS, ExecuteI32LtS,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32RelOp I32LtU = new Unsigned(OpCode.I32LtU, ExecuteI32LtU,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32RelOp I32GtS = new Signed(OpCode.I32GtS, ExecuteI32GtS,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32RelOp I32GtU = new Unsigned(OpCode.I32GtU, ExecuteI32GtU,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32RelOp I32LeS = new Signed(OpCode.I32LeS, ExecuteI32LeS,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32RelOp I32LeU = new Unsigned(OpCode.I32LeU, ExecuteI32LeU,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32RelOp I32GeS = new Signed(OpCode.I32GeS, ExecuteI32GeS,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        public static readonly InstI32RelOp I32GeU = new Unsigned(OpCode.I32GeU, ExecuteI32GeU,
            NumericInst.ValidateOperands(pop1: ValType.I32, pop2: ValType.I32, push: ValType.I32));

        private readonly NumericInst.ValidationDelegate _validate;

        private InstI32RelOp(ByteCode op, NumericInst.ValidationDelegate validate)
        {
            Op = op;
            _validate = validate;
        }

        public override ByteCode Op { get; }
        public override int StackDiff => -1;

        public override void Validate(IWasmValidationContext context) => _validate(context); // -1

        private static int ExecuteI32Eq(int i1, int i2) => i1 == i2 ? 1 : 0;
        private static int ExecuteI32Ne(int i1, int i2) => i1 != i2 ? 1 : 0;
        private static int ExecuteI32LtS(int i1, int i2) => i1 < i2 ? 1 : 0;
        private static int ExecuteI32LtU(uint i1, uint i2) => i1 < i2 ? 1 : 0;
        private static int ExecuteI32GtS(int i1, int i2) => i1 > i2 ? 1 : 0;
        private static int ExecuteI32GtU(uint i1, uint i2) => i1 > i2 ? 1 : 0;
        private static int ExecuteI32LeS(int i1, int i2) => i1 <= i2 ? 1 : 0;
        private static int ExecuteI32LeU(uint i1, uint i2) => i1 <= i2 ? 1 : 0;
        private static int ExecuteI32GeS(int i1, int i2) => i1 >= i2 ? 1 : 0;
        private static int ExecuteI32GeU(uint i1, uint i2) => i1 >= i2 ? 1 : 0;

        private sealed class Signed : InstI32RelOp, INodeComputer<int,int,int>
        {
            private readonly Func<int,int,int> _execute;

            public Signed(ByteCode op, Func<int,int,int> execute, NumericInst.ValidationDelegate validate) 
                : base(op, validate) => _execute = execute;

            public Func<ExecContext, int, int, int> GetFunc => (_, i1, i2) => _execute(i1, i2);

            public override void Execute(ExecContext context)
            {
                int i2 = context.OpStack.PopI32();
                int i1 = context.OpStack.PopI32();
                int result = _execute(i1, i2);
                context.OpStack.PushI32(result);
            }
        }

        private sealed class Unsigned : InstI32RelOp, INodeComputer<uint,uint,int>
        {
            private readonly Func<uint,uint,int> _execute;

            public Unsigned(ByteCode op, Func<uint,uint,int> execute, NumericInst.ValidationDelegate validate)
                : base(op, validate) => _execute = execute;

            public Func<ExecContext, uint, uint, int> GetFunc => (_, i1, i2) => _execute(i1, i2);

            public override void Execute(ExecContext context)
            {
                uint i2 = context.OpStack.PopU32();
                uint i1 = context.OpStack.PopU32();
                int result = _execute(i1, i2);
                context.OpStack.PushI32(result);
            }
        }
    }
}