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
    public abstract class InstI64RelOp : InstructionBase
    {
        // @Spec 3.3.1.5. i.relop
        public static readonly InstI64RelOp I64Eq = new Signed(OpCode.I64Eq, ExecuteI64Eq,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly InstI64RelOp I64Ne = new Signed(OpCode.I64Ne, ExecuteI64Ne,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly InstI64RelOp I64LtS = new Signed(OpCode.I64LtS, ExecuteI64LtS,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly InstI64RelOp I64LtU = new Unsigned(OpCode.I64LtU, ExecuteI64LtU,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly InstI64RelOp I64GtS = new Signed(OpCode.I64GtS, ExecuteI64GtS,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly InstI64RelOp I64GtU = new Unsigned(OpCode.I64GtU, ExecuteI64GtU,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly InstI64RelOp I64LeS = new Signed(OpCode.I64LeS, ExecuteI64LeS,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly InstI64RelOp I64LeU = new Unsigned(OpCode.I64LeU, ExecuteI64LeU,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly InstI64RelOp I64GeS = new Signed(OpCode.I64GeS, ExecuteI64GeS,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        public static readonly InstI64RelOp I64GeU = new Unsigned(OpCode.I64GeU, ExecuteI64GeU,
            NumericInst.ValidateOperands(pop1: ValType.I64, pop2: ValType.I64, push: ValType.I32));

        private readonly NumericInst.ValidationDelegate _validate;

        private InstI64RelOp(ByteCode op, NumericInst.ValidationDelegate validate) : base(op, -1) 
            => _validate = validate;
        
        public int LinkStackDiff => StackDiff;
        public override void Validate(IWasmValidationContext context) => _validate(context); // -1

        [OpSource(OpCode.I64Eq)]
        private static int ExecuteI64Eq(long i1, long i2) => i1 == i2 ? 1 : 0;
        [OpSource(OpCode.I64Ne)]
        private static int ExecuteI64Ne(long i1, long i2) => i1 != i2 ? 1 : 0;
        [OpSource(OpCode.I64LtS)]
        private static int ExecuteI64LtS(long i1, long i2) => i1 < i2 ? 1 : 0;
        [OpSource(OpCode.I64LtU)]
        private static int ExecuteI64LtU(ulong i1, ulong i2) => i1 < i2 ? 1 : 0;
        [OpSource(OpCode.I64GtS)]
        private static int ExecuteI64GtS(long i1, long i2) => i1 > i2 ? 1 : 0;
        [OpSource(OpCode.I64GtU)]
        private static int ExecuteI64GtU(ulong i1, ulong i2) => i1 > i2 ? 1 : 0;
        [OpSource(OpCode.I64LeS)]
        private static int ExecuteI64LeS(long i1, long i2) => i1 <= i2 ? 1 : 0;
        [OpSource(OpCode.I64LeU)]
        private static int ExecuteI64LeU(ulong i1, ulong i2) => i1 <= i2 ? 1 : 0;
        [OpSource(OpCode.I64GeS)]
        private static int ExecuteI64GeS(long i1, long i2) => i1 >= i2 ? 1 : 0;
        [OpSource(OpCode.I64GeU)]
        private static int ExecuteI64GeU(ulong i1, ulong i2) => i1 >= i2 ? 1 : 0;

        private sealed class Signed : InstI64RelOp, INodeComputer<long,long,int>
        {
            private readonly Func<long,long,int> _execute;

            public Signed(ByteCode op, Func<long, long ,int> execute, NumericInst.ValidationDelegate validate)
                : base(op, validate) => _execute = execute;

            public Func<ExecContext, long, long, int> GetFunc => (_, i1, i2) => _execute(i1, i2);

            public override void Execute(ExecContext context)
            {
                long i2 = context.OpStack.PopI64();
                long i1 = context.OpStack.PopI64();
                int result = _execute(i1, i2);
                context.OpStack.PushI32(result);
            }
        }

        private sealed class Unsigned : InstI64RelOp, INodeComputer<ulong,ulong,int>
        {
            private readonly Func<ulong,ulong,int> _execute;

            public Unsigned(ByteCode op, Func<ulong, ulong, int> execute, NumericInst.ValidationDelegate validate)
                : base(op, validate) => _execute = execute;

            public Func<ExecContext, ulong, ulong, int> GetFunc => (_, i1, i2) => _execute(i1, i2);

            public override void Execute(ExecContext context)
            {
                ulong i2 = context.OpStack.PopU64();
                ulong i1 = context.OpStack.PopU64();
                int result = _execute(i1, i2);
                context.OpStack.PushI32(result);
            }
        }
    }
}