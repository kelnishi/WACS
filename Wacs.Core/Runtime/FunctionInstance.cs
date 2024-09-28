using System.Collections.Generic;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public abstract class FunctionInstance
    {
        public FunctionType Type { get; }
        public ModuleInstance? Module { get; }
        
        public List<object> Locals { get; }

        protected FunctionInstance(FunctionType type, ModuleInstance module)
        {
            Type = type;
            Module = module;
            Locals = new List<object>();
        }

        public abstract object[] Invoke(object[] arguments);
    }
}