// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// @Spec 4.2.4. Addresses
    /// </summary>
    public interface IAddress {}
    
    public readonly struct FuncAddr : IAddress, IEquatable<FuncAddr>
    {
        public static readonly FuncAddr Null = new(-1);
        public FuncAddr(int value) => Value = value;
        public readonly int Value;
        public static implicit operator Index(FuncAddr addr) => new(addr.Value);

        public static explicit operator FuncAddr(Value value)
        {
            if (value.Type != ValType.FuncRef)
                throw new ArgumentException($"Cannot convert non-funcref ({value.Type}) Value to FuncAddr");
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

    public readonly struct TableAddr : IAddress, IEquatable<TableAddr>
    {
        public static readonly TableAddr Null = new(-1);
        public TableAddr(int value) => Value = value;
        public readonly int Value;
        public static implicit operator Index(TableAddr addr) => new(addr.Value);

        public bool Equals(TableAddr other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is TableAddr other && Equals(other);

        public override int GetHashCode() => Value;
    }

    public readonly struct MemAddr : IAddress, IEquatable<MemAddr>
    {
        public static readonly MemAddr Null = new(-1);
        public MemAddr(int value) => Value = value;
        public readonly int Value;
        public static implicit operator Index(MemAddr addr) => new(addr.Value);
        public static explicit operator MemAddr(int addr) => new(addr);

        public bool Equals(MemAddr other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is MemAddr other && Equals(other);

        public override int GetHashCode() => Value;
    }

    public readonly struct GlobalAddr : IAddress, IEquatable<GlobalAddr>
    {
        public static readonly GlobalAddr Null = new(-1);
        public GlobalAddr(int value) => Value = value;
        public readonly int Value;
        public static implicit operator Index(GlobalAddr addr) => new(addr.Value);

        public bool Equals(GlobalAddr other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is GlobalAddr other && Equals(other);

        public override int GetHashCode() => Value;
    }

    public readonly struct ElemAddr : IAddress, IEquatable<ElemAddr>
    {
        public static readonly ElemAddr Null = new(-1);
        public ElemAddr(int value) => Value = value;
        public readonly int Value;
        public static implicit operator Index(ElemAddr addr) => new(addr.Value);

        public bool Equals(ElemAddr other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ElemAddr other && Equals(other);

        public override int GetHashCode() => Value;
    }

    public readonly struct DataAddr : IAddress, IEquatable<DataAddr>
    {
        public static readonly DataAddr Null = new(-1);
        public DataAddr(int value) => Value = value;
        public readonly int Value;
        public static implicit operator Index(DataAddr addr) => new(addr.Value);

        public bool Equals(DataAddr other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is DataAddr other && Equals(other);

        public override int GetHashCode() => Value;
    }
    
}