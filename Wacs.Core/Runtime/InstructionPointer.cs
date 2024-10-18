namespace Wacs.Core.Runtime
{
    public struct InstructionPointer
    {
        public InstructionSequence Sequence;
        public int Index;

        public InstructionPointer(InstructionSequence seq, int index) =>
            (Sequence, Index) = (seq, index);

        public static InstructionPointer Nil = new(InstructionSequence.Empty, 0);
    }
}