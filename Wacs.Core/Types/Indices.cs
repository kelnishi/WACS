using System;

namespace Wacs.Core.Types
{
    // @Spec 2.5.1. Indices
    
    public readonly struct TypeIdx : IEquatable<Index>, IIndex 
    {
        public uint Value { get; }
        private TypeIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(TypeIdx functionIndex) => new Index((int)functionIndex.Value);
        public static explicit operator TypeIdx(int value) => new TypeIdx((uint)value);
        public static explicit operator TypeIdx(uint value) => new TypeIdx(value);
        
    }

    public readonly struct FuncIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private FuncIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(FuncIdx functionIndex) => new Index((int)functionIndex.Value);
        public static explicit operator FuncIdx(int value) => new FuncIdx((uint)value);
        public static explicit operator FuncIdx(uint value) => new FuncIdx(value);

        public static readonly FuncIdx Default = new FuncIdx(UInt32.MaxValue);
        
        public static bool operator ==(FuncIdx left, FuncIdx right) => left.Value.Equals(right.Value);
        public static bool operator !=(FuncIdx left, FuncIdx right) => !left.Value.Equals(right.Value);
        public override bool Equals(object obj) => obj is FuncIdx other && this == other;
        public override int GetHashCode() => Value.GetHashCode();
        
    }

    public readonly struct TableIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private TableIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(TableIdx tableIdx) => new Index((int)tableIdx.Value);
        public static explicit operator TableIdx(int value) => new TableIdx((uint)value);
        public static explicit operator TableIdx(uint value) => new TableIdx(value);
    }

    public readonly struct MemIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private MemIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(MemIdx memIdx) => new Index((int)memIdx.Value);
        
        public static explicit operator MemIdx(byte value) => new MemIdx(value);
        public static explicit operator MemIdx(int value) => new MemIdx((uint)value);
        public static explicit operator MemIdx(uint value) => new MemIdx(value);

        public static bool operator ==(MemIdx left, MemIdx right) => left.Value == right.Value;
        public static bool operator !=(MemIdx left, MemIdx right) => left.Value != right.Value;
        public static bool operator ==(MemIdx left, byte right) => left.Value == right;
        public static bool operator !=(MemIdx left, byte right) => left.Value != right;
        public override bool Equals(object? obj) => obj is MemIdx other && this == other;
        public override int GetHashCode() => Value.GetHashCode();
        
    }

    public readonly struct GlobalIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private GlobalIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(GlobalIdx globalIdx) => new Index((int)globalIdx.Value);
        public static explicit operator GlobalIdx(int value) => new GlobalIdx((uint)value);
        public static explicit operator GlobalIdx(uint value) => new GlobalIdx(value);
    }

    public readonly struct ElemIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private ElemIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(ElemIdx elemIdx) => new Index((int)elemIdx.Value);
        public static explicit operator ElemIdx(int value) => new ElemIdx((uint)value);
        public static explicit operator ElemIdx(uint value) => new ElemIdx(value);
    }

    public readonly struct DataIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private DataIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(DataIdx dataIdx) => new Index((int)dataIdx.Value);
        public static explicit operator DataIdx(int value) => new DataIdx((uint)value);
        public static explicit operator DataIdx(uint value) => new DataIdx(value);
    }

    public readonly struct LocalIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private LocalIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(LocalIdx localIdx) => new Index((int)localIdx.Value);
        public static explicit operator LocalIdx(int value) => new LocalIdx((uint)value);
        public static explicit operator LocalIdx(uint value) => new LocalIdx(value);
    }

    public readonly struct LabelIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private LabelIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(LabelIdx labelIdx) => new Index((int)labelIdx.Value);
        public static explicit operator LabelIdx(int value) => new LabelIdx((uint)value);
        public static explicit operator LabelIdx(uint value) => new LabelIdx(value);
    }
}