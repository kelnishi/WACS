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
using System.Net.Mime;
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
        public BlockTarget EnclosingBlock;
        public Label Label;
        public int LabelHeight;
        public InstructionPointer Head;
        public InstructionPointer Else = -1;
        public InstructionPointer End;

        //Elses
        public BlockTarget? Suboridinate;
        
        public override InstructionBase Link(ExecContext context, InstructionPointer pointer)
        {
            base.Link(context, pointer);
            
            Head = pointer;
            EnclosingBlock = context.PeekLabel();
            
            var blockInst = this as IBlockInstruction;
            var block = blockInst!.GetBlock(0);
            
            if (!context.LinkUnreachable && context.LinkOpStackHeight < 0)
                throw new WasmRuntimeException($"bad stack calculation:{context.LinkOpStackHeight}");
            
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

            switch (this)
            {
                case InstBlock:
                    //TODO: Set this when we remove the labelstack entirely.
                    // We'll need to:
                    //  1) compute branch targets
                    //  2) implement block traversal for exception handling 
                    //
                    // PointerAdvance = 1;
                    break;
            }
            
            return this;
        }
    }
}