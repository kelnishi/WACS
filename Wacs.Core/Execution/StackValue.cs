using System;
using System.Runtime.InteropServices;
using Wacs.Core.Types;

namespace Wacs.Core.Execution
{
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct StackValue
    {
        [FieldOffset(0)]
        public readonly ValType Type;

        // 32-bit integer
        [FieldOffset(1)]
        public readonly int Int32;

        // 64-bit integer
        [FieldOffset(1)]
        public readonly long Int64;

        // 32-bit float
        [FieldOffset(1)]
        public readonly float Float32;

        // 64-bit float
        [FieldOffset(1)]
        public readonly double Float64;

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
        
        public static implicit operator StackValue(int value) => new StackValue(value);
        public static implicit operator StackValue(uint value) => new StackValue((int)value);
        
        public static implicit operator int(StackValue value) => value.Int32;
        public static implicit operator uint(StackValue value) => unchecked((uint)value.Int32);
        
        public static implicit operator StackValue(long value) => new StackValue(value);
        public static implicit operator StackValue(ulong value) => new StackValue((long)value);
        
        public static implicit operator long(StackValue value) => value.Int64;
        public static implicit operator ulong(StackValue value) => unchecked((ulong)value.Int64);
        
        public static implicit operator StackValue(float value) => new StackValue(value);
        
        public static implicit operator float(StackValue value) => value.Float32;
        
        public static implicit operator StackValue(double value) => new StackValue(value);
        
        public static implicit operator double(StackValue value) => value.Float64;
        
    }
}