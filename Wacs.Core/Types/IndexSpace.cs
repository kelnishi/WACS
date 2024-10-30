using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
                BlockType.Empty => new FunctionType(ResultType.Empty, ResultType.Empty),
                BlockType.I32 => new FunctionType(ResultType.Empty, new ResultType(ValType.I32)),
                BlockType.I64 => new FunctionType(ResultType.Empty, new ResultType(ValType.I64)),
                BlockType.F32 => new FunctionType(ResultType.Empty, new ResultType(ValType.F32)),
                BlockType.F64 => new FunctionType(ResultType.Empty, new ResultType(ValType.F64)),
                BlockType.V128 => new FunctionType(ResultType.Empty, new ResultType(ValType.V128)),
                BlockType.Funcref => new FunctionType(ResultType.Empty, new ResultType(ValType.Funcref)),
                BlockType.Externref => new FunctionType(ResultType.Empty, new ResultType(ValType.Externref)),
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

        public override bool Contains(TableIdx idx) =>
            idx.Value < _imports.Count + _tableTypes.Count;
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

    public class LocalsSpace : AbstractIndexSpace<LocalIdx, Value>
    {
        private readonly List<Value> _data;
        private readonly ReadOnlyCollection<ValType> _locals;

        public LocalsSpace()
        {
            _locals = Array.Empty<ValType>().ToList().AsReadOnly();
            _data = Array.Empty<Value>().ToList();
        }

        public LocalsSpace(params IEnumerable<ValType>[] types)
        {
            _locals = types.SelectMany(collection => collection).ToList().AsReadOnly();
            _data = _locals.Select(t => new Value(t)).ToList();
        }

        public override Value this[LocalIdx idx]
        {
            get => _data[(Index)idx];
            set => _data[(Index)idx] = value;
        }

        public override bool Contains(LocalIdx idx) =>
            idx.Value < _locals.Count;
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