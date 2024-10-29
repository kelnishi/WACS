namespace Wacs.Core.Instructions
{
    /// <summary>
    /// Helper to calculate sizes
    /// </summary>
    public interface IBlockInstruction
    {
        public int Size { get; }

        public InstructionSequence GetBlock(int idx);
    }
}