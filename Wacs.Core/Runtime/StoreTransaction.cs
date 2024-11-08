using System.Collections.Generic;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime
{
    public class StoreTransaction
    {
        public Dictionary<FuncAddr,IFunctionInstance> Funcs { get; } = new();
        public Dictionary<TableAddr,TableInstance> Tables { get; } = new();
        public Dictionary<MemAddr,MemoryInstance> Mems { get; } = new();
        public Dictionary<GlobalAddr,GlobalInstance> Globals { get; } = new();
        public Dictionary<ElemAddr,ElementInstance> Elems { get; } = new();
        public Dictionary<DataAddr,DataInstance> Datas { get; } = new();
    }
}