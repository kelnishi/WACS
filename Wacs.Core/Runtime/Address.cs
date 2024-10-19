using System;

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
        public static readonly FuncAddr Null = new(-1);
        public FuncAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(FuncAddr addr) => new((int)addr.Value);
    }

    public class TableAddr : IAddress
    {
        public static readonly TableAddr Null = new(-1);
        public TableAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(TableAddr addr) => new((int)addr.Value);
    }

    public class MemAddr : IAddress
    {
        public static readonly MemAddr Null = new(-1);
        public MemAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(MemAddr addr) => new((int)addr.Value);
    }

    public class GlobalAddr : IAddress
    {
        public static readonly GlobalAddr Null = new(-1);
        public GlobalAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(GlobalAddr addr) => new((int)addr.Value);
    }

    public class ElemAddr : IAddress
    {
        public static readonly ElemAddr Null = new(-1);
        public ElemAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(ElemAddr addr) => new((int)addr.Value);
    }

    public class DataAddr : IAddress
    {
        public static readonly DataAddr Null = new(-1);
        public DataAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(DataAddr addr) => new((int)addr.Value);
    }
    
}