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
using System.Collections.ObjectModel;
using Wacs.Core.Runtime;

namespace Wacs.Core.Types
{
    public interface IIndex
    {
        uint Value { get; }
    }

    public class RuntimeIndexSpace<TIndex, TType>
        where TIndex : IIndex
        where TType : IAddress
    {
        private readonly List<TType> _space = new();

        public TType this[TIndex idx]
        {
            get => _space[(int)idx.Value];
            set => _space[(int)idx.Value] = value;
        }

        public bool Contains(TIndex idx) => idx.Value < _space.Count;

        public void Add(TType element) => _space.Add(element);
    }

    public class FuncAddrs : RuntimeIndexSpace<FuncIdx, FuncAddr>
    {}

    public class TableAddrs : RuntimeIndexSpace<TableIdx, TableAddr>
    {}

    public class MemAddrs : RuntimeIndexSpace<MemIdx, MemAddr>
    {}

    public class GlobalAddrs : RuntimeIndexSpace<GlobalIdx, GlobalAddr>
    {}

    public class ElemAddrs : RuntimeIndexSpace<ElemIdx, ElemAddr>
    {}

    public class DataAddrs : RuntimeIndexSpace<DataIdx, DataAddr>
    {}


    public abstract class AbstractIndexSpace<TIndex, TType> where TIndex : IIndex
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

        public FunctionType? ResolveBlockType(BlockType blockType) =>
            blockType switch
            {
                BlockType.Empty     => FunctionType.Empty,
                BlockType.I32       => FunctionType.SingleI32,
                BlockType.I64       => FunctionType.SingleI64,
                BlockType.F32       => FunctionType.SingleF32,
                BlockType.F64       => FunctionType.SingleF64,
                BlockType.V128      => FunctionType.SingleV128,
                BlockType.Funcref   => FunctionType.SingleFuncref,
                BlockType.Externref => FunctionType.SingleExternref,
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

    public class MemSpace : AbstractIndexSpace<MemIdx, MemoryType>
    {
        private readonly ReadOnlyCollection<MemoryType> _imports;
        private readonly ReadOnlyCollection<MemoryType> _memoryTypes;

        public MemSpace(Module module) =>
            (_imports, _memoryTypes) = (module.ImportedMems, module.Memories.AsReadOnly());

        public override MemoryType this[MemIdx idx]
        {
            get => idx.Value < _imports.Count ? _imports[(Index)idx] : _memoryTypes[(int)(idx.Value - _imports.Count)];
            set => throw new InvalidOperationException(InvalidSetterMessage);
        }

        public override bool Contains(MemIdx idx) =>
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
        public Value[]? Data;
        private int _capacity;

        public int Capacity => _capacity;
        
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

        public LocalsSpace(Value[] data, ValType[] parameters, ValType[] locals)
        {
            _capacity = parameters.Length + locals.Length;
            Data = data;
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
            idx.Value < _capacity;
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