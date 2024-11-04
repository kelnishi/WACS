using System.Runtime.InteropServices;

namespace Wacs.WASIp1.Types
{
    public enum PrestatTag : byte
    {
        Dir = 0,
        NotDir = 1,
    }

    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct PrestatDir
    {
        [FieldOffset(0)] public uint NameLen;
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct Prestat
    {
        [FieldOffset(0)] public PrestatTag Tag;

        [FieldOffset(4)] public PrestatDir Dir;
    }
}