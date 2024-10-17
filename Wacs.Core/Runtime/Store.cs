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
        private List<IFunctionInstance> Funcs { get; } = new();
        private List<TableInstance> Tables { get; } = new();
        private List<MemoryInstance> Mems { get; } = new();
        private List<GlobalInstance> Globals { get; } = new();
        private List<ElementInstance> Elems { get; } = new();
        private List<DataInstance> Datas { get; } = new();

        public IFunctionInstance this[FuncAddr addr] => Funcs[(Index)addr];
        public TableInstance this[TableAddr addr] => Tables[(Index)addr];
        public MemoryInstance this[MemAddr addr] => Mems[(Index)addr];
        public GlobalInstance this[GlobalAddr addr] => Globals[(Index)addr];
        public ElementInstance this[ElemAddr addr] => Elems[(Index)addr];

        public bool Contains(GlobalAddr addr) =>
            addr.Value < Globals.Count;

        public bool Contains(TableAddr addr) =>
            addr.Value < Tables.Count;

        public bool Contains(ElemAddr addr) =>
            addr.Value < Elems.Count;
        
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
        
        public ElemAddr AddElement(ElementInstance elem)
        {
            var addr = new ElemAddr(Elems.Count);
            Elems.Add(elem);
            return addr;
        }

        public DataAddr AddData(DataInstance data)
        {
            var addr = new DataAddr(Datas.Count);
            Datas.Add(data);
            return addr;
        }

        
    }
}