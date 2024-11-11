using System;
using System.IO;
using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming

namespace Wacs.Core.Runtime
{
    public enum V128Shape
    {
        I8x16,
        I16x8,
        I32x4,
        I64x2,
        
        F32x4,
        F64x2,
        
        B8x16,
        B16x8,
        B32x4,
        B64x2
    }
    
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct V128
    {
        [FieldOffset(0x0)] public readonly sbyte I8x16_0;
        [FieldOffset(0x1)] public readonly sbyte I8x16_1;
        [FieldOffset(0x2)] public readonly sbyte I8x16_2;
        [FieldOffset(0x3)] public readonly sbyte I8x16_3;
        [FieldOffset(0x4)] public readonly sbyte I8x16_4;
        [FieldOffset(0x5)] public readonly sbyte I8x16_5;
        [FieldOffset(0x6)] public readonly sbyte I8x16_6;
        [FieldOffset(0x7)] public readonly sbyte I8x16_7;
        [FieldOffset(0x8)] public readonly sbyte I8x16_8;
        [FieldOffset(0x9)] public readonly sbyte I8x16_9;
        [FieldOffset(0xA)] public readonly sbyte I8x16_A;
        [FieldOffset(0xB)] public readonly sbyte I8x16_B;
        [FieldOffset(0xC)] public readonly sbyte I8x16_C;
        [FieldOffset(0xD)] public readonly sbyte I8x16_D;
        [FieldOffset(0xE)] public readonly sbyte I8x16_E;
        [FieldOffset(0xF)] public readonly sbyte I8x16_F;

        [FieldOffset(0x0)] public readonly short I16x8_0;
        [FieldOffset(0x2)] public readonly short I16x8_1;
        [FieldOffset(0x4)] public readonly short I16x8_2;
        [FieldOffset(0x6)] public readonly short I16x8_3;
        [FieldOffset(0x8)] public readonly short I16x8_4;
        [FieldOffset(0xA)] public readonly short I16x8_5;
        [FieldOffset(0xC)] public readonly short I16x8_6;
        [FieldOffset(0xE)] public readonly short I16x8_7;

        [FieldOffset(0x0)] public readonly int I32x4_0;
        [FieldOffset(0x4)] public readonly int I32x4_1;
        [FieldOffset(0x8)] public readonly int I32x4_2;
        [FieldOffset(0xC)] public readonly int I32x4_3;

        [FieldOffset(0x0)] public readonly long I64x2_0;
        [FieldOffset(0x8)] public readonly long I64x2_1;

        [FieldOffset(0x0)] public readonly float F32x4_0;
        [FieldOffset(0x4)] public readonly float F32x4_1;
        [FieldOffset(0x8)] public readonly float F32x4_2;
        [FieldOffset(0xC)] public readonly float F32x4_3;

        [FieldOffset(0x0)] public readonly double F64x2_0;
        [FieldOffset(0x8)] public readonly double F64x2_1;

        [FieldOffset(0x0)] public readonly byte U8x16_0;
        [FieldOffset(0x1)] public readonly byte U8x16_1;
        [FieldOffset(0x2)] public readonly byte U8x16_2;
        [FieldOffset(0x3)] public readonly byte U8x16_3;
        [FieldOffset(0x4)] public readonly byte U8x16_4;
        [FieldOffset(0x5)] public readonly byte U8x16_5;
        [FieldOffset(0x6)] public readonly byte U8x16_6;
        [FieldOffset(0x7)] public readonly byte U8x16_7;
        [FieldOffset(0x8)] public readonly byte U8x16_8;
        [FieldOffset(0x9)] public readonly byte U8x16_9;
        [FieldOffset(0xA)] public readonly byte U8x16_A;
        [FieldOffset(0xB)] public readonly byte U8x16_B;
        [FieldOffset(0xC)] public readonly byte U8x16_C;
        [FieldOffset(0xD)] public readonly byte U8x16_D;
        [FieldOffset(0xE)] public readonly byte U8x16_E;
        [FieldOffset(0xF)] public readonly byte U8x16_F;

        [FieldOffset(0x0)] public readonly ushort U16x8_0;
        [FieldOffset(0x2)] public readonly ushort U16x8_1;
        [FieldOffset(0x4)] public readonly ushort U16x8_2;
        [FieldOffset(0x6)] public readonly ushort U16x8_3;
        [FieldOffset(0x8)] public readonly ushort U16x8_4;
        [FieldOffset(0xA)] public readonly ushort U16x8_5;
        [FieldOffset(0xC)] public readonly ushort U16x8_6;
        [FieldOffset(0xE)] public readonly ushort U16x8_7;

        [FieldOffset(0x0)] public readonly uint U32x4_0;
        [FieldOffset(0x4)] public readonly uint U32x4_1;
        [FieldOffset(0x8)] public readonly uint U32x4_2;
        [FieldOffset(0xC)] public readonly uint U32x4_3;

        [FieldOffset(0x0)] public readonly ulong U64x2_0;
        [FieldOffset(0x8)] public readonly ulong U64x2_1;

        public V128(long i64x2_0, long i64x2_1)
        {
            this = default;
            I64x2_0 = i64x2_0;
            I64x2_1 = i64x2_1;  
        } 
        public static implicit operator V128((long, long) tuple) => new(tuple.Item1, tuple.Item2);

        public V128(
            sbyte i8x16_0, sbyte i8x16_1, sbyte i8x16_2, sbyte i8x16_3,
            sbyte i8x16_4, sbyte i8x16_5, sbyte i8x16_6, sbyte i8x16_7, 
            sbyte i8x16_8, sbyte i8x16_9, sbyte i8X16_A, sbyte i8X16_B, 
            sbyte i8X16_C, sbyte i8X16_D, sbyte i8X16_E, sbyte i8X16_F)
        {
            this = default;
            I8x16_0 = i8x16_0; I8x16_1 = i8x16_1; I8x16_2 = i8x16_2; I8x16_3 = i8x16_3;
            I8x16_4 = i8x16_4; I8x16_5 = i8x16_5; I8x16_6 = i8x16_6; I8x16_7 = i8x16_7;
            I8x16_8 = i8x16_8; I8x16_9 = i8x16_9; I8x16_A = i8X16_A; I8x16_B = i8X16_B;
            I8x16_C = i8X16_C; I8x16_D = i8X16_D; I8x16_E = i8X16_E; I8x16_F = i8X16_F;
        }
        public static implicit operator V128((
            sbyte, sbyte, sbyte, sbyte, 
            sbyte, sbyte, sbyte, sbyte, 
            sbyte, sbyte, sbyte, sbyte, 
            sbyte, sbyte, sbyte, sbyte) tuple) => 
            new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, 
                tuple.Item5, tuple.Item6, tuple.Item7, tuple.Item8, 
                tuple.Item9, tuple.Item10, tuple.Item11, tuple.Item12, 
                tuple.Item13, tuple.Item14, tuple.Item15, tuple.Item16);

        public V128(
            short i16x8_0, short i16x8_1, short i16x8_2, short i16x8_3, 
            short i16x8_4, short i16x8_5, short i16x8_6, short i16x8_7)
        {
            this = default;
            I16x8_0 = i16x8_0; I16x8_1 = i16x8_1; I16x8_2 = i16x8_2; I16x8_3 = i16x8_3;
            I16x8_4 = i16x8_4; I16x8_5 = i16x8_5; I16x8_6 = i16x8_6; I16x8_7 = i16x8_7;
        }
        public static implicit operator V128((
            short, short, short, short, 
            short, short, short, short) tuple) => 
            new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, 
                tuple.Item5, tuple.Item6, tuple.Item7, tuple.Item8);

        public V128(int i32x4_0, int i32x4_1, int i32x4_2, int i32x4_3)
        {
            this = default;
            I32x4_0 = i32x4_0;
            I32x4_1 = i32x4_1;
            I32x4_2 = i32x4_2;
            I32x4_3 = i32x4_3;
        }
        public static implicit operator V128((int, int, int, int) tuple) =>
            new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);

        public V128(float f32x4_0, float f32x4_1, float f32x4_2, float f32x4_3)
        {
            this = default;
            F32x4_0 = f32x4_0;
            F32x4_1 = f32x4_1;
            F32x4_2 = f32x4_2;
            F32x4_3 = f32x4_3;
        }
        public static implicit operator V128((float, float, float, float) tuple) =>
            new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);

        public V128(double f64x2_0, double f64x2_1)
        {
            this = default;
            F64x2_0 = f64x2_0;
            F64x2_1 = f64x2_1;  
        }  
        public static implicit operator V128((double, double) tuple) => 
            new(tuple.Item1, tuple.Item2);

        public V128(
            byte u8X160, byte u8X161, byte u8X162, byte u8X163, 
            byte u8X164, byte u8X165, byte u8X166, byte u8X167, 
            byte u8X168, byte u8X169, byte u8X16A, byte u8X16B,
            byte u8X16C, byte u8X16D, byte u8X16E, byte u8X16F)
        {
            this = default;
            U8x16_0 = u8X160;
            U8x16_1 = u8X161;
            U8x16_2 = u8X162;
            U8x16_3 = u8X163;
            U8x16_4 = u8X164;
            U8x16_5 = u8X165;
            U8x16_6 = u8X166;
            U8x16_7 = u8X167;
            U8x16_8 = u8X168;
            U8x16_9 = u8X169;
            U8x16_A = u8X16A;
            U8x16_B = u8X16B;
            U8x16_C = u8X16C;
            U8x16_D = u8X16D;
            U8x16_E = u8X16E;
            U8x16_F = u8X16F;
        }
        public static implicit operator V128((
            byte, byte, byte, byte,
            byte, byte, byte, byte, 
            byte, byte, byte, byte, 
            byte, byte, byte, byte) tuple) => 
            new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4,
                tuple.Item5, tuple.Item6, tuple.Item7, tuple.Item8,
                tuple.Item9, tuple.Item10, tuple.Item11, tuple.Item12, 
                tuple.Item13, tuple.Item14, tuple.Item15, tuple.Item16);

        public V128(
            ushort u16X80, ushort u16X81, ushort u16X82, ushort u16X83, 
            ushort u16X84, ushort u16X85, ushort u16X86, ushort u16X87)
        {
            this = default;
            U16x8_0 = u16X80;
            U16x8_1 = u16X81;
            U16x8_2 = u16X82;
            U16x8_3 = u16X83;
            U16x8_4 = u16X84;
            U16x8_5 = u16X85;
            U16x8_6 = u16X86;
            U16x8_7 = u16X87;
        }
        public static implicit operator V128((
            ushort, ushort, ushort, ushort, 
            ushort, ushort, ushort, ushort) tuple) => 
            new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4,
                tuple.Item5, tuple.Item6, tuple.Item7, tuple.Item8);
        
        public V128(uint u32X40, uint u32X41, uint u32X42, uint u32X43)
        {
            this = default;
            U32x4_0 = u32X40;
            U32x4_1 = u32X41;
            U32x4_2 = u32X42;
            U32x4_3 = u32X43;
        }
        public static implicit operator V128((uint, uint, uint, uint) tuple) =>
            new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);

        public V128(ulong u64X20, ulong u64X21)
        {
            this = default;
            U64x2_0 = u64X20;
            U64x2_1 = u64X21;
        }
        public static implicit operator V128((ulong, ulong) tuple) => 
            new(tuple.Item1, tuple.Item2);

        public V128(Span<byte> data)
        {
            if (data.Length != 16)
                throw new InvalidDataException($"Cannot create V128 from {data.Length} bytes");
            
            this = default;
            U8x16_0 = data[0x0];
            U8x16_1 = data[0x1];
            U8x16_2 = data[0x2];
            U8x16_3 = data[0x3];
            U8x16_4 = data[0x4];
            U8x16_5 = data[0x5];
            U8x16_6 = data[0x6];
            U8x16_7 = data[0x7];
            U8x16_8 = data[0x8];
            U8x16_9 = data[0x9];
            U8x16_A = data[0xA];
            U8x16_B = data[0xB];
            U8x16_C = data[0xC];
            U8x16_D = data[0xD];
            U8x16_E = data[0xE];
            U8x16_F = data[0xF];
        }
        
        public byte this[byte index] => index switch {
            0x0 => U8x16_0,
            0x1 => U8x16_1,
            0x2 => U8x16_2,
            0x3 => U8x16_3,
            0x4 => U8x16_4,
            0x5 => U8x16_5,
            0x6 => U8x16_6,
            0x7 => U8x16_7,
            0x8 => U8x16_8,
            0x9 => U8x16_9,
            0xA => U8x16_A,
            0xB => U8x16_B,
            0xC => U8x16_C,
            0xD => U8x16_D,
            0xE => U8x16_E,
            0xF => U8x16_F,
            _ => throw new ArgumentOutOfRangeException($"Cannot get byte index {index} of MV128")
        };
        
        
        public short this[short index] => index switch {
            0x0 => I16x8_0,
            0x1 => I16x8_1,
            0x2 => I16x8_2,
            0x3 => I16x8_3,
            0x4 => I16x8_4,
            0x5 => I16x8_5,
            0x6 => I16x8_6,
            0x7 => I16x8_7,
            _ => throw new ArgumentOutOfRangeException($"Cannot get i16 index {index} of MV128")
        };
        
        
        public int this[int index] => index switch {
            0x0 => I32x4_0,
            0x1 => I32x4_1,
            0x2 => I32x4_2,
            0x3 => I32x4_3,
            _ => throw new ArgumentOutOfRangeException($"Cannot get i32 index {index} of MV128")
        };
        
        public float this[float index] => index switch {
            0f => F32x4_0,
            1f => F32x4_1,
            2f => F32x4_2,
            3f => F32x4_3,
            _ => throw new ArgumentOutOfRangeException($"Cannot get float index {index} of MV128")
        };
        
        public long this[long index]=> index switch {
            0x0 => I64x2_0,
            0x1 => I64x2_1,
            _ => throw new ArgumentOutOfRangeException($"Cannot get i64 index {index} of MV128")
        };
        
        public ulong this[ulong index] => index switch {
            0x0 => U64x2_0,
            0x1 => U64x2_1,
            _ => throw new ArgumentOutOfRangeException($"Cannot get i64 index {index} of MV128")
        };
        
        public double this[double index] => index switch {
            0.0 => F64x2_0,
            1.0 => F64x2_1,
            _ => throw new ArgumentOutOfRangeException($"Cannot get double index {index} of MV128")
        };
        
        public static V128 operator &(V128 left, V128 right) =>
            new(left.U64x2_0 & right.U64x2_0, left.U64x2_1 & right.U64x2_1);

        public static V128 operator |(V128 left, V128 right) =>
            new(left.U64x2_0 | right.U64x2_0, left.U64x2_1 | right.U64x2_1);

        public static V128 operator ^(V128 left, V128 right) =>
            new(left.U64x2_0 ^ right.U64x2_0, left.U64x2_1 ^ right.U64x2_1);
        
        public static V128 operator ~(V128 value) =>
            new(~value.U64x2_0, ~value.U64x2_1);
        
    }

    //Mutable version
    [StructLayout(LayoutKind.Explicit)]
    public struct MV128
    {
        [FieldOffset(0x0)] public sbyte I8x16_0;
        [FieldOffset(0x1)] public sbyte I8x16_1;
        [FieldOffset(0x2)] public sbyte I8x16_2;
        [FieldOffset(0x3)] public sbyte I8x16_3;
        [FieldOffset(0x4)] public sbyte I8x16_4;
        [FieldOffset(0x5)] public sbyte I8x16_5;
        [FieldOffset(0x6)] public sbyte I8x16_6;
        [FieldOffset(0x7)] public sbyte I8x16_7;
        [FieldOffset(0x8)] public sbyte I8x16_8;
        [FieldOffset(0x9)] public sbyte I8x16_9;
        [FieldOffset(0xA)] public sbyte I8x16_A;
        [FieldOffset(0xB)] public sbyte I8x16_B;
        [FieldOffset(0xC)] public sbyte I8x16_C;
        [FieldOffset(0xD)] public sbyte I8x16_D;
        [FieldOffset(0xE)] public sbyte I8x16_E;
        [FieldOffset(0xF)] public sbyte I8x16_F;

        [FieldOffset(0x0)] public short I16x8_0;
        [FieldOffset(0x2)] public short I16x8_1;
        [FieldOffset(0x4)] public short I16x8_2;
        [FieldOffset(0x6)] public short I16x8_3;
        [FieldOffset(0x8)] public short I16x8_4;
        [FieldOffset(0xA)] public short I16x8_5;
        [FieldOffset(0xC)] public short I16x8_6;
        [FieldOffset(0xE)] public short I16x8_7;

        [FieldOffset(0x0)] public int I32x4_0;
        [FieldOffset(0x4)] public int I32x4_1;
        [FieldOffset(0x8)] public int I32x4_2;
        [FieldOffset(0xC)] public int I32x4_3;

        [FieldOffset(0x0)] public long I64x2_0;
        [FieldOffset(0x8)] public long I64x2_1;

        [FieldOffset(0x0)] public float F32x4_0;
        [FieldOffset(0x4)] public float F32x4_1;
        [FieldOffset(0x8)] public float F32x4_2;
        [FieldOffset(0xC)] public float F32x4_3;

        [FieldOffset(0x0)] public double F64x2_0;
        [FieldOffset(0x8)] public double F64x2_1;

        [FieldOffset(0x0)] public byte U8x16_0;
        [FieldOffset(0x1)] public byte U8x16_1;
        [FieldOffset(0x2)] public byte U8x16_2;
        [FieldOffset(0x3)] public byte U8x16_3;
        [FieldOffset(0x4)] public byte U8x16_4;
        [FieldOffset(0x5)] public byte U8x16_5;
        [FieldOffset(0x6)] public byte U8x16_6;
        [FieldOffset(0x7)] public byte U8x16_7;
        [FieldOffset(0x8)] public byte U8x16_8;
        [FieldOffset(0x9)] public byte U8x16_9;
        [FieldOffset(0xA)] public byte U8x16_A;
        [FieldOffset(0xB)] public byte U8x16_B;
        [FieldOffset(0xC)] public byte U8x16_C;
        [FieldOffset(0xD)] public byte U8x16_D;
        [FieldOffset(0xE)] public byte U8x16_E;
        [FieldOffset(0xF)] public byte U8x16_F;

        [FieldOffset(0x0)] public ushort U16x8_0;
        [FieldOffset(0x2)] public ushort U16x8_1;
        [FieldOffset(0x4)] public ushort U16x8_2;
        [FieldOffset(0x6)] public ushort U16x8_3;
        [FieldOffset(0x8)] public ushort U16x8_4;
        [FieldOffset(0xA)] public ushort U16x8_5;
        [FieldOffset(0xC)] public ushort U16x8_6;
        [FieldOffset(0xE)] public ushort U16x8_7;

        [FieldOffset(0x0)] public uint U32x4_0;
        [FieldOffset(0x4)] public uint U32x4_1;
        [FieldOffset(0x8)] public uint U32x4_2;
        [FieldOffset(0xC)] public uint U32x4_3;

        [FieldOffset(0x0)] public ulong U64x2_0;
        [FieldOffset(0x8)] public ulong U64x2_1;

        public byte this[byte index]
        {
            get => index switch {
                0x0 => U8x16_0,
                0x1 => U8x16_1,
                0x2 => U8x16_2,
                0x3 => U8x16_3,
                0x4 => U8x16_4,
                0x5 => U8x16_5,
                0x6 => U8x16_6,
                0x7 => U8x16_7,
                0x8 => U8x16_8,
                0x9 => U8x16_9,
                0xA => U8x16_A,
                0xB => U8x16_B,
                0xC => U8x16_C,
                0xD => U8x16_D,
                0xE => U8x16_E,
                0xF => U8x16_F,
                _ => throw new ArgumentOutOfRangeException($"Cannot get byte index {index} of MV128")
            };
            set {
                switch (index)
                {
                    case 0x0: U8x16_0 = value; break;
                    case 0x1: U8x16_1 = value; break;
                    case 0x2: U8x16_2 = value; break;
                    case 0x3: U8x16_3 = value; break;
                    case 0x4: U8x16_4 = value; break;
                    case 0x5: U8x16_5 = value; break;
                    case 0x6: U8x16_6 = value; break;
                    case 0x7: U8x16_7 = value; break;
                    case 0x8: U8x16_8 = value; break;
                    case 0x9: U8x16_9 = value; break;
                    case 0xA: U8x16_A = value; break;
                    case 0xB: U8x16_B = value; break;
                    case 0xC: U8x16_C = value; break;
                    case 0xD: U8x16_D = value; break;
                    case 0xE: U8x16_E = value; break;
                    case 0xF: U8x16_F = value; break;
                    default: throw new ArgumentOutOfRangeException($"Cannot get byte index {index} of MV128");
                }
            }
        }
        
        public short this[short index]
        {
            get => index switch {
                0x0 => I16x8_0,
                0x1 => I16x8_1,
                0x2 => I16x8_2,
                0x3 => I16x8_3,
                0x4 => I16x8_4,
                0x5 => I16x8_5,
                0x6 => I16x8_6,
                0x7 => I16x8_7,
                _ => throw new ArgumentOutOfRangeException($"Cannot get i16 index {index} of MV128")
            };
            set {
                switch (index)
                {
                    case 0x0: I16x8_0 = value; break;
                    case 0x1: I16x8_1 = value; break;
                    case 0x2: I16x8_2 = value; break;
                    case 0x3: I16x8_3 = value; break;
                    case 0x4: I16x8_4 = value; break;
                    case 0x5: I16x8_5 = value; break;
                    case 0x6: I16x8_6 = value; break;
                    case 0x7: I16x8_7 = value; break;
                    default: throw new ArgumentOutOfRangeException($"Cannot get i16 index {index} of MV128");
                }
            }
        }
        
        public int this[int index]
        {
            get => index switch {
                0x0 => I32x4_0,
                0x1 => I32x4_1,
                0x2 => I32x4_2,
                0x3 => I32x4_3,
                _ => throw new ArgumentOutOfRangeException($"Cannot get i32 index {index} of MV128")
            };
            set {
                switch (index)
                {
                    case 0x0: I32x4_0 = value; break;
                    case 0x1: I32x4_1 = value; break;
                    case 0x2: I32x4_2 = value; break;
                    case 0x3: I32x4_3 = value; break;
                    default: throw new ArgumentOutOfRangeException($"Cannot get i32 index {index} of MV128");
                }
            }
        }
        
        public float this[float index]
        {
            get => index switch
            {
                0f => F32x4_0,
                1f => F32x4_1,
                2f => F32x4_2,
                3f => F32x4_3,
                _ => throw new ArgumentOutOfRangeException($"Cannot get float index {index} of MV128")
            };
            set {
                switch (index)
                {
                    case 0f: F32x4_0 = value; break;
                    case 1f: F32x4_1 = value; break;
                    case 2f: F32x4_2 = value; break;
                    case 3f: F32x4_3 = value; break;
                    default: throw new ArgumentOutOfRangeException($"Cannot set float index {index} of MV128");
                }
            }
        }

        public long this[long index]
        {
            get => index switch {
                0x0 => I64x2_0,
                0x1 => I64x2_1,
                _ => throw new ArgumentOutOfRangeException($"Cannot get i64 index {index} of MV128")
            };
            set {
                switch (index)
                {
                    case 0x0: I64x2_0 = value; break;
                    case 0x1: I64x2_1 = value; break;
                    default: throw new ArgumentOutOfRangeException($"Cannot get i64 index {index} of MV128");
                }
            }
        }
        
        public ulong this[ulong index]
        {
            get => index switch {
                0x0 => U64x2_0,
                0x1 => U64x2_1,
                _ => throw new ArgumentOutOfRangeException($"Cannot get i64 index {index} of MV128")
            };
            set {
                switch (index)
                {
                    case 0x0: U64x2_0 = value; break;
                    case 0x1: U64x2_1 = value; break;
                    default: throw new ArgumentOutOfRangeException($"Cannot get i64 index {index} of MV128");
                }
            }
        }
        
        public double this[double index]
        {
            get => index switch
            {
                0.0 => F64x2_0,
                1.0 => F64x2_1,
                _ => throw new ArgumentOutOfRangeException($"Cannot get double index {index} of MV128")
            };
            set {
                switch (index)
                {
                    case 0.0: F64x2_0 = value; break;
                    case 1.0: F64x2_1 = value; break;
                    default: throw new ArgumentOutOfRangeException($"Cannot set double index {index} of MV128");
                }
            }
        }
        
        public static implicit operator V128(MV128 mv128) => 
            MemoryMarshal.Cast<MV128, V128>(MemoryMarshal.CreateSpan(ref mv128, 1))[0];
        public static implicit operator MV128(V128 v128) => 
            MemoryMarshal.Cast<V128, MV128>(MemoryMarshal.CreateSpan(ref v128, 1))[0];
        
    }
}