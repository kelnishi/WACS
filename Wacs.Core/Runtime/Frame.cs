using System.Collections.Generic;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public class Frame
    {
        public LocalsSpace Locals { get; set; } = null!;
        public ModuleInstance? Module { get; }

        public Frame(ModuleInstance? moduleInstance = null) =>
            Module = moduleInstance;
        
        public int ProgramCounter;
        public uint StackPointer;
        public List<IInstruction> Instructions = new List<IInstruction>();
        public uint Arity;
    }
}