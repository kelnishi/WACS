using System;
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
        public static readonly InstructionSequence Empty = new();
        private readonly List<IInstruction> _instructions = new();

        public InstructionSequence()
        {
        }

        public InstructionSequence(IList<IInstruction> list) =>
            _instructions.AddRange(list);

        public InstructionSequence(IInstruction single) =>
            _instructions.Add(single);

        public bool HasExplicitEnd => _instructions[^1].Op == OpCode.End;
        public bool EndsWithElse => _instructions[^1].Op == OpCode.Else;
        public bool IsEmpty => _instructions.Count == 0;

        public int Count => _instructions.Count;

        public IInstruction this[int index]
        {
            get
            {
                if (index >= _instructions.Count)
                    throw new IndexOutOfRangeException(
                        $"Instruction sequence index {index} exceeds range [0,{_instructions.Count}]");
                return _instructions[index];
            }
        }

        /// <summary>
        /// The number of instructions in this sequence
        /// </summary>
        public int Length => _instructions.Count;

        /// <summary>
        /// The total number of instructions in this sequence and subsequences (blocks)
        /// </summary>
        public int Size
        {
            get
            {
                int sum = 0;
                for (int index = 0; index < _instructions.Count; index++)
                {
                    var inst = _instructions[index];
                    sum += inst is IBlockInstruction blockInst ? blockInst.Size : 1;
                }
                return sum;
            }
        }

        public IInstruction LastInstruction => _instructions[^1];

        public IEnumerator<IInstruction> GetEnumerator() => _instructions.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool IsConstant(IWasmValidationContext? ctx) =>
            _instructions.Count - (HasExplicitEnd ? 1 : 0) == 1 &&
            !_instructions.Any(inst => inst switch
            {
                IConstInstruction constInstruction => !constInstruction.IsConstant(ctx),
                InstEnd => false,
                _ => true
            });

        public void SwapElseEnd()
        {
            _instructions[^1] = InstEnd.Inst;
        }

        public bool ContainsInstruction(HashSet<ByteCode> opcodes)
        {
            foreach (var inst in _instructions)
            {
                if (opcodes.Contains(inst.Op))
                    return true;

                if (inst is not IBlockInstruction blockInstruction) continue;
                
                for (int i = 0, l = blockInstruction.Count; i < l; ++i)
                {
                    if (blockInstruction.GetBlock(i).ContainsInstruction(opcodes))
                        return true;
                }
            }

            return false;
        }
    }
}