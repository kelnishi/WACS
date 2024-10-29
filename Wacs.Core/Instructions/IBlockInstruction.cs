using Wacs.Core.Types;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// Helper to calculate sizes
    /// </summary>
    public interface IBlockInstruction
    {
        public int Size { get; }

        public BlockType Type { get; }

        public int Count { get; }

        public InstructionSequence GetBlock(int idx);
    }
}