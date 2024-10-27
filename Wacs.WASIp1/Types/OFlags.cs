using System;

namespace Wacs.WASIp1.Types
{
    [Flags]
    public enum OFlags : ushort
    {
        None      = 0b0000,
        Creat     = 0b0001,
        Directory = 0b0010,
        Excl      = 0b0100,
        Trunc     = 0b1000,
    }
}