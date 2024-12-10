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
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Wacs.Core.Attributes;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// @Spec 4.2.1. Values
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 20)]
    public struct Value
    {
        [FieldOffset(0)] public ValType Type;
        [FieldOffset(4)] public byte U8;

        [FieldOffset(4)] public short Int16;

        // 32-bit integer
        [FieldOffset(4)] public int Int32;
        [FieldOffset(4)] public uint UInt32;

        // Ref Address
        [FieldOffset(4)] public int Ptr;
        //Local Variables
        [FieldOffset(16)] public bool Set;

        // 64-bit integer
        [FieldOffset(4)] public long Int64;
        [FieldOffset(4)] public ulong UInt64;

        // 32-bit float
        [FieldOffset(4)] public float Float32;

        // 64-bit float
        [FieldOffset(4)] public double Float64;

        // 128-bit vector
        [FieldOffset(4)] public MV128 V128;

        // ref.extern externIdx
        [FieldOffset(4)] public ulong ExternIdx;

        // Constructors for each type
        public Value(int value)
        {
            this = default; // Ensure all fields are initialized
            Type = ValType.I32;
            Int32 = value;
        }

        public Value(uint value)
        {
            this = default; // Ensure all fields are initialized
            Type = ValType.I32;
            Int32 = (int)value;
        }

        public Value(long value)
        {
            this = default;
            Type = ValType.I64;
            Int64 = value;
        }
        
        public Value(ulong value)
        {
            this = default;
            Type = ValType.I64;
            Int64 = (long)value;
        }

        public Value(float value)
        {
            this = default;
            Type = ValType.F32;
            Float32 = value;
        }

        public Value(double value)
        {
            this = default;
            Type = ValType.F64;
            Float64 = value;
        }

        public Value(V128 value)
        {
            this = default;
            Type = ValType.V128;
            V128 = value;
        }

        public Value(uint v0, uint v1, uint v2, uint v3)
        {
            this = default;
            Type = ValType.V128;
            V128 = new V128(v0, v1, v2, v3);
        }

        public Value(ValType type, int idx)
        {
            this = default;
            Type = type;
            switch (Type)
            {
                case ValType.I32:
                    Int32 = idx;
                    break;
                case ValType.I64:
                    Int64 = idx;
                    break;
                case ValType.FuncRef:
                    Ptr = idx;
                    break;
                case ValType.ExternRef:
                    Ptr = idx;
                    break;
                default:
                    throw new InvalidDataException($"Cannot define StackValue of type {type}");
            }
        }

        public Value(ValType type, string text)
        {
            if (text == "null")
            {
                this = Null(type);
                return;
            }
            this = default;
            Type = type;
            switch (Type)
            {
                case ValType.I32:
                    Int32 = BitBashInt(text);
                    break;
                case ValType.I64:
                    Int64 = BitBashLong(text);
                    break;
                case ValType.F32:
                    Float32 = BitBashFloat(text);
                    break;
                case ValType.F64:
                    Float64 = BitBashDouble(text);
                    break;
                case ValType.FuncRef:
                    Ptr = BitBashRef(text);
                    break;
                case ValType.ExternRef:
                    Ptr = BitBashRef(text);
                    break;
                default:
                    throw new InvalidDataException($"Cannot define StackValue of type {type}");
            }
        }

        private static int BitBashInt(string intVal)
        {
            decimal value = decimal.Parse(intVal);
            if (value > uint.MaxValue)
                throw new InvalidDataException($"Integer value {intVal} out of range");
            if (value < int.MinValue)
                throw new InvalidDataException($"Integer value {intVal} out of range");
                
            if (value > int.MaxValue && value <= uint.MaxValue)
            {
                return (int)(uint)value;
            }
            return (int)value;
        }
        
        private static long BitBashLong(string intVal)
        {
            decimal value = decimal.Parse(intVal);
            if (value > ulong.MaxValue)
                throw new InvalidDataException($"Integer value {intVal} out of range");
            if (value < long.MinValue)
                throw new InvalidDataException($"Integer value {intVal} out of range");
                
            if (value > long.MaxValue && value <= ulong.MaxValue)
            {
                return (long)(ulong)value;
            }
            return (long)value;
        }

        private static int BitBashRef(string textVal)
        {
            if (textVal == "null")
                return -1;
            uint v = uint.Parse(textVal);
            return (int)v;
        }
        
        private static float BitBashFloat(string intval)
        {
            if (intval.StartsWith("nan:"))
                return float.NaN;
            decimal value = decimal.Parse(intval);
            if (value > int.MaxValue && value <= uint.MaxValue)
            {
                int v = (int)(uint)value;
                return BitConverter.ToSingle(BitConverter.GetBytes(v));
            }
            return BitConverter.ToSingle(BitConverter.GetBytes((int)value));
        }

        private static double BitBashDouble(string longval)
        {
            if (longval.StartsWith("nan:"))
                return double.NaN;
            decimal value = decimal.Parse(longval);
            if (value > long.MaxValue && value <= ulong.MaxValue)
            {
                long v = (long)(ulong)value;
                return BitConverter.ToDouble(BitConverter.GetBytes(v));
            }
            
            return BitConverter.ToDouble(BitConverter.GetBytes((long)value));
        }

        public Value(ValType type, uint idx)
        {
            this = default;
            Type = type;
            switch (Type)
            {
                case ValType.I32:
                    Int32 = (int)idx;
                    break;
                case ValType.I64:
                    Int64 = idx;
                    break;
                case ValType.FuncRef:
                    Ptr = (int)idx;
                    break;
                case ValType.ExternRef:
                    Ptr = (int)idx;
                    break;
                default:
                    throw new InvalidDataException($"Cannot define StackValue of type {type}");
            }
        }

        public Value(ValType type, ulong v)
        {
            this = default;
            Type = type;
            Int64 = (long)v;
        }

        public Value MakeSet()
        {
            Set = true;
            return this;
        }

        //Default Values
        public Value(ValType type)
        {
            this = default;

            if (type.IsRefType() && type.IsDefType())
            {
                Type = type;
                if (type.IsNullable())
                {
                    Ptr = -1;
                    Set = true;
                }
                return;
            }
            
            Type = type;
            switch (Type)
            {
                case ValType.I32:
                    Int32 = 0;
                    Set = true;
                    break;
                case ValType.I64:
                    Int64 = 0;
                    Set = true;
                    break;
                case ValType.F32:
                    Float32 = 0.0f;
                    Set = true;
                    break;
                case ValType.F64:
                    Float64 = 0.0d;
                    Set = true;
                    break;
                case ValType.V128:
                    V128 = (V128)(0L, 0L);
                    Set = true;
                    break;
                case ValType.FuncRef:
                    Ptr = -1;
                    Set = true;
                    break;
                case ValType.Func:
                    Ptr = 0;
                    break;
                case ValType.ExternRef:
                    Ptr = -1;
                    Set = true;
                    break;
                case ValType.Extern:
                    Ptr = 0;
                    break;
                case ValType.Nil:
                    Ptr = -1;
                    Set = true;
                    break;
                case ValType.Bot:
                    Ptr = -1;
                    Set = true;
                    break;
                case ValType.None:
                    Ptr = -1;
                    Set = true;
                    break;
                case ValType.NoFunc:
                    Ptr = -1;
                    Set = true;
                    break;
                case ValType.NoExtern:
                    Ptr = -1;
                    Set = true;
                    break;
                case ValType.Any:
                    Ptr = -1;
                    Set = true;
                    break;
                case ValType.Undefined:
                default:
                    throw new InvalidDataException($"Cannot define StackValue of type {type}");
            }
        }
        
        public Value(object externalValue)
        {
            this = default;
            switch (externalValue)
            {
                case byte b:
                    Type = ValType.I32;
                    U8 = b;
                    break;
                case sbyte sb:
                    Type = ValType.I32;
                    Int32 = sb;
                    break;
                case short s:
                    Type = ValType.I32;
                    Int32 = s;
                    break;
                case ushort us:
                    Type = ValType.I32;
                    Int32 = us;
                    break;
                case int i:
                    Type = ValType.I32;
                    Int32 = i;
                    break;
                case uint ui:
                    Type = ValType.I32;
                    Int32 = (int)ui;
                    break;
                case long l:
                    Type = ValType.I64;
                    Int64 = l;
                    break;
                case ulong ul:
                    Type = ValType.I64;
                    Int64 = (long)ul;
                    break;
                case float f:
                    Type = ValType.F32;
                    Float32 = f;
                    break;
                case double d:
                    Type = ValType.F64;
                    Float64 = d;
                    break;
                case BigInteger bi:
                    Type = ValType.V128;
                    V128 = (V128)bi.ToV128();
                    break;
                case V128 v128:
                    Type = ValType.V128;
                    V128 = v128;
                    break;
                case Value value:
                    Type = value.Type;
                    V128 = value.V128;
                    break;
                default:
                    throw new InvalidDataException(
                        $"Cannot convert object to stack value of type {typeof(ExternalValue)}");
            }
        }
        
        
        public Value(ValType type, object externalValue)
        {
            this = default;
            Type = type;

            if (Type.IsRefType())
            {
                Ptr = (int)externalValue;
                return;
            }
            
            switch (Type)
            {
                case ValType.I32:
                    Int32 = (int)externalValue;
                    break;
                case ValType.U32: //Special case for transpiler
                    Type = ValType.I32;
                    UInt32 = (uint)externalValue;
                    break;
                case ValType.I64:
                    Int64 = (long)externalValue;
                    break;
                case ValType.U64: //Special case for transpiler
                    Type = ValType.I64;
                    UInt64 = (ulong)externalValue;
                    break;
                case ValType.F32:
                    Float32 = (float)externalValue;
                    break;
                case ValType.F64:
                    Float64 = (double)externalValue;
                    break;
                case ValType.V128:
                    V128 = (V128)externalValue;
                    break;
                case ValType.Undefined:
                default:
                    throw new InvalidDataException($"Cannot define StackValue of type {type}");
            }
        }

        public Value(Value copy, ValType newType)
        {
            this = copy;
            Type = newType;
        }
        
        public Value Default => new Value(Type);

        public Value ToConcrete() => new(this, this.Type.ToConcrete());

        public object Scalar => Type switch
        {
            ValType.I32 => Int32,
            ValType.I64 => Int64,
            ValType.F32 => Float32,
            ValType.F64 => Float64,
            ValType.V128 => V128,
            _ when Type.IsRefType() => Ptr,
            _ => throw new InvalidCastException($"Cannot cast ValType {Type} to Scalar")
        };
        
        public static readonly Value Bot = new(ValType.Bot);
        public static readonly Value NullRef = new(ValType.None);
        public static readonly Value NullFuncRef = new(ValType.NoFunc);
        public static readonly Value NullExternRef = new(ValType.NoExtern);
        public static readonly Value Void = new (ValType.Nil);

        public static Value Null(ValType type) => type switch
        {
            ValType.FuncRef => NullFuncRef,
            ValType.ExternRef => NullExternRef,
            _ when type.IsRefType() && type.IsNullable() => new(type),
            _ => throw new InvalidCastException($"Cannot create null value for type {(Wat)type}")
        };

        public static Value DefaultOrNull(ValType type) => type switch
        {
            ValType.FuncRef => NullFuncRef,
            ValType.ExternRef => NullExternRef,
            _ => new(type),
        };

        public bool IsI32 => Type == ValType.I32;
        public bool IsV128 => Type == ValType.V128;
        public bool IsRef => Type.IsRefType();
        public bool IsNullRef => IsRef && Ptr == -1;

        public static object ToObject(Value value) => value.Scalar;

        public static implicit operator Value(int value) => new(value);
        public static implicit operator Value(uint value) => new(value);

        public static implicit operator int(Value value) => value.Int32;
        public static implicit operator uint(Value value) => unchecked((uint)value.Int32);

        public static implicit operator Value(long value) => new(value);
        public static implicit operator Value(ulong value) => new(value);

        public static implicit operator long(Value value) => value.Int64;
        public static implicit operator ulong(Value value) => unchecked((ulong)value.Int64);

        public static implicit operator Value(float value) => new(value);

        public static implicit operator float(Value value) => value.Float32;

        public static implicit operator Value(double value) => new(value);

        public static implicit operator double(Value value) => value.Float64;

        public static implicit operator Value(V128 v128) => new(v128);
        
        public static implicit operator V128(Value value) => value.V128;
        
        private static bool EqualFloats(float a, float b)
        {
            if (float.IsNaN(a) && float.IsNaN(b))
                return true;
            if (float.IsPositiveInfinity(a) && float.IsPositiveInfinity(b))
                return true;
            if (float.IsNegativeInfinity(a) && float.IsNegativeInfinity(b))
                return true;
            float epsilon = 1E-6f;
            return Math.Abs(a - b) < epsilon;
        }
        public static bool operator ==(Value left, Value right)
        {
            if (left.Type == ValType.V128 && right.Type == ValType.V128)
            {
                if (EqualFloats(left.V128.F32x4_0, right.V128.F32x4_0)
                    && EqualFloats(left.V128.F32x4_1, right.V128.F32x4_1)
                    && EqualFloats(left.V128.F32x4_2, right.V128.F32x4_2)
                    && EqualFloats(left.V128.F32x4_3, right.V128.F32x4_3))
                    return true;
            }
            return left.Type == right.Type && left.Scalar.Equals(right.Scalar);
        }

        public static bool operator !=(Value left, Value right) => !(left == right);

        public int CompareTo(Value other) => Type == other.Type ? ((double)Scalar).CompareTo(((double)other.Scalar)) : Type.CompareTo(other.Type);

        public override bool Equals(object obj) => obj is Value value && this == value;

        public override int GetHashCode() => HashCode.Combine(Type, Scalar);
        
        
        public override string ToString()
        {
            return Type switch
            {
                ValType.I32 => $"{Type.ToWat()}={Int32.ToString()}",
                ValType.I64 => $"{Type.ToWat()}={Int64.ToString()}",
                ValType.F32 => $"{Type.ToWat()}={Float32.ToString("G10", CultureInfo.InvariantCulture)}",
                ValType.F64 => $"{Type.ToWat()}={Float64.ToString("G10", CultureInfo.InvariantCulture)}",
                ValType.V128 => $"{Type.ToWat()}={V128.ToString()}",
                ValType.Bot => "Bot",
                _ when Type.IsRefType() => $"{(Wat)Type}=&{Ptr}",
                _ => "Undefined",
            };
        }
        
    }
}