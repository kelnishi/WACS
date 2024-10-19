using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;

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

        public InstructionSequence() {}

        public InstructionSequence(IList<IInstruction> list) =>
            _instructions.AddRange(list);

        public InstructionSequence(IInstruction single) =>
            _instructions.Add(single);

        public bool IsConstant =>
            (_instructions.Count - (HasExplicitEnd ? 1 : 0)) == 1 &&
            !_instructions.Any(inst => inst.OpCode switch {
                OpCode.I32Const => false,
                OpCode.I64Const => false,
                OpCode.F32Const => false,
                OpCode.F64Const => false,
                OpCode.GlobalGet => false,
                OpCode.RefNull => false,
                OpCode.RefFunc => false,
                OpCode.End => false,
                _ => true
            });

        public bool HasExplicitEnd => _instructions[^1].OpCode == OpCode.End;
        public bool EndsWithElse => _instructions[^1].OpCode == OpCode.Else;
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

        public IEnumerator<IInstruction> GetEnumerator() => _instructions.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void SwapElseEnd()
        {
            _instructions[^1] = InstEnd.Inst;
        }
    }
}