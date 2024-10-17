using System.Collections.Generic;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class Frame
    {
        public ModuleInstance Module { get; }
        
        public LocalsSpace Locals { get; set; } = new LocalsSpace();

        public ResultType[]? Labels;
        public ResultType? Return;

        public Frame(ModuleInstance moduleInstance) =>
            Module = moduleInstance;
        
        public int ProgramCounter;
        public uint StackPointer;
        public List<IInstruction> Instructions = new List<IInstruction>();
        public uint Arity;
    }
}