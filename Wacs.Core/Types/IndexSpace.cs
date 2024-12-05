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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;

namespace Wacs.Core.Types
{
    public class FuncAddrs : IEnumerable<FuncAddr>
    {
        private readonly List<FuncAddr> _space = new();

        public FuncAddr this[FuncIdx idx]
        {
            get => _space[(int)idx.Value];
            set => _space[(int)idx.Value] = value;
        }

        public IEnumerator<FuncAddr> GetEnumerator() => _space.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Contains(FuncIdx idx) => idx.Value < _space.Count;

        public void Add(FuncAddr element) => _space.Add(element);
    }

    public class TableAddrs
    {
        private readonly List<TableAddr> _space = new();

        public TableAddr this[TableIdx idx]
        {
            get => _space[(int)idx.Value];
            set => _space[(int)idx.Value] = value;
        }

        public bool Contains(TableIdx idx) => idx.Value < _space.Count;

        public void Add(TableAddr element) => _space.Add(element);
    }

    public class MemAddrs
    {
        private List<MemAddr>? _build = new();
        private bool _final;
        private MemAddr[]? _space;

        public MemAddr this[MemIdx idx] => _space![(int)idx.Value];

        public bool Contains(MemIdx idx) => idx.Value < _space!.Length;

        public void Add(MemAddr element)
        {
            if (_final)
                throw new WasmRuntimeException("Cannot add addresses after module instance has been finalized.");
                    
            _build!.Add(element);
        }

        public void Finalize()
        {
            if (_final)
                throw new WasmRuntimeException("MemAddrs space already finalized.");
            
            _final = true;
            _space = _build!.ToArray();
            _build = null;
        }
    }
    
    public class GlobalAddrs
    {
        private readonly List<GlobalAddr> _space = new();

        public GlobalAddr this[GlobalIdx idx]
        {
            get => _space[(int)idx.Value];
            set => _space[(int)idx.Value] = value;
        }

        public bool Contains(GlobalIdx idx) => idx.Value < _space.Count;

        public void Add(GlobalAddr element) => _space.Add(element);
    }

    public class ElemAddrs
    {
        private readonly List<ElemAddr> _space = new();

        public ElemAddr this[ElemIdx idx]
        {
            get => _space[(int)idx.Value];
            set => _space[(int)idx.Value] = value;
        }

        public bool Contains(ElemIdx idx) => idx.Value < _space.Count;

        public void Add(ElemAddr element) => _space.Add(element);
    }

    public class DataAddrs
    {
        private readonly List<DataAddr> _space = new();

        public DataAddr this[DataIdx idx]
        {
            get => _space[(int)idx.Value];
            set => _space[(int)idx.Value] = value;
        }

        public bool Contains(DataIdx idx) => idx.Value < _space.Count;

        public void Add(DataAddr element) => _space.Add(element);
    }
    
    public abstract class AbstractIndexSpace<TIndex, TType>
    {
        protected const string InvalidSetterMessage = "There's no crying in Baseball!";

        public abstract TType this[TIndex idx] { get; set; }
        public abstract bool Contains(TIndex idx);
    }

    public class TypesSpace : AbstractIndexSpace<TypeIdx, FunctionType>
    {
        private readonly ReadOnlyCollection<FunctionType> _moduleTypes;

        public TypesSpace(Module module) =>
            _moduleTypes = module.Types.AsReadOnly();

        public override FunctionType this[TypeIdx idx]
        {
            get => _moduleTypes[(Index)idx];
            set => throw new InvalidOperationException(InvalidSetterMessage);
        }

        public override bool Contains(TypeIdx idx) =>
            idx.Value < _moduleTypes.Count;

        public FunctionType? ResolveBlockType(ValType blockType) =>
            blockType switch
            {
                ValType.Empty  => FunctionType.Empty,
                ValType.I32    => FunctionType.SingleI32,
                ValType.I64    => FunctionType.SingleI64,
                ValType.F32    => FunctionType.SingleF32,
                ValType.F64    => FunctionType.SingleF64,
                ValType.V128   => FunctionType.SingleV128,
                
                //TODO: Handle HeapTypes
                ValType.Func   => FunctionType.SingleFuncref,
                ValType.Extern => FunctionType.SingleExternref,
                _ when Contains((TypeIdx)(int)blockType) => this[(TypeIdx)(int)blockType],
                _ => null
            };
    }

    public class FunctionsSpace : AbstractIndexSpace<FuncIdx, Module.Function>
    {
        private readonly ReadOnlyCollection<Module.Function> _funcs;
        private readonly ReadOnlyCollection<Module.Function> _imports;

        public FunctionsSpace(Module module) =>
            (_imports, _funcs) = (module.ImportedFunctions, module.Funcs.AsReadOnly());

        public override Module.Function this[FuncIdx idx]
        {
            get => idx.Value < _imports.Count ? _imports[(Index)idx] : _funcs[(int)(idx.Value - _imports.Count)];
            set => throw new InvalidOperationException(InvalidSetterMessage);
        }

        public override bool Contains(FuncIdx idx) =>
            idx.Value < _imports.Count + _funcs.Count;
    }

    public class TablesSpace : AbstractIndexSpace<TableIdx, TableType>
    {
        private readonly ReadOnlyCollection<TableType> _imports;
        private readonly ReadOnlyCollection<TableType> _tableTypes;

        public TablesSpace(Module module) =>
            (_imports, _tableTypes) = (module.ImportedTables, module.Tables.AsReadOnly());

        public override TableType this[TableIdx idx]
        {
            get => idx.Value < _imports.Count ? _imports[(Index)idx] : _tableTypes[(int)(idx.Value - _imports.Count)];
            set => throw new InvalidOperationException(InvalidSetterMessage);
        }

        public int Count => _imports.Count + _tableTypes.Count;

        public override bool Contains(TableIdx idx) =>
            idx.Value < Count;
    }

    public class MemSpace
    {
        private const string InvalidSetterMessage = "There's no crying in Baseball!";
        private readonly ReadOnlyCollection<MemoryType> _imports;
        private readonly ReadOnlyCollection<MemoryType> _memoryTypes;

        public MemSpace(Module module) =>
            (_imports, _memoryTypes) = (module.ImportedMems, module.Memories.AsReadOnly());

        public MemoryType this[MemIdx idx]
        {
            get => idx.Value < _imports.Count ? _imports[(Index)idx] : _memoryTypes[(int)(idx.Value - _imports.Count)];
            set => throw new InvalidOperationException(InvalidSetterMessage);
        }

        public bool Contains(MemIdx idx) =>
            idx.Value < _imports.Count + _memoryTypes.Count;
    }

    public class GlobalValidationSpace : AbstractIndexSpace<GlobalIdx, Module.Global>
    {
        private readonly ReadOnlyCollection<Module.Global> _globals;
        private readonly ReadOnlyCollection<Module.Global> _imports;

        public GlobalValidationSpace(Module module)
        {
            _imports = module.ImportedGlobals;
            _globals = module.Globals.AsReadOnly();
        }

        public override Module.Global this[GlobalIdx idx]
        {
            get => idx.Value < _imports.Count ? _imports[(Index)idx] : _globals[(Index)(idx.Value - _imports.Count)];
            set => throw new InvalidOperationException(InvalidSetterMessage);
        }

        public override bool Contains(GlobalIdx idx) =>
            idx.Value < _imports.Count + _globals.Count;
    }

    public struct LocalsSpace
    {
        public Value[] Data;

        public int Capacity { get; }

        public Value Get(LocalIdx idx)
        {
            if (Data == null)
                throw new InvalidOperationException("LocalSpace was used uninitialized.");
                
            return Data[idx.Value];
        }

        public void Set(LocalIdx idx, Value value)
        {
            if (Data == null)
                throw new InvalidOperationException("LocalSpace was used uninitialized.");
                
            Data[idx.Value] = value;
        }

        public LocalsSpace(Value[] data, ValType[] parameters, ValType[] locals, bool skipInit = false)
        {
            Capacity = parameters.Length + locals.Length;
            Data = data;
            if (skipInit)
                return;
            
            int idx = 0;
            foreach (var t in parameters)
            {
                Data[idx++] = new Value(t);
            }
            foreach (var t in locals)
            {
                Data[idx++] = new Value(t);
            }
        }

        public bool Contains(LocalIdx idx) =>
            idx.Value < Capacity;
    }

    public class ElementsSpace : AbstractIndexSpace<ElemIdx, Module.ElementSegment>
    {
        private readonly List<Module.ElementSegment> _segments;

        public ElementsSpace(List<Module.ElementSegment> segments) =>
            _segments = segments;

        public override Module.ElementSegment this[ElemIdx idx]
        {
            get => _segments[(Index)idx];
            set => _segments[(Index)idx] = value;
        }

        public override bool Contains(ElemIdx idx) =>
            idx.Value < _segments.Count;
    }

    public class DataValidationSpace : AbstractIndexSpace<DataIdx, bool>
    {
        private readonly int _size;

        public DataValidationSpace(int size) =>
            _size = size;

        public override bool this[DataIdx idx]
        {
            get => Contains(idx);
            set => throw new InvalidOperationException(InvalidSetterMessage);
        }

        public override bool Contains(DataIdx idx) => idx.Value < _size;
    }
}