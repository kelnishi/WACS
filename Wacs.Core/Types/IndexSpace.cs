// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Types.Defs;

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

    public class TagAddrs
    {
        private readonly List<TagAddr> _space = new();

        public TagAddr this[TagIdx idx]
        {
            get => _space[(int)idx.Value];
            set => _space[(int)idx.Value] = value;
        }

        public bool Contains(TagIdx idx) => idx.Value < _space.Count;

        public void Add(TagAddr element) => _space.Add(element);

        public IEnumerator<TagAddr> GetEnumerator() => _space.GetEnumerator();
    }

    public class ExnAddrs
    {
        private readonly List<ExnAddr> _space = new();

        public ExnAddr this[ExnIdx idx]
        {
            get => _space[(int)idx.Value];
            set => _space[(int)idx.Value] = value;
        }

        public bool Contains(ExnIdx idx) => idx.Value < _space.Count;

        public void Add(ExnAddr element) => _space.Add(element);
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

    public class TypesSpace : AbstractIndexSpace<TypeIdx, DefType>
    {
        private readonly ReadOnlyCollection<DefType> _moduleTypes;

        public TypesSpace(Module module) =>
            _moduleTypes = module.UnrollTypes().AsReadOnly();

        public override DefType this[TypeIdx idx]
        {
            get => _moduleTypes[(Index)idx];
            set => throw new InvalidOperationException(InvalidSetterMessage);
        }

        public override bool Contains(TypeIdx idx) =>
            idx.Value >= 0 && idx.Value < _moduleTypes.Count;

        public FunctionType? ResolveBlockType(ValType blockType)
        {
            switch (blockType)
            {
                case ValType.Empty: return FunctionType.Empty;
                case ValType.I32: return FunctionType.SingleI32;
                case ValType.I64: return FunctionType.SingleI64;
                case ValType.F32: return FunctionType.SingleF32;
                case ValType.F64: return FunctionType.SingleF64;
                case ValType.V128: return FunctionType.SingleV128;
                //TODO: Make static versions, reduce allocation
                case var type when type.IsRefType(): return new(ResultType.Empty, new ResultType(type));
                case var type when type.IsDefType(): return this[type.Index()].Expansion as FunctionType;
                default: return null;
            }
        }
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
        public int IncrementalHighWatermark = -1;

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

        public void SetHighWatermark(Module.Global target)
        {
            IncrementalHighWatermark = _imports.Concat(_globals).ToList().FindIndex(p => p == target);
        }

        public void SetHighImportWatermark()
        {
            IncrementalHighWatermark = _imports.Count - 1;
        }

        public override bool Contains(GlobalIdx idx) => 
            idx.Value < _imports.Count + _globals.Count && idx.Value <= IncrementalHighWatermark;
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

    public class TagsSpace : AbstractIndexSpace<TagIdx, TagType>
    {
        private readonly ReadOnlyCollection<TagType> _imports;
        private readonly ReadOnlyCollection<TagType> _tags;

        public TagsSpace(Module module)
        {
            _imports = module.ImportedTags;
            _tags = module.Tags.AsReadOnly();
        }

        public override TagType this[TagIdx idx]
        {
            get => idx.Value < _imports.Count ? _imports[(Index)idx] : _tags[(Index)(idx.Value - _imports.Count)];
            set => throw new InvalidOperationException(InvalidSetterMessage);
        }

        public override bool Contains(TagIdx idx)
        {
            return idx.Value < _imports.Count + _tags.Count;
        }
    }
}