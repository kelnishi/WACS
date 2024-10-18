using System.Collections.Generic;
using System.IO;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class Frame
    {
        public ModuleInstance Module { get; }
        public LocalsSpace Locals { get; set; } = new();
        
        public Stack<Label> Labels = new();

        public InstructionPointer ContinuationAddress { get; set; } = InstructionPointer.Nil;

        public Label this[LabelIdx index] {
            get
            {
                if (!Contains(index))
                    throw new InvalidDataException($"Label stack underflow");
                
                var aside = new Stack<Label>();
                for (int i = 0, l = (int)index.Value; i <= l; ++i)
                {
                    aside.Push(Labels.Pop());
                }
                var result = aside.Peek();
                while (aside.Count > 0) Labels.Push(aside.Pop());
                return result;
            }
        }
        
        public bool Contains(LabelIdx index) =>
            index.Value < Labels.Count;

        public Label Label => Labels.Peek();
        
        public FunctionType Type { get; set; }

        public int Arity => (int)Type.ResultType.Length;

        public Frame(ModuleInstance moduleInstance, FunctionType type) =>
            (Module, Type) = (moduleInstance, type);
        

    }
}