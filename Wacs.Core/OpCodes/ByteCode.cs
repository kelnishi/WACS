using System;
using System.Runtime.InteropServices;

namespace Wacs.Core.OpCodes
{
    [StructLayout(LayoutKind.Explicit)]
    public struct ByteCode
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
    }
}