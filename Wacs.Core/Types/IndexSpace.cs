using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Wacs.Core.Types
{
    public interface IIndex
    {
        int Value { get; }
    }

    public abstract class IndexSpace<TIndex, TType> where TIndex : IIndex
    {
        public abstract bool Contains(TIndex idx);

        public abstract TType this[TIndex idx] { get; }
    }

    public class TypesSpace : IndexSpace<TypeIdx, FunctionType>
    {
        private readonly ReadOnlyCollection<FunctionType> _moduleTypes;
        
        public override bool Contains(TypeIdx idx) => 
            idx.Value >= 0 && idx.Value < _moduleTypes.Count;

        public TypesSpace(Module module) =>
            _moduleTypes = module.Types.AsReadOnly();

        public override FunctionType this[TypeIdx idx] =>
            _moduleTypes[(Index)idx];
    }

    public class FunctionsSpace : IndexSpace<FuncIdx, Module.Function>
    {
        private readonly ReadOnlyCollection<Module.Function> _imports;
        private readonly ReadOnlyCollection<Module.Function> _funcs;

        public override bool Contains(FuncIdx idx) =>
            idx.Value >= 0 && idx.Value < (_imports.Count + _funcs.Count);

        public FunctionsSpace(Module module) =>
            (_imports, _funcs) = (module.ImportedFunctions, module.Funcs.AsReadOnly());

        public override Module.Function this[FuncIdx idx] => 
            idx.Value < _imports.Count ? _imports[(Index)idx] : _funcs[idx.Value - _imports.Count];
        
    }

    public class TablesSpace : IndexSpace<TableIdx, TableType>
    {
        private readonly ReadOnlyCollection<TableType> _imports;
        private readonly ReadOnlyCollection<TableType> _tableTypes;

        public override bool Contains(TableIdx idx) =>
            idx.Value >= 0 && idx.Value < (_imports.Count + _tableTypes.Count);

        public TablesSpace(Module module) =>
            (_imports, _tableTypes) = (module.ImportedTables, module.Tables.AsReadOnly());

        public override TableType this[TableIdx idx] => 
            idx.Value < _imports.Count ? _imports[(Index)idx] : _tableTypes[idx.Value - _imports.Count];
    }
    
    public class MemSpace : IndexSpace<MemIdx, MemoryType>
    {
        private readonly ReadOnlyCollection<MemoryType> _imports;
        private readonly ReadOnlyCollection<MemoryType> _memoryTypes;

        public override bool Contains(MemIdx idx) =>
            idx.Value >= 0 && idx.Value < (_imports.Count + _memoryTypes.Count);

        public MemSpace(Module module) => 
            (_imports, _memoryTypes) = (module.ImportedMems, module.Memories.AsReadOnly());

        public override MemoryType this[MemIdx idx] =>
            idx.Value < _imports.Count ? _imports[(Index)idx] : _memoryTypes[idx.Value - _imports.Count];
    }

    public class GlobalsSpace : IndexSpace<GlobalIdx, Module.Global>
    {
        private readonly ReadOnlyCollection<Module.Global> _imports;
        private readonly ReadOnlyCollection<Module.Global> _globals;

        public override bool Contains(GlobalIdx idx) =>
            idx.Value >= 0 && idx.Value < (_imports.Count + _globals.Count);

        public GlobalsSpace(Module module) =>
            (_imports, _globals) = (module.ImportedGlobals, module.Globals.AsReadOnly());

        public override Module.Global this[GlobalIdx idx] =>
            idx.Value < _imports.Count ? _imports[(Index)idx] : _globals[idx.Value - _imports.Count];
    }

}