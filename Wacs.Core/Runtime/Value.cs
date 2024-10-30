using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Utilities;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// @Spec 4.2.1. Values
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct Value
    {
        [FieldOffset(0)] public readonly ValType Type;

        [FieldOffset(1)] public readonly byte U8;
        
        [FieldOffset(1)] public readonly short Int16;
        
        // 32-bit integer
        [FieldOffset(1)] public readonly int Int32;
        
        // Ref Address
        [FieldOffset(1)] public readonly int Ptr;

        // 64-bit integer
        [FieldOffset(1)] public readonly long Int64;

        // 32-bit float
        [FieldOffset(1)] public readonly float Float32;

        // 64-bit float
        [FieldOffset(1)] public readonly double Float64;

        // 128-bit vector
        [FieldOffset(1)] public readonly V128 V128;

        // // ref funcIdx
        public FuncIdx FuncIdx => (FuncIdx)Int32;

        // ref.extern externIdx
        [FieldOffset(1)] public readonly ulong ExternIdx;

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
                case ValType.Funcref:
                    Ptr = idx;
                    break;
                case ValType.Externref:
                    Ptr = idx;
                    break;
                default:
                    throw new InvalidDataException($"Cannot define StackValue of type {type}");
            }
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
                case ValType.Funcref:
                    Ptr = (int)idx;
                    break;
                case ValType.Externref:
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

        //Default Values
        public Value(ValType type)
        {
            this = default;
            Type = type;
            switch (Type)
            {
                case ValType.I32:
                    Int32 = 0;
                    break;
                case ValType.I64:
                    Int64 = 0;
                    break;
                case ValType.F32:
                    Float32 = 0.0f;
                    break;
                case ValType.F64:
                    Float64 = 0.0d;
                    break;
                case ValType.V128:
                    V128 = (0L, 0L);
                    break;
                case ValType.Funcref:
                    Ptr = -1;
                    break;
                case ValType.Externref:
                    Ptr = -1;
                    break;
                case ValType.Nil:
                    Ptr = -1;
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
                case BigInteger bi:
                    Type = ValType.V128;
                    V128 = bi.ToV128();
                    break;
                case V128 v128:
                    Type = ValType.V128;
                    V128 = v128;
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
            switch (Type)
            {
                case ValType.I32:
                    Int32 = (int)externalValue;
                    break;
                case ValType.I64:
                    Int64 = (long)externalValue;
                    break;
                case ValType.F32:
                    Float32 = (float)externalValue;
                    break;
                case ValType.F64:
                    Float64 = (double)externalValue;
                    break;
                case ValType.V128:
                    V128 = new V128((byte[])externalValue);
                    break;
                case ValType.Funcref:
                    Ptr = (int)externalValue;
                    break;
                case ValType.Externref:
                    Ptr = (int)externalValue;
                    break;
                case ValType.Undefined:
                default:
                    throw new InvalidDataException($"Cannot define StackValue of type {type}");
            }
        }

        public Value Default => new Value(Type);

        public object Scalar => Type switch
        {
            ValType.I32 => Int32,
            ValType.I64 => Int64,
            ValType.F32 => Float32,
            ValType.F64 => Float64,
            ValType.V128 => V128,
            ValType.Funcref => Ptr,
            ValType.Externref => Ptr,
            _ => throw new InvalidCastException($"Cannot cast ValType {Type} to Scalar")
        };

        public static readonly Value NullFuncRef = new(ValType.Funcref);
        public static readonly Value NullExternRef = new(ValType.Externref);

        public static Value RefNull(ReferenceType type) => type switch
        {
            ReferenceType.Funcref => NullFuncRef,
            ReferenceType.Externref => NullExternRef,
            _ => throw new InvalidDataException($"Unsupported RefType: {type}"),
        };

        public bool IsI32 => Type == ValType.I32;
        public bool IsRef => Type == ValType.Funcref || Type == ValType.Externref;
        public bool IsNullRef => IsRef && Ptr == -1;
        public static implicit operator Value(int value) => new(value);
        public static implicit operator Value(uint value) => new(value);

        public static implicit operator int(Value value) => value.Int32;
        public static implicit operator uint(Value value) => unchecked((uint)value.Int32);

        public static implicit operator Value(long value) => new(value);

        public static implicit operator long(Value value) => value.Int64;
        public static implicit operator ulong(Value value) => unchecked((ulong)value.Int64);

        public static implicit operator Value(float value) => new(value);

        public static implicit operator float(Value value) => value.Float32;

        public static implicit operator Value(double value) => new(value);

        public static implicit operator double(Value value) => value.Float64;

        public static implicit operator Value(V128 v128) => new(v128);

        
    }
}