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

using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.GC
{
    public class InstRefI31 : InstructionBase, IConstInstruction
    {
        public override ByteCode Op => GcCode.RefI31;
        
        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-i31mathsfrefi31
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopI32();
            context.OpStack.PushType(ValType.I31NN);
        }
        
        private const int SignBit31 = 0x4000_0000;
        private const int UnsignedMask = 0x3FFF_FFFF;
        private const ulong SignExtendBits = 0xFFFF_FFFF_C000_0000;
        
        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-i31mathsfrefi31①
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsI32,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type at top of stack {context.OpStack.Peek().Type}.");
            long i = context.OpStack.PopI32();
            
            if ((i & SignBit31) != 0)
            {
                i = (long)(SignExtendBits | unchecked((ulong)i));
            }
            else
            {
                i &= UnsignedMask;
            }
            
            var val = new Value(ValType.I31NN, i, null);
            context.OpStack.PushValue(val);
        }
    }

    public class InstI32GetS : InstructionBase
    {
        private const int SignBit31 = 0x4000_0000;
        private const int UnsignedMask = 0x3FFF_FFFF;
        public override ByteCode Op => GcCode.I31GetS;
        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopType(ValType.I31);
            context.OpStack.PushI32();
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsType(ValType.I31),
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type at top of stack {context.OpStack.Peek().Type}.");
            var refVal = context.OpStack.PopType(ValType.I31);
            if (refVal.IsNullRef)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Reference was null");
            context.Assert(refVal.Type.Matches(ValType.I31, context.Frame.Module.Types),
                $"Instruction {Op.GetMnemonic()} failed. Wrong reference type {refVal.Type}.");
            int j = (int)refVal.Data.Ptr;
            switch (j)
            {
                case < 0: j |= SignBit31; break;
                default: j &= UnsignedMask; break;
            }
            context.OpStack.PushI32(j);
        }
    }
    
    public class InstI32GetU : InstructionBase
    {
        private const uint BitMask31 = 0x7FFF_FFFF;
        public override ByteCode Op => GcCode.I31GetU;
        public override void Validate(IWasmValidationContext context)
        {
            context.OpStack.PopType(ValType.I31);
            context.OpStack.PushI32();
        }

        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsType(ValType.I31),
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type at top of stack {context.OpStack.Peek().Type}.");
            var refVal = context.OpStack.PopType(ValType.I31);
            if (refVal.IsNullRef)
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Reference was null");
            context.Assert(refVal.Type.Matches(ValType.I31, context.Frame.Module.Types),
                $"Instruction {Op.GetMnemonic()} failed. Wrong reference type {refVal.Type}.");
            uint j = (uint)refVal.Data.Ptr & BitMask31;
            context.OpStack.PushU32(j);
        }
    }
}