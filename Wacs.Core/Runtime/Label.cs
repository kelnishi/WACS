using Wacs.Core.OpCodes;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public struct Label
    {
        public int StackHeight;

        public Label(ResultType type, InstructionPointer continuationAddress, ByteCode inst)
        {
            Type = type;
            ContinuationAddress = continuationAddress;
            Instruction = inst;
            StackHeight = 0;
        }

        public ResultType Type { get; }

        public int Arity => Type.Arity;

        public ByteCode Instruction { get; }

        public InstructionPointer ContinuationAddress { get; } // The instruction index to jump to on branch
    }
}