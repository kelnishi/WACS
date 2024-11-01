using System.Collections.Generic;
using System.IO;
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

        public Label this[LabelIdx index] {
            get
            {
                if (!Contains(index))
                    throw new InvalidDataException($"Label stack underflow");
                
                var aside = new Stack<Label>();
                for (int i = 0, l = (int)index.Value; i < l; ++i)
                {
                    aside.Push(Labels.Pop());
                }
                var result = Labels.Peek();
                while (aside.Count > 0) Labels.Push(aside.Pop());
                return result;
            }
        }

        public Label Label => Labels.Peek();

        public FunctionType Type { get; }

        public FuncIdx Index { get; set; }

        public int Arity => (int)Type.ResultType.Length;

        //For validation
        public bool Unreachable { get; set; }

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