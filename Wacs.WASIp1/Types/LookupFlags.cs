using System;
using Wacs.Core.Attributes;
using Wacs.Core.Types;

namespace Wacs.WASIp1.Types
{
    [Flags]
    [WasmType(nameof(ValType.I32))]
    public enum LookupFlags : uint
    {
        None = 0,
        SymlinkFollow = 1,
    }
}