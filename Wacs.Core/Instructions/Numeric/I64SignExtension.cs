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
    public class InstI64SignExtend : InstructionBase, INodeComputer<uint,ulong>
    {
        private const uint ByteSign = 0x80;
        private const uint ByteMask = 0xFF;

        private const uint ShortSign = 0x8000;
        private const uint ShortMask = 0xFFFF;

        private const ulong I64ByteExtend = 0xFFFF_FFFF_FFFF_FF80;
        private const ulong I64ShortExtend = 0xFFFF_FFFF_FFFF_8000;

        private const ulong WordSign = 0x8000_0000;
        private const ulong WordExtend = 0xFFFF_FFFF_8000_0000;
        private const ulong WordMask = 0xFFFF_FFFF;

        public static readonly InstI64SignExtend I64Extend8S = new(OpCode.I64Extend8S, ExecuteI64Extend8S,
            NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I64));

        public static readonly InstI64SignExtend I64Extend16S = new(OpCode.I64Extend16S, ExecuteI64Extend16S,
            NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I64));

        public static readonly InstI64SignExtend I64Extend32S = new(OpCode.I64Extend32S, ExecuteI64Extend32S,
            NumericInst.ValidateOperands(pop: ValType.I64, push: ValType.I64));
        
        public override ByteCode Op { get; }
        
        private readonly NumericInst.ValidationDelegate _validate;
        private Func<uint, ulong> _execute;
        
        private InstI64SignExtend(ByteCode op, Func<uint, ulong> execute, NumericInst.ValidationDelegate validate)
        {
            Op = op;
            _execute = execute;
            _validate = validate;
        }
        
        public override void Validate(IWasmValidationContext context) => _validate(context);
        public override int Execute(ExecContext context)
        {
            uint value = context.OpStack.PopU32();
            ulong result = _execute(value);
            context.OpStack.PushI64((long)result);
            return 1;
        }
        
        public Func<ExecContext, uint,ulong> GetFunc => (_, i1) => _execute(i1);
        
        private static ulong ExecuteI64Extend8S(uint value) =>
            (value & ByteSign) != 0
                ? I64ByteExtend | value
                : ByteMask & value;

        private static ulong ExecuteI64Extend16S(uint value) =>
            (value & ShortSign) != 0
                ? I64ShortExtend | value
                : ShortMask & value;

        private static ulong ExecuteI64Extend32S(uint value) =>
            (value & WordSign) != 0
                ? WordExtend | value
                : WordMask & value;
    }
}