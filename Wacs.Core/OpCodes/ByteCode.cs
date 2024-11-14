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
using System.Runtime.InteropServices;

namespace Wacs.Core.OpCodes
{
    [StructLayout(LayoutKind.Explicit)]
    public struct ByteCode : IComparable<ByteCode>
    {
        [FieldOffset(0)] public readonly OpCode   x00;
        [FieldOffset(1)] public readonly GcCode       xFB;
        [FieldOffset(1)] public readonly ExtCode      xFC;
        [FieldOffset(1)] public readonly SimdCode     xFD;
        [FieldOffset(1)] public readonly AtomCode     xFE;

        public ByteCode(OpCode b)
        {
            this = default;
            x00 = b;
        }

        public ByteCode(GcCode b)
        {
            this = default;
            x00 = OpCode.FB;
            xFB = b;
        }
        
        public ByteCode(ExtCode b)
        {
            this = default;
            x00 = OpCode.FC;
            xFC = b;
        }
        
        public ByteCode(SimdCode b)
        {
            this = default;
            x00 = OpCode.FD;
            xFD = b;
        }
        
        public ByteCode(AtomCode b)
        {
            this = default;
            x00 = OpCode.FE;
            xFE = b;
        }

        public static explicit operator ByteCode(ushort bytes) =>
            (byte)(bytes >> 8) switch
            {
                0xFB => new ByteCode((GcCode)(byte)(bytes & 0xFF)),
                0xFC => new ByteCode((ExtCode)(byte)(bytes & 0xFF)),
                0xFD => new ByteCode((SimdCode)(byte)(bytes & 0xFF)),
                0xFE => new ByteCode((AtomCode)(byte)(bytes & 0xFF)),
                _ => new ByteCode((OpCode)(byte)(bytes >> 8)),
            };

        public static explicit operator ushort(ByteCode byteCode) =>
            byteCode.x00 switch
            {
                OpCode.FB => (ushort)((byte)byteCode.x00 << 8 | (byte)byteCode.xFB),
                OpCode.FC => (ushort)((byte)byteCode.x00 << 8 | (byte)byteCode.xFC),
                OpCode.FD => (ushort)((byte)byteCode.x00 << 8 | (byte)byteCode.xFD),
                OpCode.FE => (ushort)((byte)byteCode.x00 << 8 | (byte)byteCode.xFE),
                _ => (ushort)((byte)byteCode.x00 << 8),
            };

        public static implicit operator OpCode(ByteCode byteCode) => byteCode.x00;
        public static implicit operator ByteCode(OpCode b) => new ByteCode(b);
        public static implicit operator ByteCode(GcCode b) => new ByteCode(b);
        public static implicit operator ByteCode(ExtCode b) => new ByteCode(b);
        public static implicit operator ByteCode(SimdCode b) => new ByteCode(b);
        public static implicit operator ByteCode(AtomCode b) => new ByteCode(b);
        
        public override bool Equals(object obj) =>
            obj is ByteCode other && x00 == other.x00 && x00 switch {
                OpCode.FB => xFB.Equals(other.xFB),
                OpCode.FC => xFC.Equals(other.xFC),
                OpCode.FD => xFD.Equals(other.xFD),
                OpCode.FE => xFE.Equals(other.xFE),
                _ => true
            };

        public override int GetHashCode() =>
            x00 switch {
                OpCode.FB => HashCode.Combine(x00,xFB),
                OpCode.FC => HashCode.Combine(x00,xFC),
                OpCode.FD => HashCode.Combine(x00,xFD),
                OpCode.FE => HashCode.Combine(x00,xFE),
                _ => HashCode.Combine(x00)
            };
        public static bool operator ==(ByteCode left, ByteCode right) => left.Equals(right);
        public static bool operator !=(ByteCode left, ByteCode right) => !(left == right);

        public int CompareTo(ByteCode other)
        {
            var x00Comparison = x00.CompareTo(other.x00);
            if (x00Comparison != 0) return x00Comparison;
            switch (x00)
            {
                case OpCode.FB: return xFB.CompareTo(other.xFB);
                case OpCode.FC: return xFC.CompareTo(other.xFC);
                case OpCode.FD: return xFD.CompareTo(other.xFD);
                case OpCode.FE: return xFE.CompareTo(other.xFE);
                default: return 0;
            }
        }
        
        public override string ToString() => x00 switch
        {
            OpCode.FB => $"(GC){xFB}",
            OpCode.FC => $"(Ext){xFC}",
            OpCode.FD => $"(SIMD){xFD}",
            OpCode.FE => $"(Threads){xFE}",
            _ => $"{x00}"
        };
        
    }
}