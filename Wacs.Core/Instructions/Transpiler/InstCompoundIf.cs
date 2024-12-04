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
using System.IO;
using FluentValidation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions.Transpiler
{
    public class InstCompoundIf : BlockTarget, IBlockInstruction
    {
        private static readonly ByteCode IfOp = OpCode.If;
        private static readonly ByteCode ElseOp = OpCode.Else;
        private Block IfBlock = Block.Empty;
        private Block ElseBlock = Block.Empty;
        private int ElseCount;
        public override ByteCode Op => IfOp;

        private Func<ExecContext, int> valueFunc;
        
        public InstCompoundIf(
            BlockType type,
            InstructionSequence ifSeq,
            InstructionSequence elseSeq,
            ITypedValueProducer<int> valueProducer)
        {
            IfBlock = new Block(
                type: type,
                seq: ifSeq
            );
            ElseBlock = new Block(
                type: type,
                seq: elseSeq
            );
            ElseCount = ElseBlock.Instructions.Count;

            valueFunc = valueProducer.GetFunc;
        }
        
        public BlockType Type => IfBlock.Type;

        public int Count => ElseBlock.Length == 0 ? 1 : 2;

        public int Size => 1 + IfBlock.Size + ElseBlock.Size;
        public Block GetBlock(int idx) => idx == 0 ? IfBlock : ElseBlock;

        // @Spec 3.3.8.5 if
        public override void Validate(IWasmValidationContext context)
        {
            try
            {
                var ifType = context.Types.ResolveBlockType(IfBlock.Type);
                context.Assert(ifType,  "Invalid BlockType: {0}",IfBlock.Type);

                //Pop the predicate
                // context.OpStack.PopI32();
                
                //Check the parameters [t1*] and discard
                context.OpStack.DiscardValues(ifType.ParameterTypes);
                
                //ControlStack will push the values back on (Control Frame is our Label)
                context.PushControlFrame(IfOp, ifType);

                //Continue on to instructions in sequence
                // *any (end) contained within will pop the control frame and check values
                // *any (else) contained within will pop and repush the control frame
                context.ValidateBlock(IfBlock);
                
                var elseType = context.Types.ResolveBlockType(ElseBlock.Type);
                if (!ifType.Equivalent(elseType))
                    throw new ValidationException($"If block returned type {ifType} without matching else block");
                
                if (ElseBlock.Length == 0)
                    return;
                
                //Continue on to instructions in sequence
                // *(end) contained within will pop the control frame and check values
                context.ValidateBlock(ElseBlock, 1);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    "Instruction loop invalid. BlockType {0} did not exist in the Context.",IfBlock.Type);
            }
        }
        
        // @Spec 4.4.8.5. if
        public override void Execute(ExecContext context)
        {
            int c = valueFunc(context);
            if (c != 0)
            {
                context.EnterBlock(this, IfBlock);
            }
            else
            {
                if (ElseCount != 0)
                    context.EnterBlock(this, ElseBlock);
            }
        }

    }
}