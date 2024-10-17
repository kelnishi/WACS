using System;
using System.Collections.Generic;
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
        public FuncAddrs FuncAddrs { get; } = new FuncAddrs();
        public TableAddrs TableAddrs { get; } = new TableAddrs();
        public MemAddrs MemAddrs { get; } = new MemAddrs();
        public GlobalAddrs GlobalAddrs { get; } = new GlobalAddrs();
        public ElemAddrs ElemAddrs { get; } = new ElemAddrs();
        public List<DataAddr> DataAddrs { get; } = new List<DataAddr>();
        public List<ExportInstance> Exports { get; } = new List<ExportInstance>();

        public ModuleInstance(Module module) =>
            Types = new TypesSpace(module);
        
        
        public FuncAddr? StartFunction { get; set; }
    }
}