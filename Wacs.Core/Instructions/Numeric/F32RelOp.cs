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
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public sealed class InstF32RelOp : InstructionBase, INodeComputer<float, float, int>
    {
        public static readonly InstF32RelOp F32Eq = new(OpCode.F32Eq, ExecuteF32Eq,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly InstF32RelOp F32Ne = new(OpCode.F32Ne, ExecuteF32Ne,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly InstF32RelOp F32Lt = new(OpCode.F32Lt, ExecuteF32Lt,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly InstF32RelOp F32Gt = new(OpCode.F32Gt, ExecuteF32Gt,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly InstF32RelOp F32Le = new(OpCode.F32Le, ExecuteF32Le,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        public static readonly InstF32RelOp F32Ge = new(OpCode.F32Ge, ExecuteF32Ge,
            NumericInst.ValidateOperands(pop1: ValType.F32, pop2: ValType.F32, push: ValType.I32));

        private readonly Func<float,float,int> _execute;

        private readonly NumericInst.ValidationDelegate _validate;

        private InstF32RelOp(ByteCode op, Func<float,float,int> execute, NumericInst.ValidationDelegate validate)
        {
            Op = op;
            _execute = execute;
            _validate = validate;
        }

        public override ByteCode Op { get; }

        public override void Validate(IWasmValidationContext context) => _validate(context);

        public override void Execute(ExecContext context)
        {
            float i2 = context.OpStack.PopF32();
            float i1 = context.OpStack.PopF32();
            int result = _execute(i1, i2);
            context.OpStack.PushI32(result);
        }

        public Func<ExecContext, float, float, int> GetFunc => (_, i1, i2) => _execute(i1, i2);

        private static int ExecuteF32Eq(float i1, float i2) =>
            i1 == i2 ? 1 : 0;

        private static int ExecuteF32Ne(float i1, float i2) => 
            i1 != i2 ? 1 : 0;

        private static int ExecuteF32Lt(float i1, float i2) => 
            float.IsNaN(i1) || float.IsNaN(i2) ? 0 : i1 < i2 ? 1 : 0;

        private static int ExecuteF32Gt(float i1, float i2) => 
            float.IsNaN(i1) || float.IsNaN(i2) ? 0 : i1 > i2 ? 1 : 0;

        private static int ExecuteF32Le(float i1, float i2) => 
            float.IsNaN(i1) || float.IsNaN(i2) ? 0 : i1 <= i2 ? 1 : 0;

        private static int ExecuteF32Ge(float i1, float i2) => 
            float.IsNaN(i1) || float.IsNaN(i2) ? 0 : i1 >= i2 ? 1 : 0;
    }
}