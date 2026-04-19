// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Runtime.InteropServices;
using Wacs.Core.Runtime;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// 16-byte union used by the generated dispatcher as a shared immediate slot.
    /// Every dispatch case that reads instruction immediates writes into one of a
    /// small fixed bank of ImmUnion slots declared at <c>TryDispatch</c> entry —
    /// eliminating the per-case named locals that were bloating the method frame
    /// (see README-RegisterBankDispatch.md for the motivation).
    ///
    /// The explicit layout puts every numeric-sized immediate kind at
    /// <see cref="FieldOffsetAttribute">offset 0</see>, overlapping with the widest
    /// element (V128). RyuJIT treats this as a single stack slot and only has to
    /// reason about alignment once.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct ImmUnion
    {
        [FieldOffset(0)] public int    S32;
        [FieldOffset(0)] public uint   U32;
        [FieldOffset(0)] public long   S64;
        [FieldOffset(0)] public ulong  U64;
        [FieldOffset(0)] public float  F32;
        [FieldOffset(0)] public double F64;
        [FieldOffset(0)] public byte   U8;
        [FieldOffset(0)] public V128   V128;
    }
}
