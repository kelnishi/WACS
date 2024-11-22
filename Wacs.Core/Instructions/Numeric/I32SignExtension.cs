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
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Numeric
{
    public class InstI32SignExtend : InstructionBase
    {
        private const uint ByteSign = 0x80;
        private const uint I32ByteExtend = 0xFFFF_FF80;
        private const uint ByteMask = 0xFF;

        private const uint ShortSign = 0x8000;
        private const uint I32ShortExtend = 0xFFFF_8000;
        private const uint ShortMask = 0xFFFF;

        public static readonly InstI32SignExtend I32Extend8S = new(OpCode.I32Extend8S, ExecuteI32Extend8S,
            NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32));

        public static readonly InstI32SignExtend I32Extend16S = new(OpCode.I32Extend16S, ExecuteI32Extend16S,
            NumericInst.ValidateOperands(pop: ValType.I32, push: ValType.I32));
        
        public override ByteCode Op { get; }
        
        private readonly NumericInst.ValidationDelegate _validate;
        private Func<uint, uint> _execute;
        
        private InstI32SignExtend(ByteCode op, Func<uint, uint> execute, NumericInst.ValidationDelegate validate)
        {
            Op = op;
            _execute = execute;
            _validate = validate;
        }
        
        public override void Validate(IWasmValidationContext context) => _validate(context);
        public override int Execute(ExecContext context)
        {
            uint value = context.OpStack.PopU32();
            uint result = _execute(value);
            context.OpStack.PushI32((int)result);
            return 1;
        }

        private static uint ExecuteI32Extend8S(uint value) =>
            ((value & ByteSign) != 0)
                ? (I32ByteExtend | value)
                : (ByteMask & value);

        private static uint ExecuteI32Extend16S(uint value) =>
            (value & ShortSign) != 0
                ? I32ShortExtend | value
                : ShortMask & value;
    }
}