using System.Runtime.InteropServices;

namespace Wacs.WASIp1.Types
{
    /// <summary>
    /// A region of memory for scatter/gather reads.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct IoVec
    {
        [FieldOffset(0)]
        public uint bufPtr;
        
        [FieldOffset(4)]
        public uint bufLen;
    }
    
    /// <summary>
    /// A region of memory for scatter/gather writes.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct CIoVec
    {
        [FieldOffset(0)]
        public uint bufPtr;
        
        [FieldOffset(4)]
        public uint bufLen;
    }
}