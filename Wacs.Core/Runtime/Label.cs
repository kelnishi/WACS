using Wacs.Core.OpCodes;
namespace Wacs.Core.Runtime
{
    public class Label
    {
        public uint Arity { get; } // Number of result values the label expects
        
        public OpCode Instruction { get; }
        public int ContinuationAddress { get; } // The instruction index to jump to on branch
        

        public Label(uint arity, int continuationAddress, OpCode inst)
        {
            Arity = arity;
            ContinuationAddress = continuationAddress;
            Instruction = inst;
        }
    }
}