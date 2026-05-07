// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime;

namespace Wacs.WASI.NN.HostBinding
{
    /// <summary>
    /// Local canonical-ABI helpers — same shape as
    /// <c>Wacs.WASI.Preview2.HostBinding.CanonicalAbi.ExecContextExtensions</c>,
    /// inlined here so this package doesn't reference Preview2.
    /// wasi-nn is independent of the WASI 0.2.x interface tree;
    /// embedders can wire one or the other or both without
    /// dragging in unrelated transitives.
    /// </summary>
    internal static class ExecContextExtensions
    {
        public static byte[] Memory(this ExecContext ctx)
            => ctx.DefaultMemory.Data;

        public static int ReadI32LE(this ExecContext ctx, int ptr)
        {
            var mem = ctx.DefaultMemory.Data;
            return mem[ptr] | (mem[ptr + 1] << 8) |
                   (mem[ptr + 2] << 16) | (mem[ptr + 3] << 24);
        }

        public static void WriteI32LE(this ExecContext ctx, int ptr, int value)
        {
            var mem = ctx.DefaultMemory.Data;
            mem[ptr]     = (byte)value;
            mem[ptr + 1] = (byte)(value >> 8);
            mem[ptr + 2] = (byte)(value >> 16);
            mem[ptr + 3] = (byte)(value >> 24);
        }
    }
}
