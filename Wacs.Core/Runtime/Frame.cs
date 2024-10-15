using System.Collections.Generic;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime
{
    public class Frame
    {
        public List<Value> Locals;
        public ModuleInstance Module;
        
        
        public int ProgramCounter;
        public uint StackPointer;
        public List<IInstruction> Instructions;
        public uint Arity;
    }
}