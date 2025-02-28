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
using System.IO;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using InstructionPointer = System.Int32;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// Helper to calculate sizes
    /// </summary>
    public interface IBlockInstruction
    {
        public int BlockSize { get; }

        public ValType BlockType { get; }

        public int Count { get; }

        public Block GetBlock(int idx);
    }
    
    public interface IIfInstruction {}

    public abstract class BlockTarget : InstructionBase
    {
        public InstructionPointer Else = -1;
        public BlockTarget EnclosingBlock;
        public InstructionPointer End;
        public InstructionPointer Head;
        public Label Label;
        public int LabelHeight;

        //Elses
        public BlockTarget? Suboridinate;

        protected BlockTarget(ByteCode block) : base(block, 0) { }
        protected BlockTarget(ByteCode block, int stack) : base(block, stack) { }

        public override InstructionBase Link(ExecContext context, InstructionPointer pointer)
        {
            _ = base.Link(context, pointer);
            
            Head = pointer;
            EnclosingBlock = context.PeekLabel();
            
            //Merge adjacent PointerAdvances
            if (this is InstBlock or InstLoop or InstTryTable)
            {
                Nop = true;
                var parent = EnclosingBlock;
                int skips = 0;
                int address = pointer;
                while (parent.Head == address - 1)
                {
                    if (parent is not (InstBlock or InstLoop or InstTryTable)) 
                        break;
                    address--;
                    skips++;
                    parent.PointerAdvance = skips;
                    parent.Nop = true;
                    parent = parent.EnclosingBlock;
                }
            }
            
            var blockInst = this as IBlockInstruction;
            var block = blockInst!.GetBlock(0);
            
            if (!context.LinkUnreachable && context.LinkOpStackHeight < 0)
                throw new WasmRuntimeException($"bad stack calculation:{context.LinkOpStackHeight} at {this}[0x{pointer:x8}]");
            
            var label = new Label
            {
                ContinuationAddress = pointer,
                Instruction = Op,
                StackHeight = context.LinkOpStackHeight
            };
            
            try
            {
                var funcType = context.Frame.Module.Types.ResolveBlockType(block.BlockType);
                if (funcType == null)
                    throw new IndexOutOfRangeException($"Could not resolve type for block instruction {this}");
                
                label.Arity = this is InstLoop ? funcType.ParameterTypes.Arity : funcType.ResultType.Arity;
                label.Parameters = funcType.ParameterTypes.Arity;
                label.Results = funcType.ResultType.Arity;
            }
            catch (IndexOutOfRangeException)
            {
                throw new InvalidDataException($"Failure computing Labels. BlockType:{block.BlockType} did not exist in the Module");
            }
            
            Label = label;
            
            //Push this onto a stack in the context so we can address the End instructions
            context.PushLabel(this);
            LabelHeight = context.LabelHeight;
            
            return this;
        }
    }
}