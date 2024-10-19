using System.Runtime.InteropServices;

namespace Wacs.Core.OpCodes
{
    [StructLayout(LayoutKind.Explicit)]
    public struct ByteCode
    {
        [FieldOffset(0)] public readonly OpCode   ZZ;
        [FieldOffset(1)] public readonly GcCode      FB;
        [FieldOffset(1)] public readonly ExtCode     FC;
        [FieldOffset(1)] public readonly SimdCode    FD;
        [FieldOffset(1)] public readonly ThreadsCode FE;
    }
}