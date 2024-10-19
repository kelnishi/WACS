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

        // 32-bit integer
        [FieldOffset(1)] public readonly int Int32;

        // 64-bit integer
        [FieldOffset(1)] public readonly long Int64;

        // 32-bit float
        [FieldOffset(1)] public readonly float Float32;

        // 64-bit float
        [FieldOffset(1)] public readonly double Float64;

        // 128-bit vector
        [FieldOffset(1)] public readonly ulong V128_low;
        [FieldOffset(9)] public readonly ulong V128_high;

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

        public Value((ulong, ulong) value)
        {
            this = default;
            Type = ValType.V128;
            (V128_low, V128_high) = value;
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
                    V128_high = 0u;
                    V128_low = 0u;
                    break;
                case ValType.Funcref:
                    Int64 = -1;
                    break;
                case ValType.Externref:
                    Int64 = -1;
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
                case int i:
                    Int32 = i;
                    break;
                case uint ui:
                    Int32 = (int)ui;
                    break;
                case long l:
                    Int64 = l;
                    break;
                case ulong ul:
                    Int64 = (long)ul;
                    break;

                case BigInteger bi:
                    (V128_low, V128_high) = bi.ToV128();
                    break;

                default:
                    throw new InvalidDataException(
                        $"Cannot convert object to stack value of type {typeof(ExternalValue)}");
            }
        }

        public Value Default => new Value(Type);

        public object Scalar => Type switch
        {
            ValType.I32 => Int32,
            ValType.I64 => Int64,
            ValType.F32 => Float32,
            ValType.F64 => Float64,
            ValType.V128 => new object[] { V128_low, V128_high },
            ValType.Funcref => Int64,
            ValType.Externref => Int64,
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
        public bool IsNullRef => IsRef && Int64 == -1;
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

        public static implicit operator (ulong, ulong)(Value value) => (value.V128_low, value.V128_high);

        public static implicit operator Value((ulong, ulong) value) =>
            value == default ? new Value(DefaultV128) : new Value(value);

        public static readonly (ulong, ulong) DefaultV128 = (0UL, 0UL);
    }
}