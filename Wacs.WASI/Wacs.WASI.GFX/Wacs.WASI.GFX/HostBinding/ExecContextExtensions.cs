// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;

namespace Wacs.WASI.GFX.HostBinding
{
    /// <summary>
    /// Local canonical-ABI memory helpers — mirror of the
    /// WASI.NN package's helpers. Inlined here to keep this
    /// package free of NN / Preview2 transitives at the
    /// runtime memory-touch layer.
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

        public static long ReadI64LE(this ExecContext ctx, int ptr)
        {
            var mem = ctx.DefaultMemory.Data;
            return mem[ptr]
                | ((long)mem[ptr + 1] << 8)
                | ((long)mem[ptr + 2] << 16)
                | ((long)mem[ptr + 3] << 24)
                | ((long)mem[ptr + 4] << 32)
                | ((long)mem[ptr + 5] << 40)
                | ((long)mem[ptr + 6] << 48)
                | ((long)mem[ptr + 7] << 56);
        }

        public static double ReadF64LE(this ExecContext ctx, int ptr)
        {
            var mem = ctx.DefaultMemory.Data;
            ulong bits =
                (ulong)mem[ptr]
                | ((ulong)mem[ptr + 1] << 8)
                | ((ulong)mem[ptr + 2] << 16)
                | ((ulong)mem[ptr + 3] << 24)
                | ((ulong)mem[ptr + 4] << 32)
                | ((ulong)mem[ptr + 5] << 40)
                | ((ulong)mem[ptr + 6] << 48)
                | ((ulong)mem[ptr + 7] << 56);
            return BitConverter.Int64BitsToDouble((long)bits);
        }

        public static void WriteF64LE(this ExecContext ctx, int ptr, double value)
        {
            var mem = ctx.DefaultMemory.Data;
            long bits = BitConverter.DoubleToInt64Bits(value);
            mem[ptr]     = (byte)bits;
            mem[ptr + 1] = (byte)(bits >> 8);
            mem[ptr + 2] = (byte)(bits >> 16);
            mem[ptr + 3] = (byte)(bits >> 24);
            mem[ptr + 4] = (byte)(bits >> 32);
            mem[ptr + 5] = (byte)(bits >> 40);
            mem[ptr + 6] = (byte)(bits >> 48);
            mem[ptr + 7] = (byte)(bits >> 56);
        }
    }
}
