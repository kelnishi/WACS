using System;

namespace Wacs.WASIp1.Types
{
    [Flags]
    public enum FdFlags : ushort
    {
        None      = 0b00000,
        Append    = 0b00001,
        DSync     = 0b00010,
        NonBlock  = 0b00100,
        RSync     = 0b01000,
        Sync      = 0b10000,
    }
}