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
    public sealed class InstF64RelOp : InstructionBase, INodeComputer<double,double,int>
    {
        public static readonly InstF64RelOp F64Eq = new(OpCode.F64Eq, ExecuteF64Eq,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly InstF64RelOp F64Ne = new(OpCode.F64Ne, ExecuteF64Ne,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly InstF64RelOp F64Lt = new(OpCode.F64Lt, ExecuteF64Lt,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly InstF64RelOp F64Gt = new(OpCode.F64Gt, ExecuteF64Gt,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly InstF64RelOp F64Le = new(OpCode.F64Le, ExecuteF64Le,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        public static readonly InstF64RelOp F64Ge = new(OpCode.F64Ge, ExecuteF64Ge,
            NumericInst.ValidateOperands(pop1: ValType.F64, pop2: ValType.F64, push: ValType.I32));

        private readonly Func<double,double,int> _execute;

        private readonly NumericInst.ValidationDelegate _validate;

        private InstF64RelOp(ByteCode op, Func<double,double,int> execute, NumericInst.ValidationDelegate validate) : base(op, -1)
        {
            _execute = execute;
            _validate = validate;
        }

        public int LinkStackDiff => StackDiff;
        public Func<ExecContext, double,double,int> GetFunc => (_, i1, i2) => _execute(i1, i2);

        public override void Validate(IWasmValidationContext context) => _validate(context); // -1

        public override void Execute(ExecContext context)
        {
            double i2 = context.OpStack.PopF64();
            double i1 = context.OpStack.PopF64();
            int result = _execute(i1, i2);
            context.OpStack.PushI32(result);
        }

        private static int ExecuteF64Eq(double i1, double i2) => i1 == i2 ? 1 : 0;

        private static int ExecuteF64Ne(double i1, double i2) => i1 != i2 ? 1 : 0;

        private static int ExecuteF64Lt(double i1, double i2) => 
            double.IsNaN(i1) || double.IsNaN(i2) ? 0 : i1 < i2 ? 1 : 0;

        private static int ExecuteF64Gt(double i1, double i2) => 
            double.IsNaN(i1) || double.IsNaN(i2) ? 0 : i1 > i2 ? 1 : 0;

        private static int ExecuteF64Le(double i1, double i2) => 
            double.IsNaN(i1) || double.IsNaN(i2) ? 0 : i1 <= i2 ? 1 : 0;

        private static int ExecuteF64Ge(double i1, double i2) => 
            double.IsNaN(i1) || double.IsNaN(i2) ? 0 : i1 >= i2 ? 1 : 0;
    }
}