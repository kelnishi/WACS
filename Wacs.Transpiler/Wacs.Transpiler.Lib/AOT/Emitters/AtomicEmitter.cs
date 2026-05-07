// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using Wacs.Core.Instructions;
using Wacs.Core.Instructions.Atomic;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using AtomOp = Wacs.Core.OpCodes.AtomCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for WebAssembly threads-proposal atomic ops (0xFE prefix).
    ///
    /// All emit sites defer to <see cref="AtomicHelpers"/> for correctness, so the
    /// transpiled code shares semantics with the polymorphic interpreter and the
    /// switch runtime: bounds + exact-alignment checks trap identically, and
    /// RMW / cmpxchg / wait / notify go through the same
    /// <see cref="MemoryInstance"/> atomic helpers + <see cref="IConcurrencyPolicy"/>
    /// machinery phase 1 established.
    ///
    /// Stream shape per op (same as non-atomic loads/stores):
    /// &lt;memIdx:i32 literal&gt; &lt;offset:i64 literal&gt; — the align hint is
    /// validation-only.
    /// </summary>
    internal static class AtomicEmitter
    {
        private static readonly FieldInfo MemoriesField =
            typeof(ThinContext).GetField(nameof(ThinContext.Memories))!;
        private static readonly FieldInfo ExecContextField =
            typeof(ThinContext).GetField(nameof(ThinContext.ExecContext))!;

        /// <summary>
        /// All 47 AtomCode sub-ops are emittable. Nothing opts-out, so a
        /// function that used to fall back to the polymorphic interpreter
        /// solely because of an atomic now transpiles to native IL.
        /// </summary>
        public static bool CanEmit(AtomOp op) => true;

        public static void Emit(ILGenerator il, InstructionBase inst, AtomOp op)
        {
            switch (op)
            {
                // ---- Loads ----
                case AtomOp.I32AtomicLoad:     EmitLoad(il, inst, nameof(AtomicHelpers.LoadI32));   break;
                case AtomOp.I64AtomicLoad:     EmitLoad(il, inst, nameof(AtomicHelpers.LoadI64));   break;
                case AtomOp.I32AtomicLoad8U:   EmitLoad(il, inst, nameof(AtomicHelpers.LoadI32_8U));break;
                case AtomOp.I32AtomicLoad16U:  EmitLoad(il, inst, nameof(AtomicHelpers.LoadI32_16U));break;
                case AtomOp.I64AtomicLoad8U:   EmitLoad(il, inst, nameof(AtomicHelpers.LoadI64_8U));break;
                case AtomOp.I64AtomicLoad16U:  EmitLoad(il, inst, nameof(AtomicHelpers.LoadI64_16U));break;
                case AtomOp.I64AtomicLoad32U:  EmitLoad(il, inst, nameof(AtomicHelpers.LoadI64_32U));break;

                // ---- Stores ----
                case AtomOp.I32AtomicStore:    EmitStore(il, inst, nameof(AtomicHelpers.StoreI32));  break;
                case AtomOp.I64AtomicStore:    EmitStore(il, inst, nameof(AtomicHelpers.StoreI64));  break;
                case AtomOp.I32AtomicStore8:   EmitStore(il, inst, nameof(AtomicHelpers.StoreI32_8));break;
                case AtomOp.I32AtomicStore16:  EmitStore(il, inst, nameof(AtomicHelpers.StoreI32_16));break;
                case AtomOp.I64AtomicStore8:   EmitStore(il, inst, nameof(AtomicHelpers.StoreI64_8));break;
                case AtomOp.I64AtomicStore16:  EmitStore(il, inst, nameof(AtomicHelpers.StoreI64_16));break;
                case AtomOp.I64AtomicStore32:  EmitStore(il, inst, nameof(AtomicHelpers.StoreI64_32));break;

                // ---- RMW ----
                case AtomOp.I32AtomicRmwAdd:       EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_Add));    break;
                case AtomOp.I64AtomicRmwAdd:       EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_Add));    break;
                case AtomOp.I32AtomicRmw8AddU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_8_AddU));  break;
                case AtomOp.I32AtomicRmw16AddU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_16_AddU)); break;
                case AtomOp.I64AtomicRmw8AddU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_8_AddU));  break;
                case AtomOp.I64AtomicRmw16AddU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_16_AddU)); break;
                case AtomOp.I64AtomicRmw32AddU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_32_AddU)); break;

                case AtomOp.I32AtomicRmwSub:       EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_Sub));    break;
                case AtomOp.I64AtomicRmwSub:       EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_Sub));    break;
                case AtomOp.I32AtomicRmw8SubU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_8_SubU));  break;
                case AtomOp.I32AtomicRmw16SubU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_16_SubU)); break;
                case AtomOp.I64AtomicRmw8SubU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_8_SubU));  break;
                case AtomOp.I64AtomicRmw16SubU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_16_SubU)); break;
                case AtomOp.I64AtomicRmw32SubU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_32_SubU)); break;

                case AtomOp.I32AtomicRmwAnd:       EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_And));    break;
                case AtomOp.I64AtomicRmwAnd:       EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_And));    break;
                case AtomOp.I32AtomicRmw8AndU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_8_AndU));  break;
                case AtomOp.I32AtomicRmw16AndU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_16_AndU)); break;
                case AtomOp.I64AtomicRmw8AndU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_8_AndU));  break;
                case AtomOp.I64AtomicRmw16AndU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_16_AndU)); break;
                case AtomOp.I64AtomicRmw32AndU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_32_AndU)); break;

                case AtomOp.I32AtomicRmwOr:        EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_Or));     break;
                case AtomOp.I64AtomicRmwOr:        EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_Or));     break;
                case AtomOp.I32AtomicRmw8OrU:      EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_8_OrU));   break;
                case AtomOp.I32AtomicRmw16OrU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_16_OrU));  break;
                case AtomOp.I64AtomicRmw8OrU:      EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_8_OrU));   break;
                case AtomOp.I64AtomicRmw16OrU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_16_OrU));  break;
                case AtomOp.I64AtomicRmw32OrU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_32_OrU));  break;

                case AtomOp.I32AtomicRmwXor:       EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_Xor));    break;
                case AtomOp.I64AtomicRmwXor:       EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_Xor));    break;
                case AtomOp.I32AtomicRmw8XorU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_8_XorU));  break;
                case AtomOp.I32AtomicRmw16XorU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_16_XorU)); break;
                case AtomOp.I64AtomicRmw8XorU:     EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_8_XorU));  break;
                case AtomOp.I64AtomicRmw16XorU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_16_XorU)); break;
                case AtomOp.I64AtomicRmw32XorU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_32_XorU)); break;

                case AtomOp.I32AtomicRmwXchg:      EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_Xchg));   break;
                case AtomOp.I64AtomicRmwXchg:      EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_Xchg));   break;
                case AtomOp.I32AtomicRmw8XchgU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_8_XchgU)); break;
                case AtomOp.I32AtomicRmw16XchgU:   EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I32_16_XchgU));break;
                case AtomOp.I64AtomicRmw8XchgU:    EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_8_XchgU)); break;
                case AtomOp.I64AtomicRmw16XchgU:   EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_16_XchgU));break;
                case AtomOp.I64AtomicRmw32XchgU:   EmitRmw(il, inst, nameof(AtomicHelpers.Rmw_I64_32_XchgU));break;

                // ---- Cmpxchg ----
                case AtomOp.I32AtomicRmwCmpxchg:      EmitCmpxchg(il, inst, nameof(AtomicHelpers.Cmpxchg_I32));     break;
                case AtomOp.I64AtomicRmwCmpxchg:      EmitCmpxchg(il, inst, nameof(AtomicHelpers.Cmpxchg_I64));     break;
                case AtomOp.I32AtomicRmw8CmpxchgU:    EmitCmpxchg(il, inst, nameof(AtomicHelpers.Cmpxchg_I32_8U));   break;
                case AtomOp.I32AtomicRmw16CmpxchgU:   EmitCmpxchg(il, inst, nameof(AtomicHelpers.Cmpxchg_I32_16U));  break;
                case AtomOp.I64AtomicRmw8CmpxchgU:    EmitCmpxchg(il, inst, nameof(AtomicHelpers.Cmpxchg_I64_8U));   break;
                case AtomOp.I64AtomicRmw16CmpxchgU:   EmitCmpxchg(il, inst, nameof(AtomicHelpers.Cmpxchg_I64_16U));  break;
                case AtomOp.I64AtomicRmw32CmpxchgU:   EmitCmpxchg(il, inst, nameof(AtomicHelpers.Cmpxchg_I64_32U));  break;

                // ---- Wait / notify ----
                case AtomOp.MemoryAtomicNotify:  EmitNotify(il, inst);           break;
                case AtomOp.MemoryAtomicWait32:  EmitWait(il, inst, is64: false); break;
                case AtomOp.MemoryAtomicWait64:  EmitWait(il, inst, is64: true);  break;

                // ---- Fence ----
                case AtomOp.AtomicFence:
                    il.Emit(OpCodes.Call, typeof(AtomicHelpers).GetMethod(
                        nameof(AtomicHelpers.Fence), BindingFlags.Public | BindingFlags.Static)!);
                    break;

                default:
                    throw new System.NotSupportedException(
                        $"AtomicEmitter: no case for {op.GetMnemonic()}");
            }
        }

        // Emit: stack is [addr], becomes [value]. Loads MemoryInstance from
        // ctx.Memories[memIdx], then calls AtomicHelpers.LoadXxx(mem, addr, offset).
        private static void EmitLoad(ILGenerator il, InstructionBase inst, string helperName)
        {
            var op = (InstAtomicMemoryOp)inst;
            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4, op.MemIndex);
            il.Emit(OpCodes.Ldelem_Ref);             // MemoryInstance
            il.Emit(OpCodes.Ldloc, addrLocal);       // addr
            il.Emit(OpCodes.Ldc_I8, op.MemOffset);   // offset

            il.Emit(OpCodes.Call, typeof(AtomicHelpers).GetMethod(helperName,
                BindingFlags.Public | BindingFlags.Static)!);
        }

        // Emit: stack is [addr, value], becomes [].
        private static void EmitStore(ILGenerator il, InstructionBase inst, string helperName)
        {
            var op = (InstAtomicMemoryOp)inst;
            var method = typeof(AtomicHelpers).GetMethod(helperName,
                BindingFlags.Public | BindingFlags.Static)!;
            var valueType = method.GetParameters()[3].ParameterType;

            var valueLocal = il.DeclareLocal(valueType);
            il.Emit(OpCodes.Stloc, valueLocal);      // save value
            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);       // save addr

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4, op.MemIndex);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, addrLocal);
            il.Emit(OpCodes.Ldc_I8, op.MemOffset);
            il.Emit(OpCodes.Ldloc, valueLocal);

            il.Emit(OpCodes.Call, method);
        }

        // Emit: stack is [addr, arg], becomes [original].
        private static void EmitRmw(ILGenerator il, InstructionBase inst, string helperName)
        {
            var op = (InstAtomicMemoryOp)inst;
            var method = typeof(AtomicHelpers).GetMethod(helperName,
                BindingFlags.Public | BindingFlags.Static)!;
            var argType = method.GetParameters()[3].ParameterType;

            var argLocal = il.DeclareLocal(argType);
            il.Emit(OpCodes.Stloc, argLocal);
            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4, op.MemIndex);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, addrLocal);
            il.Emit(OpCodes.Ldc_I8, op.MemOffset);
            il.Emit(OpCodes.Ldloc, argLocal);

            il.Emit(OpCodes.Call, method);
        }

        // Emit: stack is [addr, expected, replacement], becomes [original].
        private static void EmitCmpxchg(ILGenerator il, InstructionBase inst, string helperName)
        {
            var op = (InstAtomicMemoryOp)inst;
            var method = typeof(AtomicHelpers).GetMethod(helperName,
                BindingFlags.Public | BindingFlags.Static)!;
            var paramTypes = method.GetParameters();
            var expectedType = paramTypes[3].ParameterType;
            var replacementType = paramTypes[4].ParameterType;

            var replLocal = il.DeclareLocal(replacementType);
            il.Emit(OpCodes.Stloc, replLocal);
            var expLocal = il.DeclareLocal(expectedType);
            il.Emit(OpCodes.Stloc, expLocal);
            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, MemoriesField);
            il.Emit(OpCodes.Ldc_I4, op.MemIndex);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Ldloc, addrLocal);
            il.Emit(OpCodes.Ldc_I8, op.MemOffset);
            il.Emit(OpCodes.Ldloc, expLocal);
            il.Emit(OpCodes.Ldloc, replLocal);

            il.Emit(OpCodes.Call, method);
        }

        // notify: stack is [addr, count], becomes [woken].
        // Routes through ThinContext.ExecContext for policy access.
        private static void EmitNotify(ILGenerator il, InstructionBase inst)
        {
            var op = (InstAtomicMemoryOp)inst;
            var countLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, countLocal);
            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);

            il.Emit(OpCodes.Ldarg_0);                    // ThinContext
            il.Emit(OpCodes.Ldc_I4, op.MemIndex);        // memIdx
            il.Emit(OpCodes.Ldloc, addrLocal);           // addr
            il.Emit(OpCodes.Ldc_I8, op.MemOffset);       // offset
            il.Emit(OpCodes.Ldloc, countLocal);          // count

            il.Emit(OpCodes.Call, typeof(AtomicHelpers).GetMethod(
                nameof(AtomicHelpers.Notify), BindingFlags.Public | BindingFlags.Static)!);
        }

        // wait: stack is [addr, expected, timeoutNs], becomes [result].
        private static void EmitWait(ILGenerator il, InstructionBase inst, bool is64)
        {
            var op = (InstAtomicMemoryOp)inst;
            var timeoutLocal = il.DeclareLocal(typeof(long));
            il.Emit(OpCodes.Stloc, timeoutLocal);
            var expectedType = is64 ? typeof(long) : typeof(int);
            var expectedLocal = il.DeclareLocal(expectedType);
            il.Emit(OpCodes.Stloc, expectedLocal);
            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);

            il.Emit(OpCodes.Ldarg_0);                    // ThinContext
            il.Emit(OpCodes.Ldc_I4, op.MemIndex);
            il.Emit(OpCodes.Ldloc, addrLocal);
            il.Emit(OpCodes.Ldc_I8, op.MemOffset);
            il.Emit(OpCodes.Ldloc, expectedLocal);
            il.Emit(OpCodes.Ldloc, timeoutLocal);

            var name = is64 ? nameof(AtomicHelpers.Wait64) : nameof(AtomicHelpers.Wait32);
            il.Emit(OpCodes.Call, typeof(AtomicHelpers).GetMethod(
                name, BindingFlags.Public | BindingFlags.Static)!);
        }
    }

    /// <summary>
    /// Runtime support for transpiled atomic ops. Each entry point does bounds +
    /// exact-alignment checks (trap on failure) and then delegates to the phase-1
    /// <see cref="MemoryInstance"/> atomic helpers, so correctness properties
    /// (e.g. concurrent-grow safety) are shared with the polymorphic interpreter
    /// and the switch runtime.
    /// </summary>
    public static class AtomicHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CheckEa(MemoryInstance mem, int addr, long offset, int widthBytes, string op)
        {
            long ea = (uint)addr + offset;
            if (ea < 0 || ea + widthBytes > mem.Data.Length)
                throw new TrapException(
                    $"{op}: out of bounds atomic access (ea={ea}, width={widthBytes}, size={mem.Data.Length})");
            if ((ea & (widthBytes - 1)) != 0)
                throw new TrapException(
                    $"{op}: unaligned atomic access at ea={ea} (width={widthBytes})");
            return (int)ea;
        }

        // ---- Loads ----
        public static int LoadI32(MemoryInstance mem, int addr, long offset)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i32.atomic.load");
            return mem.AtomicLoadInt32(ea);
        }
        public static long LoadI64(MemoryInstance mem, int addr, long offset)
        {
            int ea = CheckEa(mem, addr, offset, 8, "i64.atomic.load");
            return mem.AtomicLoadInt64(ea);
        }
        public static int LoadI32_8U(MemoryInstance mem, int addr, long offset)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i32.atomic.load8_u");
            return Volatile.Read(ref mem.Data[ea]);
        }
        public static int LoadI32_16U(MemoryInstance mem, int addr, long offset)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i32.atomic.load16_u");
            ref ushort cell = ref Unsafe.As<byte, ushort>(ref mem.Data[ea]);
            return Volatile.Read(ref cell);
        }
        public static long LoadI64_8U(MemoryInstance mem, int addr, long offset)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i64.atomic.load8_u");
            return Volatile.Read(ref mem.Data[ea]);
        }
        public static long LoadI64_16U(MemoryInstance mem, int addr, long offset)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i64.atomic.load16_u");
            ref ushort cell = ref Unsafe.As<byte, ushort>(ref mem.Data[ea]);
            return Volatile.Read(ref cell);
        }
        public static long LoadI64_32U(MemoryInstance mem, int addr, long offset)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i64.atomic.load32_u");
            return (uint)mem.AtomicLoadInt32(ea);  // zero-extend
        }

        // ---- Stores ----
        public static void StoreI32(MemoryInstance mem, int addr, long offset, int value)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i32.atomic.store");
            mem.AtomicStoreInt32(ea, (int)value);
        }
        public static void StoreI64(MemoryInstance mem, int addr, long offset, long value)
        {
            int ea = CheckEa(mem, addr, offset, 8, "i64.atomic.store");
            mem.AtomicStoreInt64(ea, (long)value);
        }
        public static void StoreI32_8(MemoryInstance mem, int addr, long offset, int value)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i32.atomic.store8");
            Volatile.Write(ref mem.Data[ea], (byte)value);
        }
        public static void StoreI32_16(MemoryInstance mem, int addr, long offset, int value)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i32.atomic.store16");
            ref ushort cell = ref Unsafe.As<byte, ushort>(ref mem.Data[ea]);
            Volatile.Write(ref cell, (ushort)value);
        }
        public static void StoreI64_8(MemoryInstance mem, int addr, long offset, long value)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i64.atomic.store8");
            Volatile.Write(ref mem.Data[ea], (byte)value);
        }
        public static void StoreI64_16(MemoryInstance mem, int addr, long offset, long value)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i64.atomic.store16");
            ref ushort cell = ref Unsafe.As<byte, ushort>(ref mem.Data[ea]);
            Volatile.Write(ref cell, (ushort)value);
        }
        public static void StoreI64_32(MemoryInstance mem, int addr, long offset, long value)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i64.atomic.store32");
            mem.AtomicStoreInt32(ea, (int)value);
        }

        // ---- RMW: add ----
        public static int Rmw_I32_Add(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i32.atomic.rmw.add");
            return mem.AtomicAddInt32(ea, arg);
        }
        public static long Rmw_I64_Add(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 8, "i64.atomic.rmw.add");
            return mem.AtomicAddInt64(ea, arg);
        }
        public static int Rmw_I32_8_AddU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i32.atomic.rmw8.add_u");
            return SubwordCas.Loop(mem, ea, 1, old => old + arg);
        }
        public static int Rmw_I32_16_AddU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i32.atomic.rmw16.add_u");
            return SubwordCas.Loop(mem, ea, 2, old => old + arg);
        }
        public static long Rmw_I64_8_AddU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i64.atomic.rmw8.add_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 1, old => old + a);
        }
        public static long Rmw_I64_16_AddU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i64.atomic.rmw16.add_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 2, old => old + a);
        }
        public static long Rmw_I64_32_AddU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i64.atomic.rmw32.add_u");
            return (uint)mem.AtomicAddInt32(ea, (int)arg);
        }

        // ---- RMW: sub ----
        public static int Rmw_I32_Sub(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i32.atomic.rmw.sub");
            return mem.AtomicAddInt32(ea, -arg);
        }
        public static long Rmw_I64_Sub(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 8, "i64.atomic.rmw.sub");
            return mem.AtomicAddInt64(ea, -arg);
        }
        public static int Rmw_I32_8_SubU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i32.atomic.rmw8.sub_u");
            return SubwordCas.Loop(mem, ea, 1, old => old - arg);
        }
        public static int Rmw_I32_16_SubU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i32.atomic.rmw16.sub_u");
            return SubwordCas.Loop(mem, ea, 2, old => old - arg);
        }
        public static long Rmw_I64_8_SubU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i64.atomic.rmw8.sub_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 1, old => old - a);
        }
        public static long Rmw_I64_16_SubU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i64.atomic.rmw16.sub_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 2, old => old - a);
        }
        public static long Rmw_I64_32_SubU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i64.atomic.rmw32.sub_u");
            return (uint)mem.AtomicAddInt32(ea, -(int)arg);
        }

        // ---- RMW: and ----
        public static int Rmw_I32_And(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i32.atomic.rmw.and");
            return mem.AtomicAndInt32(ea, arg);
        }
        public static long Rmw_I64_And(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 8, "i64.atomic.rmw.and");
            return mem.AtomicAndInt64(ea, arg);
        }
        public static int Rmw_I32_8_AndU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i32.atomic.rmw8.and_u");
            return SubwordCas.Loop(mem, ea, 1, old => old & arg);
        }
        public static int Rmw_I32_16_AndU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i32.atomic.rmw16.and_u");
            return SubwordCas.Loop(mem, ea, 2, old => old & arg);
        }
        public static long Rmw_I64_8_AndU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i64.atomic.rmw8.and_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 1, old => old & a);
        }
        public static long Rmw_I64_16_AndU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i64.atomic.rmw16.and_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 2, old => old & a);
        }
        public static long Rmw_I64_32_AndU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i64.atomic.rmw32.and_u");
            return (uint)mem.AtomicAndInt32(ea, (int)arg);
        }

        // ---- RMW: or ----
        public static int Rmw_I32_Or(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i32.atomic.rmw.or");
            return mem.AtomicOrInt32(ea, arg);
        }
        public static long Rmw_I64_Or(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 8, "i64.atomic.rmw.or");
            return mem.AtomicOrInt64(ea, arg);
        }
        public static int Rmw_I32_8_OrU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i32.atomic.rmw8.or_u");
            return SubwordCas.Loop(mem, ea, 1, old => old | arg);
        }
        public static int Rmw_I32_16_OrU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i32.atomic.rmw16.or_u");
            return SubwordCas.Loop(mem, ea, 2, old => old | arg);
        }
        public static long Rmw_I64_8_OrU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i64.atomic.rmw8.or_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 1, old => old | a);
        }
        public static long Rmw_I64_16_OrU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i64.atomic.rmw16.or_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 2, old => old | a);
        }
        public static long Rmw_I64_32_OrU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i64.atomic.rmw32.or_u");
            return (uint)mem.AtomicOrInt32(ea, (int)arg);
        }

        // ---- RMW: xor ----
        public static int Rmw_I32_Xor(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i32.atomic.rmw.xor");
            return mem.AtomicXorInt32(ea, arg);
        }
        public static long Rmw_I64_Xor(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 8, "i64.atomic.rmw.xor");
            return mem.AtomicXorInt64(ea, arg);
        }
        public static int Rmw_I32_8_XorU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i32.atomic.rmw8.xor_u");
            return SubwordCas.Loop(mem, ea, 1, old => old ^ arg);
        }
        public static int Rmw_I32_16_XorU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i32.atomic.rmw16.xor_u");
            return SubwordCas.Loop(mem, ea, 2, old => old ^ arg);
        }
        public static long Rmw_I64_8_XorU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i64.atomic.rmw8.xor_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 1, old => old ^ a);
        }
        public static long Rmw_I64_16_XorU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i64.atomic.rmw16.xor_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 2, old => old ^ a);
        }
        public static long Rmw_I64_32_XorU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i64.atomic.rmw32.xor_u");
            return (uint)mem.AtomicXorInt32(ea, (int)arg);
        }

        // ---- RMW: xchg ----
        public static int Rmw_I32_Xchg(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i32.atomic.rmw.xchg");
            return mem.AtomicExchangeInt32(ea, arg);
        }
        public static long Rmw_I64_Xchg(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 8, "i64.atomic.rmw.xchg");
            return mem.AtomicExchangeInt64(ea, arg);
        }
        public static int Rmw_I32_8_XchgU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i32.atomic.rmw8.xchg_u");
            return SubwordCas.Loop(mem, ea, 1, _ => arg);
        }
        public static int Rmw_I32_16_XchgU(MemoryInstance mem, int addr, long offset, int arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i32.atomic.rmw16.xchg_u");
            return SubwordCas.Loop(mem, ea, 2, _ => arg);
        }
        public static long Rmw_I64_8_XchgU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i64.atomic.rmw8.xchg_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 1, _ => a);
        }
        public static long Rmw_I64_16_XchgU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i64.atomic.rmw16.xchg_u");
            int a = (int)arg;
            return (uint)SubwordCas.Loop(mem, ea, 2, _ => a);
        }
        public static long Rmw_I64_32_XchgU(MemoryInstance mem, int addr, long offset, long arg)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i64.atomic.rmw32.xchg_u");
            return (uint)mem.AtomicExchangeInt32(ea, (int)arg);
        }

        // ---- Cmpxchg ----
        public static int Cmpxchg_I32(MemoryInstance mem, int addr, long offset, int expected, int replacement)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i32.atomic.rmw.cmpxchg");
            return mem.AtomicCompareExchangeInt32(ea, replacement, expected);
        }
        public static long Cmpxchg_I64(MemoryInstance mem, int addr, long offset, long expected, long replacement)
        {
            int ea = CheckEa(mem, addr, offset, 8, "i64.atomic.rmw.cmpxchg");
            return mem.AtomicCompareExchangeInt64(ea, replacement, expected);
        }
        public static int Cmpxchg_I32_8U(MemoryInstance mem, int addr, long offset, int expected, int replacement)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i32.atomic.rmw8.cmpxchg_u");
            return SubwordCas.Cmpxchg(mem, ea, 1, expected, replacement);
        }
        public static int Cmpxchg_I32_16U(MemoryInstance mem, int addr, long offset, int expected, int replacement)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i32.atomic.rmw16.cmpxchg_u");
            return SubwordCas.Cmpxchg(mem, ea, 2, expected, replacement);
        }
        public static long Cmpxchg_I64_8U(MemoryInstance mem, int addr, long offset, long expected, long replacement)
        {
            int ea = CheckEa(mem, addr, offset, 1, "i64.atomic.rmw8.cmpxchg_u");
            return SubwordCas.Cmpxchg(mem, ea, 1, (int)expected, (int)replacement);
        }
        public static long Cmpxchg_I64_16U(MemoryInstance mem, int addr, long offset, long expected, long replacement)
        {
            int ea = CheckEa(mem, addr, offset, 2, "i64.atomic.rmw16.cmpxchg_u");
            return SubwordCas.Cmpxchg(mem, ea, 2, (int)expected, (int)replacement);
        }
        public static long Cmpxchg_I64_32U(MemoryInstance mem, int addr, long offset, long expected, long replacement)
        {
            int ea = CheckEa(mem, addr, offset, 4, "i64.atomic.rmw32.cmpxchg_u");
            // Zero-extend int result to long (upper 32 bits = 0) per wasm rmw32 spec.
            return (uint)mem.AtomicCompareExchangeInt32(ea, (int)replacement, (int)expected);
        }

        // ---- Wait / Notify ----
        // Wait/notify need access to the ConcurrencyPolicy. In-framework,
        // ThinContext.ExecContext gives us the runtime's policy. Standalone
        // mode (ExecContext == null) falls back to NotSupportedPolicy so the
        // ops still behave correctly: wait returns timed-out/not-equal,
        // notify returns 0, neither deadlocks.

        private static readonly IConcurrencyPolicy _standaloneFallback = new NotSupportedPolicy();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IConcurrencyPolicy GetPolicy(ThinContext ctx)
            => ctx.ExecContext?.ConcurrencyPolicy ?? _standaloneFallback;

        public static int Notify(ThinContext ctx, int memIdx, int addr, long offset, int count)
        {
            var mem = ctx.Memories[memIdx];
            int ea = CheckEa(mem, addr, offset, 4, "memory.atomic.notify");
            return GetPolicy(ctx).Notify(mem, ea, count);
        }

        public static int Wait32(ThinContext ctx, int memIdx, int addr, long offset, int expected, long timeoutNs)
        {
            var mem = ctx.Memories[memIdx];
            int ea = CheckEa(mem, addr, offset, 4, "memory.atomic.wait32");
            return GetPolicy(ctx).Wait32(mem, ea, expected, timeoutNs);
        }

        public static int Wait64(ThinContext ctx, int memIdx, int addr, long offset, long expected, long timeoutNs)
        {
            var mem = ctx.Memories[memIdx];
            int ea = CheckEa(mem, addr, offset, 8, "memory.atomic.wait64");
            return GetPolicy(ctx).Wait64(mem, ea, expected, timeoutNs);
        }

        // ---- Fence ----
        public static void Fence() => Interlocked.MemoryBarrier();
    }
}
