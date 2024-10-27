using System;

namespace Wacs.WASIp1.Types
{
    [Flags]
    public enum LookupFlags : uint
    {
        None = 0,
        SymlinkFollow = 1,
    }
}