// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;

namespace Wacs.WASI.Preview2.HostBinding.CanonicalAbi
{
    /// <summary>
    /// Convenience methods on <see cref="ExecContext"/> for
    /// canonical-ABI marshaling. Each one is a thin pass-through
    /// to <see cref="MemoryReader"/> / <see cref="MemoryWriter"/>
    /// that lets binding bodies write
    /// <c>ctx.ReadUtf8String(ptr, len)</c> instead of
    /// <c>MemoryReader.ReadUtf8String(ctx.DefaultMemory, ptr, len)</c>
    /// — keeps the per-syscall code dense and readable.
    ///
    /// <para>Phase C.4b: <see cref="Memory"/> returns the guest's
    /// default <see cref="MemoryInstance"/> rather than its
    /// <c>byte[] Data</c> field, so bindings using either
    /// <see cref="MemoryStorageMode.ManagedArray"/> or
    /// <see cref="MemoryStorageMode.NativePointer"/> see the right
    /// backing automatically. The previous return type
    /// (<c>byte[]</c>) silently broke in NativePointer mode where
    /// <c>Data</c> is the empty-array sentinel.</para>
    /// </summary>
    public static class ExecContextExtensions
    {
        /// <summary>The guest's default linear memory. Helpers
        /// dispatch reads/writes through it; mode-aware in both
        /// ManagedArray and NativePointer modes.</summary>
        public static MemoryInstance Memory(this ExecContext ctx)
            => ctx.DefaultMemory;

        public static string ReadUtf8String(this ExecContext ctx,
            int ptr, int len)
            => MemoryReader.ReadUtf8String(ctx.DefaultMemory,
                ptr, len);

        public static byte[] ReadByteArray(this ExecContext ctx,
            int ptr, int len)
            => MemoryReader.ReadByteArray(ctx.DefaultMemory,
                ptr, len);

        public static byte[][] ReadByteArrayList(this ExecContext ctx,
            int listPtr, int listLen)
            => MemoryReader.ReadByteArrayList(ctx.DefaultMemory,
                listPtr, listLen);

        public static int ReadI32LE(this ExecContext ctx, int ptr)
            => MemoryReader.ReadI32LE(ctx.DefaultMemory, ptr);

        public static void WriteI32LE(this ExecContext ctx,
            int ptr, int value)
            => MemoryWriter.WriteI32LE(ctx.DefaultMemory, ptr, value);

        public static void WriteByte(this ExecContext ctx,
            int ptr, byte value)
            => ctx.DefaultMemory.AsSpan(ptr, 1)[0] = value;

        public static byte ReadByte(this ExecContext ctx, int ptr)
            => ctx.DefaultMemory.AsSpan(ptr, 1)[0];
    }
}
