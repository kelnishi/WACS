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
            x00 = OpCode.FB;
            xFC = b;
        }
        
        public ByteCode(SimdCode b)
        {
            this = default;
            x00 = OpCode.FB;
            xFD = b;
        }
        
        public ByteCode(AtomCode b)
        {
            this = default;
            x00 = OpCode.FB;
            xFE = b;
        }
        
        public static implicit operator OpCode(ByteCode byteCode) => byteCode.x00;
        public static implicit operator ByteCode(OpCode b) => new ByteCode(b);
        public static implicit operator ByteCode(GcCode b) => new ByteCode(b);
        public static implicit operator ByteCode(ExtCode b) => new ByteCode(b);
        public static implicit operator ByteCode(SimdCode b) => new ByteCode(b);
        public static implicit operator ByteCode(AtomCode b) => new ByteCode(b);
        
    }
}