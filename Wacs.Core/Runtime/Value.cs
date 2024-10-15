using System;
using System.IO;
using System.Runtime.InteropServices;
using Wacs.Core.Types;

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

        // ref funcaddr
        [FieldOffset(1)] public readonly ulong FuncAddr;
        
        // ref.extern externaddr
        [FieldOffset(1)] public readonly ulong ExternAddr;


        // Constructors for each type
        public Value(int value)
        {
            this = default; // Ensure all fields are initialized
            Type = ValType.I32;
            Int32 = value;
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

        //Default Values
        public Value(ValType type)
        {
            this = default;
            Type = type;
            switch (Type)
            {
                case ValType.I32: Int32 = 0; break;
                case ValType.I64: Int64 = 0; break;
                case ValType.F32: Float32 = 0.0f; break;
                case ValType.F64: Float64 = 0.0d; break;

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
                default:
                    throw new InvalidDataException($"Cannot define StackValue of type {type}");
            }
        }

        public Value(ValType type, UInt32 idx)
        {
            this = default;
            Type = type;
            Int64 = idx;
        }

        private Value(ValType type, Int64 v)
        {
            this = default;
            Type = type;
            Int64 = v;
        }

        public static readonly Value NullFuncRef = new Value(ValType.Funcref, -1);
        public static readonly Value NullExternRef = new Value(ValType.Externref, -1);

        public bool IsNullRef => (Type == ValType.Funcref || Type == ValType.Externref) && Int64 == -1;
        
        public static implicit operator Value(int value) => new Value(value);
        
        public static implicit operator int(Value value) => value.Int32;
        public static implicit operator uint(Value value) => unchecked((uint)value.Int32);
        
        public static implicit operator Value(long value) => new Value(value);
        
        public static implicit operator long(Value value) => value.Int64;
        public static implicit operator ulong(Value value) => unchecked((ulong)value.Int64);
        
        public static implicit operator Value(float value) => new Value(value);
        
        public static implicit operator float(Value value) => value.Float32;
        
        public static implicit operator Value(double value) => new Value(value);
        
        public static implicit operator double(Value value) => value.Float64;
        
    }
}