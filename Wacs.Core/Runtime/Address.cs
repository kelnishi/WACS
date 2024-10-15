using System;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// @Spec 4.2.4. Addresses
    /// </summary>
    public interface IAddress
    {
        uint Value { get; } 
    }
    public class FuncAddr : IAddress
    {
        public uint Value { get; }
        public FuncAddr(int value) => Value = (uint)value;
        public static implicit operator Index(FuncAddr addr) => new Index((int)addr.Value);
        
        public static readonly FuncAddr Null = new FuncAddr(-1);
    }

    public class TableAddr : IAddress
    {
        public uint Value { get; }
        public TableAddr(int value) => Value = (uint)value;
        public static implicit operator Index(TableAddr addr) => new Index((int)addr.Value);
        
        public static readonly TableAddr Null = new TableAddr(-1);
    }

    public class MemAddr : IAddress
    {
        public uint Value { get; }
        public MemAddr(int value) => Value = (uint)value;
        public static implicit operator Index(MemAddr addr) => new Index((int)addr.Value);
        
        public static readonly MemAddr Null = new MemAddr(-1);
    }

    public class GlobalAddr : IAddress
    {
        public uint Value { get; }
        public GlobalAddr(int value) => Value = (uint)value;
        public static implicit operator Index(GlobalAddr addr) => new Index((int)addr.Value);
        
        public static readonly GlobalAddr Null = new GlobalAddr(-1);
    }

    public class ElemAddr : IAddress
    {
        public uint Value { get; }
        public ElemAddr(int value) => Value = (uint)value;
        public static implicit operator Index(ElemAddr addr) => new Index((int)addr.Value);
        
        public static readonly ElemAddr Null = new ElemAddr(-1);
    }

    public class DataAddr : IAddress
    {
        public uint Value { get; }
        public DataAddr(int value) => Value = (uint)value;
        public static implicit operator Index(DataAddr addr) => new Index((int)addr.Value);
        
        public static readonly DataAddr Null = new DataAddr(-1);
    }
    
}