using System.Runtime.InteropServices;

namespace Wacs.WASIp1.Types
{
    public enum PrestatType : byte
    {
        Dir = 0,
    }

    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct PrestatDir
    {
        [FieldOffset(0)] public uint NameLen;
    }
    
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct Prestat
    {
        [FieldOffset(0)] public PrestatType Type;

        [FieldOffset(4)] public PrestatDir Dir;
    }
}