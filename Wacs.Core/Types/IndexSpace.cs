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

        public TypesSpace(List<FunctionType> moduleTypes) =>
            _moduleTypes = moduleTypes.AsReadOnly();

        public override FunctionType this[TypeIdx idx] => _moduleTypes[(Index)idx];
    }
}