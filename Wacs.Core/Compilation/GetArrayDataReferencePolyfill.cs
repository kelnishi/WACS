// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Small forwarder so the source-generated dispatcher can emit a single call
    /// site that works on both target frameworks. <c>MemoryMarshal.GetArrayDataReference</c>
    /// is net5.0+; on netstandard2.1 we fall back to
    /// <c>MemoryMarshal.GetReference((ReadOnlySpan&lt;byte&gt;)array)</c> — zero-alloc
    /// because <c>ReadOnlySpan&lt;byte&gt;</c> is a ref struct (the implicit conversion
    /// from <c>byte[]</c> creates a struct-value on the stack, not a heap alloc).
    /// Both forms lower to a single <c>ldr</c> on ARM64.
    /// </summary>
    internal static class ArrayRefHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref byte GetByteRef(byte[] array)
        {
#if NET5_0_OR_GREATER
            return ref MemoryMarshal.GetArrayDataReference(array);
#else
            return ref MemoryMarshal.GetReference((System.ReadOnlySpan<byte>)array);
#endif
        }
    }
}
