using System;
using System.Collections.Generic;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// @Spec 4.2.3. Store
    /// </summary>
    public class Store
    {
        public List<IFunctionInstance> Funcs { get; } = new List<IFunctionInstance>();
        public List<TableInstance> Tables { get; } = new List<TableInstance>();
        public List<MemoryInstance> Mems { get; } = new List<MemoryInstance>();
        public List<GlobalInstance> Globals { get; } = new List<GlobalInstance>();
        public List<ElementInstance> Elems { get; } = new List<ElementInstance>();
        public List<DataInstance> Datas { get; } = new List<DataInstance>();

        public IFunctionInstance this[FuncAddr addr] => Funcs[(Index)addr];
        public TableInstance this[TableAddr addr] => Tables[(Index)addr];
        public MemoryInstance this[MemAddr addr] => Mems[(Index)addr];
        public GlobalInstance this[GlobalAddr addr] => Globals[(Index)addr];
        
        public FuncAddr AddFunction(IFunctionInstance func)
        {
            var addr = new FuncAddr(Funcs.Count);
            Funcs.Add(func);
            return addr;
        }

        public TableAddr AddTable(TableInstance table)
        {
            var addr = new TableAddr(Tables.Count);
            Tables.Add(table);
            return addr;
        }
        
        public MemAddr AddMemory(MemoryInstance mem)
        {
            var addr = new MemAddr(Mems.Count);
            Mems.Add(mem);
            return addr;
        }
        
        public GlobalAddr AddGlobal(GlobalInstance global)
        {
            var addr = new GlobalAddr(Globals.Count);
            Globals.Add(global);
            return addr;
        }

        
        
    }
}