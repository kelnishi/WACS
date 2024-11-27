// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Collections.Generic;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// @Spec 4.2.3. Store
    /// </summary>
    public class Store
    {
        private readonly List<DataInstance> Datas = new();
        private readonly List<ElementInstance> Elems = new();

        private readonly List<IFunctionInstance> Funcs = new();
        private readonly List<GlobalInstance> Globals = new();
        
        private MemoryInstance?[] Mems = new MemoryInstance[32];
        private int MemsCount = 0;
        
        private readonly List<TableInstance> Tables = new();
        private StoreTransaction? CurrentTransaction = null;

        public IFunctionInstance this[FuncAddr addr] => 
            CurrentTransaction?.Funcs.GetValueOrDefault(addr)??Funcs[addr.Value];

        public TableInstance this[TableAddr addr] => 
            CurrentTransaction?.Tables.GetValueOrDefault(addr)??Tables[addr.Value];

        public MemoryInstance this[MemAddr addr] => 
            CurrentTransaction?.Mems.GetValueOrDefault(addr)??Mems[addr.Value];

        public GlobalInstance this[GlobalAddr addr] =>
            CurrentTransaction?.Globals.GetValueOrDefault(addr)??Globals[addr.Value];

        public ElementInstance this[ElemAddr addr] =>
            CurrentTransaction?.Elems.GetValueOrDefault(addr)??Elems[addr.Value];

        public DataInstance this[DataAddr addr] =>
            CurrentTransaction?.Datas.GetValueOrDefault(addr)??Datas[addr.Value];

        public bool Contains(FuncAddr addr) => addr.Value < Funcs.Count || (CurrentTransaction?.Funcs.ContainsKey(addr) ?? false);
        public bool Contains(TableAddr addr) => addr.Value < Tables.Count || (CurrentTransaction?.Tables.ContainsKey(addr) ?? false);
        public bool Contains(MemAddr addr) => addr.Value < MemsCount || (CurrentTransaction?.Mems.ContainsKey(addr) ?? false);
        public bool Contains(GlobalAddr addr) => addr.Value < Globals.Count || (CurrentTransaction?.Globals.ContainsKey(addr) ?? false);
        public bool Contains(ElemAddr addr) => addr.Value < Elems.Count || (CurrentTransaction?.Elems.ContainsKey(addr) ?? false);
        public bool Contains(DataAddr addr) => addr.Value < Datas.Count || (CurrentTransaction?.Datas.ContainsKey(addr) ?? false);

        public void OpenTransaction()
        {
            if (CurrentTransaction != null)
                throw new InvalidOperationException("Only one StoreTransaction at a time!");
            
            CurrentTransaction = new StoreTransaction();
        }

        public void CommitTransaction()
        {
            if (CurrentTransaction == null)
                throw new InvalidOperationException("No open transaction to commit");

            foreach (var (addr, inst) in CurrentTransaction.Funcs) Funcs[addr.Value] = inst;
            foreach (var (addr, inst) in CurrentTransaction.Tables) Tables[addr.Value] = inst;
            foreach (var (addr, inst) in CurrentTransaction.Mems) Mems[addr.Value] = inst;
            foreach (var (addr, inst) in CurrentTransaction.Globals) Globals[addr.Value] = inst;
            foreach (var (addr, inst) in CurrentTransaction.Elems) Elems[addr.Value] = inst;
            foreach (var (addr, inst) in CurrentTransaction.Datas) Datas[addr.Value] = inst;
            CurrentTransaction = null;
        }

        public void DiscardTransaction()
        {
            if (CurrentTransaction == null)
                throw new InvalidOperationException("No open transaction to discard");
            
            while (Funcs.Count > 0 && Funcs[^1] == null) Funcs.RemoveAt(Funcs.Count - 1);
            while (Tables.Count > 0 && Tables[^1] == null) Tables.RemoveAt(Tables.Count - 1);
            while (Globals.Count > 0 && Globals[^1] == null) Globals.RemoveAt(Globals.Count - 1);
            while (Elems.Count > 0 && Elems[^1] == null) Elems.RemoveAt(Elems.Count - 1);
            while (Datas.Count > 0 && Datas[^1] == null) Datas.RemoveAt(Datas.Count - 1);
            
            while (MemsCount > 0 && Mems[MemsCount-1] == null) MemsCount -= 1;
            
            CurrentTransaction = null;
        }

        public FuncAddr AllocateWasmFunction(Module.Function func, ModuleInstance moduleInst)
        {
            var funcInst = new FunctionInstance(moduleInst, func);
            var funcAddr = AddFunction(funcInst);
            return funcAddr;
        }

        public FuncAddr AllocateHostFunction((string module, string entity) id, FunctionType funcType, Type delType, Delegate hostFunc, bool isAsync)
        {
            var funcInst = new HostFunction(id, funcType, delType, hostFunc, isAsync);
            var funcAddr = AddFunction(funcInst);
            return funcAddr;
        }

        public FuncAddr AddFunction(IFunctionInstance func)
        {
            var addr = new FuncAddr(Funcs.Count);
            if (CurrentTransaction == null)
                throw new InvalidOperationException("Cannot add to Store without a transaction.");
            Funcs.Add(null!);
            CurrentTransaction.Funcs.Add(addr, func);
            
            return addr;
        }

        public TableAddr AddTable(TableInstance table)
        {
            var addr = new TableAddr(Tables.Count);
            if (CurrentTransaction == null)
                throw new InvalidOperationException("Cannot add to Store without a transaction.");
            Tables.Add(null!);
            CurrentTransaction.Tables.Add(addr, table);

            return addr;
        }

        public TableInstance GetMutableTable(TableAddr addr)
        {
            if (!Contains(addr))
                throw new InvalidOperationException("Table does not exist in Store.");
            
            if (CurrentTransaction == null)
                return Tables[addr.Value];
            
            if (!CurrentTransaction.Tables.ContainsKey(addr))
            {
                CurrentTransaction.Tables.Add(addr, this[addr].Clone());
            }

            return CurrentTransaction.Tables[addr];
        }


        public MemAddr AddMemory(MemoryInstance mem)
        {
            var addr = new MemAddr(MemsCount);
            if (CurrentTransaction == null)
                throw new InvalidOperationException("Cannot add to Store without a transaction.");
            MemsCount += 1;
            if (Mems.Length < MemsCount)
            {
                Array.Resize(ref Mems, Mems.Length + 32);
            }
            CurrentTransaction.Mems.Add(addr, mem);
            return addr;
        }

        public GlobalAddr AddGlobal(GlobalInstance global)
        {
            var addr = new GlobalAddr(Globals.Count);
            if (CurrentTransaction == null)
                throw new InvalidOperationException("Cannot add to Store without a transaction.");
            Globals.Add(null!);
            CurrentTransaction.Globals.Add(addr, global);
            
            return addr;
        }

        public ElemAddr AddElement(ElementInstance elem)
        {
            var addr = new ElemAddr(Elems.Count);
            if (CurrentTransaction == null)
                throw new InvalidOperationException("Cannot add to Store without a transaction.");
            Elems.Add(null!);
            CurrentTransaction.Elems.Add(addr, elem);
            return addr;
        }

        public DataAddr AddData(DataInstance data)
        {
            var addr = new DataAddr(Datas.Count);
            if (CurrentTransaction == null)
                throw new InvalidOperationException("Cannot add to Store without a transaction.");
            Datas.Add(null!);
            CurrentTransaction.Datas.Add(addr, data);
            return addr;
        }

        public void DropData(DataAddr addr)
        {
            // if (CurrentTransaction == null)
            //     throw new InvalidOperationException("Cannot remove from Store without a transaction.");

            if (CurrentTransaction != null)
            {
                CurrentTransaction.Datas[addr] = DataInstance.Empty;    
            }
            else
            {
                Datas[addr.Value] = DataInstance.Empty;
            }
        }

        public void DropElement(ElemAddr addr)
        {
            if (CurrentTransaction != null)
            {
                CurrentTransaction.Elems[addr] = ElementInstance.Empty;    
            }
            else
            {
                Elems[addr.Value] = ElementInstance.Empty;
            }
        }
    }
}