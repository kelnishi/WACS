// Copyright 2024 Kelvin Nishikawa
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Types
{
    // @Spec 2.5.1. Indices
    
    public readonly struct TypeIdx : IEquatable<Index>
    {
        public static readonly TypeIdx Default = new(int.MinValue);
        public readonly int Value;
        private TypeIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(TypeIdx functionIndex) => new(functionIndex.Value);
        public static explicit operator TypeIdx(int value) => new(value);
        public static explicit operator TypeIdx(uint value) => new((int)value);
        public static explicit operator TypeIdx(ValType type) => type.Index();

        public static explicit operator ValType(TypeIdx type) => (ValType)type.Value;
        
        public static bool operator ==(TypeIdx left, TypeIdx right) => left.Value == right.Value;
        public static bool operator !=(TypeIdx left, TypeIdx right) => !(left == right);
        
        //For matching heaptypes
        public bool Matches(TypeIdx def2, TypesSpace? types)
        {
            if (types is null)
                throw new WasmRuntimeException("Cannot match types without a context");
            var defType1 = types[this];
            var defType2 = types[def2];
            return defType1.Matches(defType2, types);
        }
        
        public override string ToString() => $"TypeIdx[{Value & ~(int)ValType.IndexMask}]";
    }

    public readonly struct FuncIdx : IEquatable<Index>
    {

        public readonly uint Value;
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

    public readonly struct TableIdx : IEquatable<Index>
    {
        public readonly uint Value;
        private TableIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(TableIdx tableIdx) => new((int)tableIdx.Value);
        public static explicit operator TableIdx(int value) => new((uint)value);
        public static explicit operator TableIdx(uint value) => new(value);
    }

    public readonly struct MemIdx : IEquatable<Index>
    {
        public readonly uint Value;
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

        public override string ToString() => $"MemIdx[{Value}]";

    }

    public readonly struct GlobalIdx : IEquatable<Index>
    {
        public readonly uint Value;
        private GlobalIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(GlobalIdx globalIdx) => new((int)globalIdx.Value);
        public static explicit operator GlobalIdx(int value) => new((uint)value);
        public static explicit operator GlobalIdx(uint value) => new(value);
    }
    
    public readonly struct TagIdx : IEquatable<Index>
    {
        public readonly uint Value;
        private TagIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(TagIdx tagIdx) => new((int)tagIdx.Value);
        public static explicit operator TagIdx(int value) => new((uint)value);
        public static explicit operator TagIdx(uint value) => new(value);
    }

    public readonly struct ExnIdx : IEquatable<Index>, RefIdx
    {
        public readonly uint Value;
        public ExnIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(ExnIdx exnIdx) => new((int)exnIdx.Value);
        public static explicit operator ExnIdx(int value) => new((uint)value);
        public static explicit operator ExnIdx(uint value) => new(value);
    }

    public readonly struct ElemIdx : IEquatable<Index>
    {
        public readonly uint Value;
        private ElemIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(ElemIdx elemIdx) => new((int)elemIdx.Value);
        public static explicit operator ElemIdx(int value) => new((uint)value);
        public static explicit operator ElemIdx(uint value) => new(value);
    }

    public readonly struct DataIdx : IEquatable<Index>
    {
        public readonly uint Value;
        private DataIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(DataIdx dataIdx) => new((int)dataIdx.Value);
        public static explicit operator DataIdx(int value) => new((uint)value);
        public static explicit operator DataIdx(uint value) => new(value);
    }

    public readonly struct LabelIdx : IEquatable<Index>
    {
        public readonly uint Value;
        private LabelIdx(uint value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(LabelIdx labelIdx) => new((int)labelIdx.Value);
        public static explicit operator LabelIdx(int value) => new((uint)value);
        public static explicit operator LabelIdx(uint value) => new(value);
        
        public static LabelIdx operator +(LabelIdx left, int right) => new((uint)(left.Value + right));
        public static LabelIdx operator -(LabelIdx left, int right) => new((uint)(left.Value - right));
    }
    
    public readonly struct FieldIdx : IEquatable<Index>
    {
        public readonly int Value;
        private FieldIdx(int value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator Index(FieldIdx fieldIdx) => new(fieldIdx.Value);
        public static explicit operator FieldIdx(int value) => new(value);
        public static explicit operator FieldIdx(uint value) => new((int)value);
    }

    public interface RefIdx {}

    public readonly struct PtrIdx : RefIdx
    {
        public readonly long Value;
        public PtrIdx(long value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator PtrIdx(long value) => new(value);
    }
    
    public readonly struct VecIdx : RefIdx
    {
        public readonly long Value;
        private VecIdx(long value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator VecIdx(long value) => new(value);
    }
    
    public readonly struct StructIdx : RefIdx
    {
        public readonly long Value;
        private StructIdx(long value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator StructIdx(long value) => new(value);
    }
    
    public readonly struct ArrayIdx : RefIdx
    {
        public readonly long Value;
        private ArrayIdx(long value) => Value = value;
        public bool Equals(Index other) => Value == other.Value;
        public static explicit operator ArrayIdx(long value) => new(value);
    }
    
}