// Copyright 2025 Kelvin Nishikawa
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
using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;
using Wacs.Core.Validation;

namespace Wacs.Core.Instructions
{
    public class InstTryTable : InstructionBase, IBlockInstruction
    {
        private static readonly ByteCode TryTableOp = OpCode.TryTable;
        private Block Block;
        private CatchType[] Catches;
        
        public override ByteCode Op => TryTableOp;
        public ValType BlockType => Block.BlockType;
        public int Count => 1;
        public int Size => 1 + Block.Size;
        public Block GetBlock(int idx) => Block;
        
        public override void Validate(IWasmValidationContext context)
        {
            try
            {
                var funcType = context.Types.ResolveBlockType(Block.BlockType);
                context.Assert(funcType,  "Invalid BlockType: {0}",Block.BlockType);
                
                //Check the parameters [t1*] and discard
                context.OpStack.DiscardValues(funcType.ParameterTypes);
                
                //ControlStack will push the values back on (Control Frame is our Label)
                context.PushControlFrame(TryTableOp, funcType);
                
                context.ValidateCatches(Catches);
                
                context.ValidateBlock(Block);
            }
            catch (IndexOutOfRangeException exc)
            {
                _ = exc;
                //Types didn't hit
                context.Assert(false,
                    "Instruction block was invalid. BlockType {0} did not exist in the Context.",Block.BlockType);
            }
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            var blockType = ValTypeParser.Parse(reader, parseBlockIndex: true, parseStorageType: false);
            Catches = reader.ParseVector(CatchType.Parse);
            Block = new Block(
                blockType: blockType,
                seq: new InstructionSequence(reader.ParseUntil(BinaryModuleParser.ParseInstruction, IsEnd))
            );
            return this;
        }
    }
    
    public class InstThrow : InstructionBase
    {

        private TagIdx X;
        public override ByteCode Op => OpCode.Throw;
        public override void Validate(IWasmValidationContext context)
        {
            throw new NotImplementedException();
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }
        
        public override InstructionBase Parse(BinaryReader reader)
        {
            X = (TagIdx)reader.ReadLeb128_u32();
            return this;
        }
    }
    
    public class InstThrowRef : InstructionBase
    {
        public override ByteCode Op => OpCode.ThrowRef;
        public override void Validate(IWasmValidationContext context)
        {
            throw new NotImplementedException();
        }

        public override void Execute(ExecContext context)
        {
            throw new NotImplementedException();
        }
    }
}