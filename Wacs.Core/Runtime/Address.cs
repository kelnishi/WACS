using System;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// @Spec 4.2.4. Addresses
    /// </summary>
    public interface IAddress
    {
        uint Value { get; }
    }
    public readonly struct FuncAddr : IAddress, IEquatable<FuncAddr>
    {
        public static readonly FuncAddr Null = new(-1);
        public FuncAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(FuncAddr addr) => new((int)addr.Value);

        public static explicit operator FuncAddr(Value value)
        {
            if (value.Type != ValType.Funcref)
                throw new ArgumentException("Cannot convert non-funcref Value to FuncAddr");
            return new FuncAddr(value.Int32);
        }
        
        public bool Equals(FuncAddr other) => Value == other.Value;
        public static bool operator ==(FuncAddr left, FuncAddr right) => left.Equals(right);
        public static bool operator !=(FuncAddr left, FuncAddr right) => !left.Equals(right);
        
        //Handles comparison vs null
        public static bool operator ==(FuncAddr? left, FuncAddr right) => right.Equals(left);
        public static bool operator !=(FuncAddr? left, FuncAddr right) => !right.Equals(left);
        public static bool operator ==(FuncAddr left, FuncAddr? right) => left.Equals(right);
        public static bool operator !=(FuncAddr left, FuncAddr? right) => !left.Equals(right);
        
        public override bool Equals(object? obj) => 
            obj == null ? Equals(Null) : obj is FuncAddr addr && Equals(addr);

        public override int GetHashCode() => Value.GetHashCode();

    }

    public readonly struct TableAddr : IAddress
    {
        public static readonly TableAddr Null = new(-1);
        public TableAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(TableAddr addr) => new((int)addr.Value);
    }

    public readonly struct MemAddr : IAddress
    {
        public static readonly MemAddr Null = new(-1);
        public MemAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(MemAddr addr) => new((int)addr.Value);
        public static explicit operator MemAddr(int addr) => new MemAddr(addr);
    }

    public readonly struct GlobalAddr : IAddress
    {
        public static readonly GlobalAddr Null = new(-1);
        public GlobalAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(GlobalAddr addr) => new((int)addr.Value);
    }

    public readonly struct ElemAddr : IAddress
    {
        public static readonly ElemAddr Null = new(-1);
        public ElemAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(ElemAddr addr) => new((int)addr.Value);
    }

    public readonly struct DataAddr : IAddress
    {
        public static readonly DataAddr Null = new(-1);
        public DataAddr(int value) => Value = (uint)value;
        public uint Value { get; }
        public static implicit operator Index(DataAddr addr) => new((int)addr.Value);
    }
    
}