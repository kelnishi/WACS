using System;

namespace Wacs.WASIp1.Types
{
    [Flags]
    public enum FstFlags : ushort
    {
        None      = 0b0000,
        ATim      = 0b0001,
        ATimNow   = 0b0010,
        MTim      = 0b0100,
        MTimNow   = 0b1000,
    }
}