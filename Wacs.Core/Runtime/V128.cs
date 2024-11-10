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

        [FieldOffset(0x0)] public readonly byte B8x16_0;
        [FieldOffset(0x1)] public readonly byte B8x16_1;
        [FieldOffset(0x2)] public readonly byte B8x16_2;
        [FieldOffset(0x3)] public readonly byte B8x16_3;
        [FieldOffset(0x4)] public readonly byte B8x16_4;
        [FieldOffset(0x5)] public readonly byte B8x16_5;
        [FieldOffset(0x6)] public readonly byte B8x16_6;
        [FieldOffset(0x7)] public readonly byte B8x16_7;
        [FieldOffset(0x8)] public readonly byte B8x16_8;
        [FieldOffset(0x9)] public readonly byte B8x16_9;
        [FieldOffset(0xA)] public readonly byte B8x16_A;
        [FieldOffset(0xB)] public readonly byte B8x16_B;
        [FieldOffset(0xC)] public readonly byte B8x16_C;
        [FieldOffset(0xD)] public readonly byte B8x16_D;
        [FieldOffset(0xE)] public readonly byte B8x16_E;
        [FieldOffset(0xF)] public readonly byte B8x16_F;

        [FieldOffset(0x0)] public readonly ushort B16x8_0;
        [FieldOffset(0x2)] public readonly ushort B16x8_1;
        [FieldOffset(0x4)] public readonly ushort B16x8_2;
        [FieldOffset(0x6)] public readonly ushort B16x8_3;
        [FieldOffset(0x8)] public readonly ushort B16x8_4;
        [FieldOffset(0xA)] public readonly ushort B16x8_5;
        [FieldOffset(0xC)] public readonly ushort B16x8_6;
        [FieldOffset(0xE)] public readonly ushort B16x8_7;

        [FieldOffset(0x0)] public readonly uint B32x4_0;
        [FieldOffset(0x4)] public readonly uint B32x4_1;
        [FieldOffset(0x8)] public readonly uint B32x4_2;
        [FieldOffset(0xC)] public readonly uint B32x4_3;

        [FieldOffset(0x0)] public readonly ulong B64x2_0;
        [FieldOffset(0x8)] public readonly ulong B64x2_1;

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
            byte b8x16_0, byte b8x16_1, byte b8x16_2, byte b8x16_3, 
            byte b8x16_4, byte b8x16_5, byte b8x16_6, byte b8x16_7, 
            byte b8x16_8, byte b8x16_9, byte b8X16_A, byte b8X16_B,
            byte b8X16_C, byte b8X16_D, byte b8X16_E, byte b8X16_F)
        {
            this = default;
            B8x16_0 = b8x16_0;
            B8x16_1 = b8x16_1;
            B8x16_2 = b8x16_2;
            B8x16_3 = b8x16_3;
            B8x16_4 = b8x16_4;
            B8x16_5 = b8x16_5;
            B8x16_6 = b8x16_6;
            B8x16_7 = b8x16_7;
            B8x16_8 = b8x16_8;
            B8x16_9 = b8x16_9;
            B8x16_A = b8X16_A;
            B8x16_B = b8X16_B;
            B8x16_C = b8X16_C;
            B8x16_D = b8X16_D;
            B8x16_E = b8X16_E;
            B8x16_F = b8X16_F;
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
            ushort b16x8_0, ushort b16x8_1, ushort b16x8_2, ushort b16x8_3, 
            ushort b16x8_4, ushort b16x8_5, ushort b16x8_6, ushort b16x8_7)
        {
            this = default;
            B16x8_0 = b16x8_0;
            B16x8_1 = b16x8_1;
            B16x8_2 = b16x8_2;
            B16x8_3 = b16x8_3;
            B16x8_4 = b16x8_4;
            B16x8_5 = b16x8_5;
            B16x8_6 = b16x8_6;
            B16x8_7 = b16x8_7;
        }
        public static implicit operator V128((
            ushort, ushort, ushort, ushort, 
            ushort, ushort, ushort, ushort) tuple) => 
            new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4,
                tuple.Item5, tuple.Item6, tuple.Item7, tuple.Item8);
        
        public V128(uint b32x4_0, uint b32x4_1, uint b32x4_2, uint b32x4_3)
        {
            this = default;
            B32x4_0 = b32x4_0;
            B32x4_1 = b32x4_1;
            B32x4_2 = b32x4_2;
            B32x4_3 = b32x4_3;
        }
        public static implicit operator V128((uint, uint, uint, uint) tuple) =>
            new(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);

        public V128(ulong b64x2_0, ulong b64x2_1)
        {
            this = default;
            B64x2_0 = b64x2_0;
            B64x2_1 = b64x2_1;
        }
        public static implicit operator V128((ulong, ulong) tuple) => 
            new(tuple.Item1, tuple.Item2);

        public V128(Span<byte> data)
        {
            if (data.Length != 16)
                throw new InvalidDataException($"Cannot create V128 from {data.Length} bytes");
            
            this = default;
            B8x16_0 = data[0x0];
            B8x16_1 = data[0x1];
            B8x16_2 = data[0x2];
            B8x16_3 = data[0x3];
            B8x16_4 = data[0x4];
            B8x16_5 = data[0x5];
            B8x16_6 = data[0x6];
            B8x16_7 = data[0x7];
            B8x16_8 = data[0x8];
            B8x16_9 = data[0x9];
            B8x16_A = data[0xA];
            B8x16_B = data[0xB];
            B8x16_C = data[0xC];
            B8x16_D = data[0xD];
            B8x16_E = data[0xE];
            B8x16_F = data[0xF];
        }
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

        [FieldOffset(0x0)] public byte B8x16_0;
        [FieldOffset(0x1)] public byte B8x16_1;
        [FieldOffset(0x2)] public byte B8x16_2;
        [FieldOffset(0x3)] public byte B8x16_3;
        [FieldOffset(0x4)] public byte B8x16_4;
        [FieldOffset(0x5)] public byte B8x16_5;
        [FieldOffset(0x6)] public byte B8x16_6;
        [FieldOffset(0x7)] public byte B8x16_7;
        [FieldOffset(0x8)] public byte B8x16_8;
        [FieldOffset(0x9)] public byte B8x16_9;
        [FieldOffset(0xA)] public byte B8x16_A;
        [FieldOffset(0xB)] public byte B8x16_B;
        [FieldOffset(0xC)] public byte B8x16_C;
        [FieldOffset(0xD)] public byte B8x16_D;
        [FieldOffset(0xE)] public byte B8x16_E;
        [FieldOffset(0xF)] public byte B8x16_F;

        [FieldOffset(0x0)] public ushort B16x8_0;
        [FieldOffset(0x2)] public ushort B16x8_1;
        [FieldOffset(0x4)] public ushort B16x8_2;
        [FieldOffset(0x6)] public ushort B16x8_3;
        [FieldOffset(0x8)] public ushort B16x8_4;
        [FieldOffset(0xA)] public ushort B16x8_5;
        [FieldOffset(0xC)] public ushort B16x8_6;
        [FieldOffset(0xE)] public ushort B16x8_7;

        [FieldOffset(0x0)] public uint B32x4_0;
        [FieldOffset(0x4)] public uint B32x4_1;
        [FieldOffset(0x8)] public uint B32x4_2;
        [FieldOffset(0xC)] public uint B32x4_3;

        [FieldOffset(0x0)] public ulong B64x2_0;
        [FieldOffset(0x8)] public ulong B64x2_1;

        public byte this[byte index]
        {
            get => index switch {
                    0x0 => B8x16_0,
                    0x1 => B8x16_1,
                    0x2 => B8x16_2,
                    0x3 => B8x16_3,
                    0x4 => B8x16_4,
                    0x5 => B8x16_5,
                    0x6 => B8x16_6,
                    0x7 => B8x16_7,
                    0x8 => B8x16_8,
                    0x9 => B8x16_9,
                    0xA => B8x16_A,
                    0xB => B8x16_B,
                    0xC => B8x16_C,
                    0xD => B8x16_D,
                    0xE => B8x16_E,
                    0xF => B8x16_F,
                    _ => throw new ArgumentOutOfRangeException($"Cannot get byte index {index} of MV128")
                };
            set {
                switch (index)
                {
                    case 0x0: B8x16_0 = value; break;
                    case 0x1: B8x16_1 = value; break;
                    case 0x2: B8x16_2 = value; break;
                    case 0x3: B8x16_3 = value; break;
                    case 0x4: B8x16_4 = value; break;
                    case 0x5: B8x16_5 = value; break;
                    case 0x6: B8x16_6 = value; break;
                    case 0x7: B8x16_7 = value; break;
                    case 0x8: B8x16_8 = value; break;
                    case 0x9: B8x16_9 = value; break;
                    case 0xA: B8x16_A = value; break;
                    case 0xB: B8x16_B = value; break;
                    case 0xC: B8x16_C = value; break;
                    case 0xD: B8x16_D = value; break;
                    case 0xE: B8x16_E = value; break;
                    case 0xF: B8x16_F = value; break;
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
        
        public long this[long index]
        {
            get => index switch {
                0x0 => I64x2_0,
                0x1 => I64x2_1,
                _ => throw new ArgumentOutOfRangeException($"Cannot get i32 index {index} of MV128")
            };
            set {
                switch (index)
                {
                    case 0x0: I64x2_0 = value; break;
                    case 0x1: I64x2_1 = value; break;
                    default: throw new ArgumentOutOfRangeException($"Cannot get i32 index {index} of MV128");
                }
            }
        }
        
        public static implicit operator V128(MV128 mv128) => 
            MemoryMarshal.Cast<MV128, V128>(MemoryMarshal.CreateSpan(ref mv128, 1))[0];
        public static implicit operator MV128(V128 v128) => 
            MemoryMarshal.Cast<V128, MV128>(MemoryMarshal.CreateSpan(ref v128, 1))[0];
        
    }
}