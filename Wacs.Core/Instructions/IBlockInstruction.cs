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

using Wacs.Core.Runtime;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// Helper to calculate sizes
    /// </summary>
    public interface IBlockInstruction
    {
        public int Size { get; }

        public ValType BlockType { get; }

        public int Count { get; }

        public Block GetBlock(int idx);
    }


    public abstract class BlockTarget : InstructionBase
    {
        public BlockTarget EnclosingBlock;
        public Label Label;
    }
}