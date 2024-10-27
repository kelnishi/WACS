using System.Runtime.InteropServices;

namespace Wacs.WASIp1.Types
{
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct FdStat
    {
        [FieldOffset(0)] public Filetype Filetype;
        [FieldOffset(2)] public FdFlags Flags;
        [FieldOffset(8)] public Rights RightsBase;
        [FieldOffset(16)] public Rights RightsInheriting;
    }
}