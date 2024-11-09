using System.Collections.Generic;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class Frame
    {
        public readonly Stack<Label> Labels = new();

        public Frame(ModuleInstance moduleInstance, FunctionType type) =>
            (Module, Type) = (moduleInstance, type);

        public ModuleInstance Module { get; }
        public LocalsSpace Locals { get; set; } = new();
        public InstructionPointer ContinuationAddress { get; set; } = InstructionPointer.Nil;

        public Label Label => Labels.Peek();

        public FunctionType Type { get; }

        public FuncIdx Index { get; set; }

        public string FuncId { get; set; } = "";

        public int Arity => (int)Type.ResultType.Length;

        //For validation
        public bool ConditionallyReachable { get; set; }

        public bool Contains(LabelIdx index) =>
            index.Value < Labels.Count;

        public void ForceLabels(int depth)
        {
            while (Labels.Count < depth)
            {
                var fakeLabel = new Label(ResultType.Empty, InstructionPointer.Nil, OpCode.Nop);
                Labels.Push(fakeLabel);
            }

            while (Labels.Count > depth)
            {
                Labels.Pop();
            }
        }
    }
}