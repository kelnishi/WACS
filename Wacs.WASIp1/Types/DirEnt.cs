using System.Runtime.InteropServices;
using dircookie = System.UInt64;
using inode = System.UInt64;
using dirnamlen = System.UInt32;


namespace Wacs.WASIp1.Types
{
    [StructLayout(LayoutKind.Explicit)]
    public struct DirEnt
    {
        [FieldOffset(0)] public dircookie DNext;
        [FieldOffset(8)] public inode DIno;
        [FieldOffset(16)] public dirnamlen DNamlen;
        [FieldOffset(24)] public Filetype DType;
    }
}