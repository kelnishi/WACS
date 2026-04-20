// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Wacs.Core.Runtime.GC;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Runtime
{
    /// <summary>
    /// Compact operand-stack prototype. Stores scalar operands in a
    /// <see cref="ulong"/>[] (8 B per slot) with sidecar arrays for
    /// reference payloads and type tags. Hot-path ops (i32/i64/f32/f64
    /// arithmetic, local.get/set, i32/i64/f32/f64 const) only touch the
    /// 8-byte scalar array — ~4× less memory traffic per push/pop vs
    /// the full <see cref="Value"/> struct, and 4× denser cache packing.
    ///
    /// <para>Ref-valued ops (funcref / externref / structref / arrayref)
    /// additionally touch <see cref="_refs"/>. Validation guarantees
    /// per-op type correctness so per-slot type tags are only consulted
    /// at the slow-path <see cref="PopAny"/> boundary; <see cref="_types"/>
    /// is written on every push but never read in the hot path.</para>
    ///
    /// <para>API-compatible with <see cref="OpStack"/> at the method level
    /// so the switch-runtime generator can retarget its inline emission
    /// between the two with a flag. Not yet wired into the polymorphic
    /// runtime — that path still uses the full-Value stack.</para>
    /// </summary>
    public sealed class CompactOpStack
    {
        // Keep field names parallel to OpStack._registers / .Count so the
        // generator's emission templates can swap between them with
        // a one-line change.
        internal readonly ulong[] _slots;
        internal readonly IGcRef?[] _refs;
        internal readonly int[] _types;   // ValType raw int — never read on the hot path
        private readonly int _stackLimit;
        public int Count;

        public CompactOpStack(int limit)
        {
            _stackLimit = limit;
            _slots = new ulong[limit];
            _refs = new IGcRef?[limit];
            _types = new int[limit];
            Count = 0;
        }

        /// <summary>
        /// Fast-path ref into the slot array, mirroring
        /// <see cref="OpStack.FirstRegister"/>. Used by the generator's
        /// inline pop/push emission via <c>Unsafe.Add(ref _slotsRef, i)</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref ulong FirstSlot() => ref _slots[0];

        // netstandard2.1 polyfills for the .NET 6+ bit-conversion APIs.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float BitsToFloat(ulong bits)
        {
            uint u = unchecked((uint)bits);
            return Unsafe.As<uint, float>(ref u);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double BitsToDouble(ulong bits)
        {
            return Unsafe.As<ulong, double>(ref bits);
        }

        // =================================================================
        // Scalar push/pop — 8-byte slot traffic only. _types sidecar is
        // written but not read in hot paths.
        // =================================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushI32(int value)
        {
            _slots[Count] = unchecked((uint)value);
            _types[Count] = (int)ValType.I32;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushU32(uint value)
        {
            _slots[Count] = value;
            _types[Count] = (int)ValType.I32;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushI64(long value)
        {
            _slots[Count] = unchecked((ulong)value);
            _types[Count] = (int)ValType.I64;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushU64(ulong value)
        {
            _slots[Count] = value;
            _types[Count] = (int)ValType.I64;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushF32(float value)
        {
            _slots[Count] = Unsafe.As<float, uint>(ref value);
            _types[Count] = (int)ValType.F32;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushF64(double value)
        {
            _slots[Count] = Unsafe.As<double, ulong>(ref value);
            _types[Count] = (int)ValType.F64;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int PopI32()
        {
            --Count;
            return unchecked((int)(uint)_slots[Count]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint PopU32()
        {
            --Count;
            return unchecked((uint)_slots[Count]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long PopI64()
        {
            --Count;
            return unchecked((long)_slots[Count]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong PopU64()
        {
            --Count;
            return _slots[Count];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float PopF32()
        {
            --Count;
            return BitsToFloat(_slots[Count]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double PopF64()
        {
            --Count;
            return BitsToDouble(_slots[Count]);
        }

        // =================================================================
        // Ref-typed push/pop — touches the sidecar _refs array. Still a
        // single cache line (refs array lives separately from slots).
        // =================================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushRef(Value value)
        {
            _slots[Count] = unchecked((ulong)value.Data.Ptr);
            _refs[Count] = value.GcRef;
            _types[Count] = (int)value.Type;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushValue(Value value)
        {
            _slots[Count] = unchecked((ulong)value.Data.Int64);
            _refs[Count] = value.GcRef;
            _types[Count] = (int)value.Type;
            Count++;
        }

        public Value PopAny()
        {
            --Count;
            var v = default(Value);
            v.Type = (ValType)_types[Count];
            v.Data.Int64 = unchecked((long)_slots[Count]);
            v.GcRef = _refs[Count];
            _refs[Count] = null; // drop managed ref for GC
            return v;
        }

        public Value Peek()
        {
            var v = default(Value);
            v.Type = (ValType)_types[Count - 1];
            v.Data.Int64 = unchecked((long)_slots[Count - 1]);
            v.GcRef = _refs[Count - 1];
            return v;
        }

        public void Clear()
        {
            for (int i = 0; i < Count; i++) _refs[i] = null;
            Count = 0;
        }

        // =================================================================
        // "Fast" aliases used by the generator's inline emission — same
        // semantics as Push/Pop for this prototype; kept separate so the
        // generator template can target them identically to OpStack.
        // =================================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushI32Fast(int value)
        {
            _slots[Count] = unchecked((uint)value);
            _types[Count] = (int)ValType.I32;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushU32Fast(uint value)
        {
            _slots[Count] = value;
            _types[Count] = (int)ValType.I32;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushI64Fast(long value)
        {
            _slots[Count] = unchecked((ulong)value);
            _types[Count] = (int)ValType.I64;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushU64Fast(ulong value)
        {
            _slots[Count] = value;
            _types[Count] = (int)ValType.I64;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushF32Fast(float value)
        {
            _slots[Count] = Unsafe.As<float, uint>(ref value);
            _types[Count] = (int)ValType.F32;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushF64Fast(double value)
        {
            _slots[Count] = Unsafe.As<double, ulong>(ref value);
            _types[Count] = (int)ValType.F64;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushValueFast(Value value)
        {
            _slots[Count] = unchecked((ulong)value.Data.Int64);
            _refs[Count] = value.GcRef;
            _types[Count] = (int)value.Type;
            Count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int PopI32Fast() { --Count; return unchecked((int)(uint)_slots[Count]); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint PopU32Fast() { --Count; return unchecked((uint)_slots[Count]); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long PopI64Fast() { --Count; return unchecked((long)_slots[Count]); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong PopU64Fast() { --Count; return _slots[Count]; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float PopF32Fast() { --Count; return BitsToFloat(_slots[Count]); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double PopF64Fast() { --Count; return BitsToDouble(_slots[Count]); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Value PopAnyFast()
        {
            --Count;
            var v = default(Value);
            v.Type = (ValType)_types[Count];
            v.Data.Int64 = unchecked((long)_slots[Count]);
            v.GcRef = _refs[Count];
            _refs[Count] = null;
            return v;
        }
    }
}
