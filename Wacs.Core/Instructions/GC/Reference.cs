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

using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.GC
{
    public class InstRefCast : InstructionBase
    {
        private ValType Ht;
        private bool Nullable = false;
        
        public InstRefCast(bool nullable) : base(nullable?ByteCode.RefCastNull:ByteCode.RefCast) 
            => Nullable = nullable;

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-refmathsfrefcastmathitrt
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(Ht.Validate(context.Types),
                "Instruction {0} is invalid. {1} is not a valid reference type.", Op.GetMnemonic(), Ht);

            var rtp = Ht.AsNullable().TopHeapType(context.Types);
            context.Assert(Ht.Matches(rtp, context.Types),
                "Instruction {0} is invalid. {1} did not match top type {2}", Op.GetMnemonic(), Ht, rtp);
            
            context.OpStack.PopType(rtp);   // -1
            context.OpStack.PushType(Ht);   // +0
        }

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-refmathsfrefcastmathitrt①
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(ExecContext context)
        {
            var rt1 = Ht;
            context.Assert(context.OpStack.Peek().IsRefType,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack:{context.OpStack.Peek().Type}");
            var refVal = context.OpStack.PopRefType();

            if (!Ht.Matches(refVal, context.Frame.Module.Types))
                throw new TrapException($"Instruction {Op.GetMnemonic()} failed. Type {refVal} does not match {rt1}");
            
            context.OpStack.PushRef(refVal);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            Ht = ValTypeParser.ParseHeapType(reader, Nullable);
            return this;
        }
    }
    
    public class InstRefTest : InstructionBase
    {
        private ValType Ht;
        private bool Nullable = false;
        
        public InstRefTest(bool nullable) : base(nullable?ByteCode.RefTestNull: ByteCode.RefTest) 
            => Nullable = nullable;

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-refmathsfreftestmathitrt
        /// </summary>
        /// <param name="context"></param>
        public override void Validate(IWasmValidationContext context)
        {
            context.Assert(Ht.Validate(context.Types),
                "Instruction {0} is invalid. {1} is not a valid reference type.", Op.GetMnemonic(), Ht);

            var rtp = Ht.AsNullable().TopHeapType(context.Types);
            context.Assert(Ht.Matches(rtp, context.Types),
                "Instruction {0} is invalid. {1} did not match top type {2}", Op.GetMnemonic(), Ht, rtp);
            
            context.OpStack.PopType(rtp);   // -1
            context.OpStack.PushI32();      // +0
        }

        /// <summary>
        /// https://webassembly.github.io/gc/core/bikeshed/index.html#-hrefsyntax-instr-refmathsfreftestmathitrt①
        /// </summary>
        /// <param name="context"></param>
        public override void Execute(ExecContext context)
        {
            context.Assert(context.OpStack.Peek().IsRefType,
                $"Instruction {Op.GetMnemonic()} failed. Wrong operand type on top of stack:{context.OpStack.Peek().Type}");

            var refVal = context.OpStack.PopRefType();
            int c = Ht.Matches(refVal, context.Frame.Module.Types) ? 1 : 0;
            
            context.OpStack.PushI32(c);
        }

        public override InstructionBase Parse(BinaryReader reader)
        {
            Ht = ValTypeParser.ParseHeapType(reader, Nullable);
            return this;
        }
    }
    
}