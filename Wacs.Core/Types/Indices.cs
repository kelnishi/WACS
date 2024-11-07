using System;

namespace Wacs.Core.Types
{
    // @Spec 2.5.1. Indices
    
    public readonly struct TypeIdx : IEquatable<Index>, IIndex 
    {
        public uint Value { get; }
        private TypeIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(TypeIdx functionIndex) => new((int)functionIndex.Value);
        public static explicit operator TypeIdx(int value) => new((uint)value);
        public static explicit operator TypeIdx(uint value) => new(value);
        
    }

    public readonly struct FuncIdx : IEquatable<Index>, IIndex
    {
        
        public uint Value { get; }
        private FuncIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(FuncIdx functionIndex) => new((int)functionIndex.Value);
        public static explicit operator FuncIdx(int value) => new((uint)value);
        public static explicit operator FuncIdx(uint value) => new(value);
        public static bool operator ==(FuncIdx left, FuncIdx right) => left.Value.Equals(right.Value);
        public static bool operator !=(FuncIdx left, FuncIdx right) => !left.Value.Equals(right.Value);
        public override bool Equals(object? obj) => obj is FuncIdx other && this == other;
        public override int GetHashCode() => Value.GetHashCode();
        
        
        public static readonly FuncIdx Default = new(uint.MaxValue);
        public static readonly FuncIdx GlobalInitializers = new(uint.MaxValue - 1);
        public static readonly FuncIdx ElementInitializers = new(uint.MaxValue - 2);
        public static readonly FuncIdx ElementInitialization = new(uint.MaxValue - 3);
        public static readonly FuncIdx ExpressionEvaluation = new(uint.MaxValue - 4);

        public override string ToString() => Value switch
        {
            uint.MaxValue => "Default",
            uint.MaxValue - 1 => "GlobalInitializers",
            uint.MaxValue - 2 => "ElementInitializers",
            uint.MaxValue - 3 => "ElementInitialization",
            uint.MaxValue - 4 => "ExpressionEvaluation",
            _ => Value.ToString()
        };
    }

    public readonly struct TableIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private TableIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(TableIdx tableIdx) => new((int)tableIdx.Value);
        public static explicit operator TableIdx(int value) => new((uint)value);
        public static explicit operator TableIdx(uint value) => new(value);
    }

    public readonly struct MemIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private MemIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(MemIdx memIdx) => new((int)memIdx.Value);
        
        public static explicit operator MemIdx(byte value) => new(value);
        public static explicit operator MemIdx(int value) => new((uint)value);
        public static explicit operator MemIdx(uint value) => new(value);

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
        public static explicit operator Index(GlobalIdx globalIdx) => new((int)globalIdx.Value);
        public static explicit operator GlobalIdx(int value) => new((uint)value);
        public static explicit operator GlobalIdx(uint value) => new(value);
    }

    public readonly struct ElemIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private ElemIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(ElemIdx elemIdx) => new((int)elemIdx.Value);
        public static explicit operator ElemIdx(int value) => new((uint)value);
        public static explicit operator ElemIdx(uint value) => new(value);
    }

    public readonly struct DataIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private DataIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(DataIdx dataIdx) => new((int)dataIdx.Value);
        public static explicit operator DataIdx(int value) => new((uint)value);
        public static explicit operator DataIdx(uint value) => new(value);
    }

    public readonly struct LocalIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private LocalIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(LocalIdx localIdx) => new((int)localIdx.Value);
        public static explicit operator LocalIdx(int value) => new((uint)value);
        public static explicit operator LocalIdx(uint value) => new(value);
    }

    public readonly struct LabelIdx : IEquatable<Index>, IIndex
    {
        public uint Value { get; }
        private LabelIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(LabelIdx labelIdx) => new((int)labelIdx.Value);
        public static explicit operator LabelIdx(int value) => new((uint)value);
        public static explicit operator LabelIdx(uint value) => new(value);
    }
}