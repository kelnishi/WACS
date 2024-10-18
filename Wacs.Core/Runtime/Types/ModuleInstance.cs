using System;
using System.Collections.Generic;
using System.IO;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime.Types
{
    /// <summary>
    /// @Spec 4.2.5. Module Instances
    /// Represents an instantiated WebAssembly module, containing the runtime instances of functions, tables, memories, and globals.
    /// </summary>
    public class ModuleInstance
    {
        public TypesSpace Types { get; }
        public FuncAddrs FuncAddrs { get; } = new();
        public TableAddrs TableAddrs { get; } = new();
        public MemAddrs MemAddrs { get; } = new();
        public GlobalAddrs GlobalAddrs { get; } = new();
        public ElemAddrs ElemAddrs { get; } = new();
        public List<DataAddr> DataAddrs { get; } = new();
        public List<ExportInstance> Exports { get; } = new();

        public ModuleInstance(Module module) =>
            Types = new TypesSpace(module);
        
    }
}