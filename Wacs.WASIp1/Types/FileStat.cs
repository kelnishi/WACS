using System.Runtime.InteropServices;
using device = System.UInt64;
using inode = System.UInt64;
using linkcount = System.UInt64;
using filesize = System.UInt64;
using timestamp = System.UInt64;

namespace Wacs.WASIp1.Types
{
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct FileStat
    {
        [FieldOffset(0)] public device Device;
        [FieldOffset(8)] public inode Ino;
        [FieldOffset(16)] public Filetype Mode;
        [FieldOffset(24)] public linkcount NLink;
        [FieldOffset(32)] public filesize Size;
        [FieldOffset(40)] public timestamp ATim;
        [FieldOffset(48)] public timestamp MTim;
        [FieldOffset(56)] public timestamp CTim;
    }
}