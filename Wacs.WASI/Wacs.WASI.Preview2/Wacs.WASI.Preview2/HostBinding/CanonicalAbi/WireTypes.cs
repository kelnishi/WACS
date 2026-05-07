// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview2.HostBinding.CanonicalAbi
{
    /// <summary>
    /// Wire-type sizing + alignment primitives for the canonical
    /// ABI. Pure functions, no state. Bindings use these to lay
    /// out retArea slots and decode primitive params.
    /// </summary>
    public static class WireTypes
    {
        /// <summary>Byte size of a primitive — drives both
        /// alignment (start at <see cref="AlignUp"/>(offset, size))
        /// and write width (size bytes per field). Mirrors the
        /// canonical-ABI alignment rule "alignment(P) = sizeof(P)"
        /// for primitives.</summary>
        public static int PrimitiveByteSize(Type t)
        {
            if (t == typeof(bool) || t == typeof(byte)
                || t == typeof(sbyte)) return 1;
            if (t == typeof(short) || t == typeof(ushort)) return 2;
            if (t == typeof(int) || t == typeof(uint)
                || t == typeof(float)) return 4;
            if (t == typeof(long) || t == typeof(ulong)
                || t == typeof(double)) return 8;
            throw new NotSupportedException(
                "PrimitiveByteSize for " + t + " is unsupported.");
        }

        /// <summary>Round <paramref name="offset"/> up to the
        /// next multiple of <paramref name="alignment"/>.</summary>
        public static int AlignUp(int offset, int alignment)
        {
            var rem = offset % alignment;
            return rem == 0 ? offset : offset + (alignment - rem);
        }

        /// <summary>True iff <paramref name="t"/> is one of the
        /// primitive types the canonical ABI supports as a flat
        /// wire slot — bool, the eight integer widths, and the
        /// two float widths.</summary>
        public static bool IsPrimitive(Type t)
        {
            return t == typeof(bool)
                || t == typeof(sbyte) || t == typeof(byte)
                || t == typeof(short) || t == typeof(ushort)
                || t == typeof(int) || t == typeof(uint)
                || t == typeof(long) || t == typeof(ulong)
                || t == typeof(float) || t == typeof(double);
        }
    }
}
