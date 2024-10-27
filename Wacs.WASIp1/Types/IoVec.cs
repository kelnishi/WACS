using System.Runtime.InteropServices;

namespace Wacs.WASIp1.Types
{
    /// <summary>
    /// A region of memory for scatter/gather reads.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct IoVec
    {
        [FieldOffset(0)]
        private uint bufPtr;
        
        [FieldOffset(4)]
        private uint bufLen;
    }
    
    /// <summary>
    /// A region of memory for scatter/gather writes.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct CIoVec
    {
        [FieldOffset(0)]
        private uint bufPtr;
        
        [FieldOffset(4)]
        private uint bufLen;
    }
}