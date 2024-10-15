using System;

namespace Wacs.Core.Types
{
    // @Spec 2.5.1. Indices
    
    public readonly struct TypeIdx : IEquatable<Index>, IIndex 
    {
        public int Value { get; }
        private TypeIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(TypeIdx functionIndex) => new Index(functionIndex.Value);
        public static explicit operator TypeIdx(int value) => new TypeIdx(value);
        public static explicit operator TypeIdx(uint value) => new TypeIdx((int)value);
        
    }

    public readonly struct FuncIdx : IEquatable<Index>, IIndex
    {
        public int Value { get; }
        private FuncIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(FuncIdx functionIndex) => new Index(functionIndex.Value);
        public static explicit operator FuncIdx(int value) => new FuncIdx(value);
        public static explicit operator FuncIdx(uint value) => new FuncIdx((int)value);

        public static readonly FuncIdx Default = new FuncIdx(-1);
        
        public static bool operator ==(FuncIdx left, FuncIdx right) => left.Value.Equals(right.Value);
        public static bool operator !=(FuncIdx left, FuncIdx right) => !left.Value.Equals(right.Value);
        public override bool Equals(object obj) => obj is FuncIdx other && this == other;
        public override int GetHashCode() => Value.GetHashCode();
        
    }

    public readonly struct TableIdx : IEquatable<Index>, IIndex
    {
        public int Value { get; }
        private TableIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(TableIdx tableIdx) => new Index(tableIdx.Value);
        public static explicit operator TableIdx(int value) => new TableIdx(value);
        public static explicit operator TableIdx(uint value) => new TableIdx((int)value);
    }

    public readonly struct MemIdx : IEquatable<Index>, IIndex
    {
        public int Value { get; }
        private MemIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(MemIdx memIdx) => new Index(memIdx.Value);
        public static explicit operator MemIdx(int value) => new MemIdx(value);
        public static explicit operator MemIdx(uint value) => new MemIdx((int)value);
    }

    public readonly struct GlobalIdx : IEquatable<Index>, IIndex
    {
        public int Value { get; }
        private GlobalIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(GlobalIdx globalIdx) => new Index(globalIdx.Value);
        public static explicit operator GlobalIdx(int value) => new GlobalIdx(value);
        public static explicit operator GlobalIdx(uint value) => new GlobalIdx((int)value);
    }

    public readonly struct ElemIdx : IEquatable<Index>, IIndex
    {
        public int Value { get; }
        private ElemIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(ElemIdx elemIdx) => new Index(elemIdx.Value);
        public static explicit operator ElemIdx(int value) => new ElemIdx(value);
        public static explicit operator ElemIdx(uint value) => new ElemIdx((int)value);
    }

    public readonly struct DataIdx : IEquatable<Index>, IIndex
    {
        public int Value { get; }
        private DataIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(DataIdx dataIdx) => new Index(dataIdx.Value);
        public static explicit operator DataIdx(int value) => new DataIdx(value);
        public static explicit operator DataIdx(uint value) => new DataIdx((int)value);
    }

    public readonly struct LocalIdx : IEquatable<Index>, IIndex
    {
        public int Value { get; }
        private LocalIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(LocalIdx localIdx) => new Index(localIdx.Value);
        public static explicit operator LocalIdx(int value) => new LocalIdx(value);
        public static explicit operator LocalIdx(uint value) => new LocalIdx((int)value);
    }

    public readonly struct LabelIdx : IEquatable<Index>, IIndex
    {
        public int Value { get; }
        private LabelIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static implicit operator Index(LabelIdx labelIdx) => new Index(labelIdx.Value);
        public static explicit operator LabelIdx(int value) => new LabelIdx(value);
        public static explicit operator LabelIdx(uint value) => new LabelIdx((int)value);
    }
}