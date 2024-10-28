using System;
using Wacs.Core.Attributes;
using Wacs.Core.Types;

namespace Wacs.WASIp1.Types
{
    [Flags]
    [WasmType(nameof(ValType.I32))]
    public enum FstFlags : ushort
    {
        None      = 0b0000,
        ATim      = 0b0001,
        ATimNow   = 0b0010,
        MTim      = 0b0100,
        MTimNow   = 0b1000,
    }
}