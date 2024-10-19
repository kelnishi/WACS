using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class Label
    {
        public int StackHeight = 0;


        public Label(ResultType type, InstructionPointer continuationAddress, OpCode inst)
        {
            Type = type;
            ContinuationAddress = continuationAddress;
            Instruction = inst;
        }

        public ResultType Type { get; }

        public int Arity => Type.Arity;

        public OpCode Instruction { get; }
        public InstructionPointer ContinuationAddress { get; } // The instruction index to jump to on branch
    }
}