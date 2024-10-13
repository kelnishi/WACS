using System;
using System.IO;
using System.Runtime.InteropServices;
using Wacs.Core.Types;

namespace Wacs.Core.Execution
{
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct StackValue
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

        [FieldOffset(1)] public readonly ulong V128_low;
        [FieldOffset(9)] public readonly ulong V128_high;

        // Constructors for each type
        public StackValue(int value)
        {
            this = default; // Ensure all fields are initialized
            Type = ValType.I32;
            Int32 = value;
        }

        public StackValue(long value)
        {
            this = default;
            Type = ValType.I64;
            Int64 = value;
        }

        public StackValue(float value)
        {
            this = default;
            Type = ValType.F32;
            Float32 = value;
        }

        public StackValue(double value)
        {
            this = default;
            Type = ValType.F64;
            Float64 = value;
        }

        //Default Values
        public StackValue(ValType type)
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

        public StackValue(ValType type, UInt32 idx)
        {
            this = default;
            Type = type;
            Int64 = idx;
        }

        private StackValue(ValType type, Int64 v)
        {
            this = default;
            Type = type;
            Int64 = v;
        }

        public static readonly StackValue NullFuncRef = new StackValue(ValType.Funcref, -1);
        public static readonly StackValue NullExternRef = new StackValue(ValType.Externref, -1);

        public bool IsNullRef => (Type == ValType.Funcref || Type == ValType.Externref) && Int64 == -1;
        
        public static implicit operator StackValue(int value) => new StackValue(value);
        
        public static implicit operator int(StackValue value) => value.Int32;
        public static implicit operator uint(StackValue value) => unchecked((uint)value.Int32);
        
        public static implicit operator StackValue(long value) => new StackValue(value);
        
        public static implicit operator long(StackValue value) => value.Int64;
        public static implicit operator ulong(StackValue value) => unchecked((ulong)value.Int64);
        
        public static implicit operator StackValue(float value) => new StackValue(value);
        
        public static implicit operator float(StackValue value) => value.Float32;
        
        public static implicit operator StackValue(double value) => new StackValue(value);
        
        public static implicit operator double(StackValue value) => value.Float64;
        
    }
}