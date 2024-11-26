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

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Validation;

namespace Wacs.Core
{
    /// <summary>
    /// A linear sequence of instructions
    /// We'll use this instead of arrays or lists, so we can abstract
    /// pointers into the code.
    /// </summary>
    public class InstructionSequence : IEnumerable<IInstruction>
    {
        public static readonly InstructionSequence Empty = new(new List<IInstruction>());

        //public for direct array access on critical path
        public readonly IInstruction[] _instructions;

        public readonly int Count;

        public InstructionSequence(IList<IInstruction> list)
        {
            _instructions = list.ToArray();
            Count = _instructions.Length;
        }

        public IInstruction? this[int index]
        {
            get
            {
                if (index >= Count)
                    return null;
                return _instructions[index];
            }
        }

        public bool HasExplicitEnd => _instructions[^1].Op == OpCode.End;
        public bool EndsWithElse => _instructions[^1].Op == OpCode.Else;

        /// <summary>
        /// The total number of instructions in this sequence and subsequences (blocks)
        /// </summary>
        public int Size
        {
            get
            {
                int sum = 0;
                for (int index = 0; index < Count; index++)
                {
                    var inst = _instructions[index];
                    sum += inst is IBlockInstruction blockInst ? blockInst.Size : 1;
                }
                return sum;
            }
        }

        public IInstruction LastInstruction => _instructions[^1];

        public IEnumerator<IInstruction> GetEnumerator() => ((IEnumerable<IInstruction>)_instructions).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool IsConstant(IWasmValidationContext? ctx) =>
            !_instructions.Any(inst => inst switch
            {
                IContextConstInstruction ctxInstruction => !ctxInstruction.IsConstant(ctx), 
                IConstInstruction => false,
                IConstOpInstruction opInst => !opInst.IsConstant,
                InstEnd => false,
                _ => true
            });

        public bool ContainsInstruction(HashSet<ByteCode> opcodes)
        {
            foreach (var inst in _instructions)
            {
                if (opcodes.Contains(inst.Op))
                    return true;

                if (inst is not IBlockInstruction blockInstruction) continue;
                
                for (int i = 0, l = blockInstruction.Count; i < l; ++i)
                {
                    if (blockInstruction.GetBlock(i).Instructions.ContainsInstruction(opcodes))
                        return true;
                }
            }

            return false;
        }
    }
}