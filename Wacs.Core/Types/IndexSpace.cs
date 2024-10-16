using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Wacs.Core.Runtime;

namespace Wacs.Core.Types
{
    public interface IIndex
    {
        int Value { get; }
    }

    public abstract class IndexSpace<TIndex, TType> where TIndex : IIndex
    {
        public abstract bool Contains(TIndex idx);

        public abstract TType this[TIndex idx] { get; set; }
    }

    public class TypesSpace : IndexSpace<TypeIdx, FunctionType>
    {
        private readonly ReadOnlyCollection<FunctionType> _moduleTypes;

        public override bool Contains(TypeIdx idx) =>
            idx.Value >= 0 && idx.Value < _moduleTypes.Count;

        public TypesSpace(Module module) =>
            _moduleTypes = module.Types.AsReadOnly();

        public override FunctionType this[TypeIdx idx]
        {
            get => _moduleTypes[(Index)idx];
            set => throw new NotImplementedException();
        }
    }

    public class FunctionsSpace : IndexSpace<FuncIdx, Module.Function>
    {
        private readonly ReadOnlyCollection<Module.Function> _imports;
        private readonly ReadOnlyCollection<Module.Function> _funcs;

        public override bool Contains(FuncIdx idx) =>
            idx.Value >= 0 && idx.Value < (_imports.Count + _funcs.Count);

        public FunctionsSpace(Module module) =>
            (_imports, _funcs) = (module.ImportedFunctions, module.Funcs.AsReadOnly());

        public override Module.Function this[FuncIdx idx]
        {
            get => idx.Value < _imports.Count ? _imports[(Index)idx] : _funcs[idx.Value - _imports.Count];
            set => throw new NotImplementedException();
        }
    }

    public class TablesSpace : IndexSpace<TableIdx, TableType>
    {
        private readonly ReadOnlyCollection<TableType> _imports;
        private readonly ReadOnlyCollection<TableType> _tableTypes;

        public override bool Contains(TableIdx idx) =>
            idx.Value >= 0 && idx.Value < (_imports.Count + _tableTypes.Count);

        public TablesSpace(Module module) =>
            (_imports, _tableTypes) = (module.ImportedTables, module.Tables.AsReadOnly());

        public override TableType this[TableIdx idx]
        {
            get => idx.Value < _imports.Count ? _imports[(Index)idx] : _tableTypes[idx.Value - _imports.Count];
            set => throw new NotImplementedException();
        }
    }

    public class MemSpace : IndexSpace<MemIdx, MemoryType>
    {
        private readonly ReadOnlyCollection<MemoryType> _imports;
        private readonly ReadOnlyCollection<MemoryType> _memoryTypes;

        public override bool Contains(MemIdx idx) =>
            idx.Value >= 0 && idx.Value < (_imports.Count + _memoryTypes.Count);

        public MemSpace(Module module) =>
            (_imports, _memoryTypes) = (module.ImportedMems, module.Memories.AsReadOnly());

        public override MemoryType this[MemIdx idx]
        {
            get => idx.Value < _imports.Count ? _imports[(Index)idx] : _memoryTypes[idx.Value - _imports.Count];
            set => throw new NotImplementedException();
        }
    }

    public class GlobalsSpace : IndexSpace<GlobalIdx, Value>
    {
        private readonly ReadOnlyCollection<Module.Global> _imports;
        private readonly ReadOnlyCollection<Module.Global> _globals;
        private List<Value> _data = new List<Value>();

        public override bool Contains(GlobalIdx idx) =>
            idx.Value >= 0 && idx.Value < (_imports.Count + _globals.Count);

        public GlobalsSpace(Module module)
        {
            _imports = module.ImportedGlobals;
            _globals = module.Globals.AsReadOnly();

            foreach (var import in _imports)
            {
                _data.Add(new Value(import.Type.ContentType));
            }

            foreach (var global in _globals)
            {
                _data.Add(new Value(global.Type.ContentType));
            }
        }

        public override Value this[GlobalIdx idx]
        {
            get => _data[(Index)idx];
            set
            {
                var type = idx.Value < _imports.Count
                    ? _imports[(Index)idx].Type
                    : _globals[idx.Value - _imports.Count].Type;

                if (type.Mutability == Mutability.Immutable)
                    throw new InvalidOperationException($"Cannot set immutable Global");

                _data[(Index)idx] = value;
            }
        }
    }

    public class LocalsSpace : IndexSpace<LocalIdx, Value>
    {
        private readonly ReadOnlyCollection<ValType> _locals;
        private readonly List<Value> _data;

        public override bool Contains(LocalIdx idx) =>
            idx.Value >= 0 && idx.Value < _locals.Count;

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
    }
}