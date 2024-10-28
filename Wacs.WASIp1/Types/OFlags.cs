using System;
using System.IO;
using Wacs.Core.Attributes;
using Wacs.Core.Types;

namespace Wacs.WASIp1.Types
{
    [Flags]
    [WasmType(nameof(ValType.I32))]
    public enum OFlags : ushort
    {
        None      = 0b0000,
        Creat     = 0b0001,
        Directory = 0b0010,
        Excl      = 0b0100,
        Trunc     = 0b1000,
    }

    public static class OFlagsExtension
    {
        public static FileMode ToFileMode(this OFlags flags)
        {
            FileMode mode = FileMode.Open;
            if (flags.HasFlag(OFlags.Creat)) mode = FileMode.Create;
            if (flags.HasFlag(OFlags.Trunc)) mode = FileMode.Truncate;
            if (flags.HasFlag(OFlags.Excl)) mode = FileMode.OpenOrCreate;
            if (flags.HasFlag(OFlags.Directory)) mode = FileMode.CreateNew; // Assuming Directory creates a new file in the directory
            return mode;
        }
    }
}