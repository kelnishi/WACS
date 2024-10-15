using System.Collections.Generic;

namespace Wacs.Core.Runtime
{
    public class Label
    {
        public uint Arity { get; } // Number of result values the label expects
        public int ContinuationAddress { get; } // The instruction index to jump to on branch
        public Stack<Value> ValueStack { get; } // Snapshot of the operand stack at label entry

        public Label(uint arity, int continuationAddress, Stack<Value> valueStack)
        {
            Arity = arity;
            ContinuationAddress = continuationAddress;
            ValueStack = valueStack;
        }
    }
}