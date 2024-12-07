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
using FluentValidation;
using Wacs.Core.Types.Defs;
using Wacs.Core.Validation;

namespace Wacs.Core.Types
{
    public class Block
    {
        public static readonly Block Empty = new(ValType.Empty, InstructionSequence.Empty);

        public readonly ValType BlockType;

        public readonly InstructionSequence Instructions;

        public Block(ValType blockType, InstructionSequence seq)
        {
            BlockType = blockType;
            Instructions = seq;
        }

        private TypeIdx TypeIndex => BlockType.Index();

        /// <summary>
        /// The number of immediate child instructions 
        /// </summary>
        public int Length => Instructions.Count;

        /// <summary>
        /// The total number of instructions in the tree below
        /// </summary>
        public int Size => Instructions.Size;

        /// <summary>
        /// @Spec 3.2.2. Block Types
        /// </summary>
        public class Validator : AbstractValidator<Block>
        {
            public Validator()
            {
                // @Spec 3.2.2.1. typeidx
                // @Spec 3.2.2.2. [valtype?]
                RuleFor(b => b.BlockType)
                    .Must((_, type, ctx) => ctx.GetValidationContext().ValidateType(type))
                    .WithMessage("Blocks must have a defined BlockType if not a ValType index");

            }
        }
    }


}