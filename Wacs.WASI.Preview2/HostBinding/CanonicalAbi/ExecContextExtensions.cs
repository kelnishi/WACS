// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime;

namespace Wacs.WASI.Preview2.HostBinding.CanonicalAbi
{
    /// <summary>
    /// Convenience methods on <see cref="ExecContext"/> for
    /// canonical-ABI marshaling. Each one is a thin pass-through
    /// to <see cref="MemoryReader"/> / <see cref="MemoryWriter"/>
    /// that lets binding bodies write
    /// <c>ctx.ReadUtf8String(ptr, len)</c> instead of
    /// <c>MemoryReader.ReadUtf8String(ctx.DefaultMemory.Data,
    /// ptr, len)</c> — keeps the per-syscall code dense and
    /// readable.
    /// </summary>
    internal static class ExecContextExtensions
    {
        /// <summary>The guest's default linear memory backing
        /// array. Most canonical-ABI helpers take it directly;
        /// this just shortens the access.</summary>
        public static byte[] Memory(this ExecContext ctx)
            => ctx.DefaultMemory.Data;

        public static string ReadUtf8String(this ExecContext ctx,
            int ptr, int len)
            => MemoryReader.ReadUtf8String(ctx.DefaultMemory.Data,
                ptr, len);

        public static byte[] ReadByteArray(this ExecContext ctx,
            int ptr, int len)
            => MemoryReader.ReadByteArray(ctx.DefaultMemory.Data,
                ptr, len);

        public static byte[][] ReadByteArrayList(this ExecContext ctx,
            int listPtr, int listLen)
            => MemoryReader.ReadByteArrayList(ctx.DefaultMemory.Data,
                listPtr, listLen);

        public static int ReadI32LE(this ExecContext ctx, int ptr)
            => MemoryReader.ReadI32LE(ctx.DefaultMemory.Data, ptr);

        public static void WriteI32LE(this ExecContext ctx,
            int ptr, int value)
            => MemoryWriter.WriteI32LE(ctx.DefaultMemory.Data, ptr, value);

        public static void WriteByte(this ExecContext ctx,
            int ptr, byte value)
            => ctx.DefaultMemory.Data[ptr] = value;

        public static byte ReadByte(this ExecContext ctx, int ptr)
            => ctx.DefaultMemory.Data[ptr];
    }
}
