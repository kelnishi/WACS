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
    public class InstI32TestOp : InstructionBase, INodeComputer<int, int>
    {
        // @Spec 3.3.1.4 i.testop
        public static readonly InstI32TestOp I32Eqz = new(OpCode.I32Eqz, ExecuteI32Eqz, NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32));
        private readonly Func<int, int> _execute;
        private readonly NumericInst.ValidationDelegate _validate;

        private InstI32TestOp(ByteCode op, Func<int, int> execute, NumericInst.ValidationDelegate validate)
        {
            Op = op;
            _execute = execute;
            _validate = validate;
        }

        public override ByteCode Op { get; }

        public override void Validate(IWasmValidationContext context) => _validate(context);

        public override void Execute(ExecContext context)
        {
            int i = context.OpStack.PopI32();
            int result = _execute(i);
            context.OpStack.PushI32(result);
        }

        public Func<ExecContext, int, int> GetFunc => (_, i1) => _execute(i1);

        // @Spec 4.6.1.4. t.testop
        private static int ExecuteI32Eqz(int i) => i == 0 ? 1 : 0;
    }

    public class InstI64TestOp : InstructionBase, INodeComputer<long,int>
    {
        public static readonly InstI64TestOp I64Eqz = new(OpCode.I64Eqz, ExecuteI64Eqz, NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I32));
        private readonly Func<long, int> _execute;

        private readonly NumericInst.ValidationDelegate _validate;

        private InstI64TestOp(ByteCode op, Func<long, int> execute, NumericInst.ValidationDelegate validate)
        {
            Op = op;
            _execute = execute;
            _validate = validate;
        }

        public override ByteCode Op { get; }

        public override void Validate(IWasmValidationContext context) => _validate(context);

        public override void Execute(ExecContext context)
        {
            long i = context.OpStack.PopI64();
            int result = _execute(i);
            context.OpStack.PushI32(result);
        }

        public Func<ExecContext, long, int> GetFunc => (_, i1) => _execute(i1);

        private static int ExecuteI64Eqz(long i) => i == 0 ? 1 : 0;
    }
}