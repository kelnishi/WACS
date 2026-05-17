// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime.Types;

namespace Wacs.ComponentModel.Harness
{
    /// <summary>
    /// Little-endian readers / writers over a wasm
    /// <see cref="MemoryInstance"/>'s backing <c>byte[]</c>.
    /// The canonical ABI mandates LE on every numeric width;
    /// these methods own that detail so emitted harness IL
    /// (and hand-written harnesses) can express a memory access
    /// without inlining the bit-twiddle. Fully AOT-safe — no
    /// reflection, no allocation, callable from IL2CPP-transpiled
    /// code.
    /// </summary>
    public static class MemoryHelpers
    {
        public static int ReadI32LE(MemoryInstance memory, int offset)
        {
            var data = memory.Data;
            return data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24);
        }

        public static void WriteI32LE(MemoryInstance memory, int offset, int value)
        {
            var data = memory.Data;
            data[offset]     = unchecked((byte)value);
            data[offset + 1] = unchecked((byte)(value >> 8));
            data[offset + 2] = unchecked((byte)(value >> 16));
            data[offset + 3] = unchecked((byte)(value >> 24));
        }

        public static byte ReadU8(MemoryInstance memory, int offset)
        {
            return memory.Data[offset];
        }

        public static void WriteU8(MemoryInstance memory, int offset, byte value)
        {
            memory.Data[offset] = value;
        }
    }
}
