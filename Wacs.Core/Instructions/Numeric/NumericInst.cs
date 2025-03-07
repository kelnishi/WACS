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

using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public partial class NumericInst : InstructionBase, IConstOpInstruction
    {
        public delegate void ExecuteDelegate(ExecContext context);

        public delegate void ValidationDelegate(IWasmValidationContext context);

        private readonly ExecuteDelegate _execute;

        private readonly ValidationDelegate _validate;

        private NumericInst(ByteCode op, ExecuteDelegate execute, ValidationDelegate validate, int stackDiff, bool isConst = false) : base(op, stackDiff) 
            => (_execute, _validate, IsConstant) = (execute, validate, isConst);

        public bool IsConstant { get; }

        public override void Validate(IWasmValidationContext context) => _validate(context);

        public override void Execute(ExecContext context)
        {
            _execute(context);
        }

        public override string RenderText(ExecContext? context)
        {
            if (context == null)
                return $"{base.RenderText(context)}";
            if (!context.Attributes.Live)
                return $"{base.RenderText(context)}";

            var val = context.OpStack.PopAny();
            string valStr = $" (;>{val}<;)";
            context.OpStack.PushValue(val);
            return $"{base.RenderText(context)} {valStr}";
        }

        // [pop] -> [push]
        public static ValidationDelegate ValidateOperands(ValType pop, ValType push) =>
            context =>
            {
                context.OpStack.PopType(pop);
                context.OpStack.PushType(push);
            };

        // [pop1 pop2] -> [push]
        public static ValidationDelegate ValidateOperands(ValType pop1, ValType pop2, ValType push) =>
            context => {
                context.OpStack.PopType(pop2);
                context.OpStack.PopType(pop1);
                context.OpStack.PushType(push);
            };

        // [pop1 pop2 pop3] -> [push]
        public static ValidationDelegate ValidateOperands(ValType pop1, ValType pop2, ValType pop3, ValType push) =>
            context => {
                context.OpStack.PopType(pop3);
                context.OpStack.PopType(pop2);
                context.OpStack.PopType(pop1);
                context.OpStack.PushType(push);
            };
    }
}