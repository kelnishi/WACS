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
        public List<FunctionType> Types { get; }
        public List<FuncAddr> FuncAddrs { get; } = new List<FuncAddr>();
        public List<TableAddr> TableAddrs { get; } = new List<TableAddr>();
        public List<MemAddr> MemAddrs { get; } = new List<MemAddr>();
        public List<GlobalAddr> GlobalAddrs { get; } = new List<GlobalAddr>();
        public List<ElemAddr> ElemAddrs { get; } = new List<ElemAddr>();
        public List<DataAddr> DataAddrs { get; } = new List<DataAddr>();
        public List<ExportInstance> Exports { get; } = new List<ExportInstance>();

        public ModuleInstance(List<FunctionType> types) =>
            Types = types;

        public FunctionType this[TypeIdx idx] => Types[(Index)idx];
        public FuncAddr this[FuncIdx idx] => FuncAddrs[(Index)idx];
        public TableAddr this[TableIdx idx] => TableAddrs[(Index)idx];
        public MemAddr this[MemIdx idx] => MemAddrs[(Index)idx];
        public GlobalAddr this[GlobalIdx idx] => GlobalAddrs[(Index)idx];
        
        public FuncAddr? StartFunction { get; set; }
    }
}