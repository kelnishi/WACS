// Copyright 2025 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using WasmOpCode = Wacs.Core.OpCodes.OpCode;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for WebAssembly memory load/store instructions.
    ///
    /// Memory access pattern:
    ///   byte[] mem = ctx.Memories[memIdx];
    ///   long ea = address + offset;
    ///   bounds check → trap
    ///   value = MemoryHelpers.LoadXxx(mem, ea);
    ///
    /// Uses static helper methods for the actual load/store to keep emitted IL simple.
    /// </summary>
    internal static class MemoryEmitter
    {
        private static readonly FieldInfo MemoriesField =
            typeof(ThinContext).GetField(nameof(ThinContext.Memories))!;
        internal static readonly FieldInfo MemoryDataField =
            typeof(Wacs.Core.Runtime.Types.MemoryInstance).GetField(
                nameof(Wacs.Core.Runtime.Types.MemoryInstance.Data))!;

        public static bool CanEmit(WasmOpCode op)
        {
            byte b = (byte)op;
            // Load/store instructions: 0x28-0x3E
            // memory.size: 0x3F, memory.grow: 0x40
            return b >= 0x28 && b <= 0x40;
        }

        public static void Emit(ILGenerator il, InstructionBase inst, WasmOpCode op)
        {
            switch (op)
            {
                // === Loads ===
                case WasmOpCode.I32Load:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI32));
                    break;
                case WasmOpCode.I64Load:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI64));
                    break;
                case WasmOpCode.F32Load:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadF32));
                    break;
                case WasmOpCode.F64Load:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadF64));
                    break;
                case WasmOpCode.I32Load8S:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI32_8S));
                    break;
                case WasmOpCode.I32Load8U:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI32_8U));
                    break;
                case WasmOpCode.I32Load16S:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI32_16S));
                    break;
                case WasmOpCode.I32Load16U:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI32_16U));
                    break;
                case WasmOpCode.I64Load8S:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI64_8S));
                    break;
                case WasmOpCode.I64Load8U:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI64_8U));
                    break;
                case WasmOpCode.I64Load16S:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI64_16S));
                    break;
                case WasmOpCode.I64Load16U:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI64_16U));
                    break;
                case WasmOpCode.I64Load32S:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI64_32S));
                    break;
                case WasmOpCode.I64Load32U:
                    EmitLoad(il, (InstMemoryLoad)inst, nameof(MemoryHelpers.LoadI64_32U));
                    break;

                // === Stores ===
                case WasmOpCode.I32Store:
                    EmitStore(il, (InstMemoryStore)inst, nameof(MemoryHelpers.StoreI32));
                    break;
                case WasmOpCode.I64Store:
                    EmitStore(il, (InstMemoryStore)inst, nameof(MemoryHelpers.StoreI64));
                    break;
                case WasmOpCode.F32Store:
                    EmitStore(il, (InstMemoryStore)inst, nameof(MemoryHelpers.StoreF32));
                    break;
                case WasmOpCode.F64Store:
                    EmitStore(il, (InstMemoryStore)inst, nameof(MemoryHelpers.StoreF64));
                    break;
                case WasmOpCode.I32Store8:
                    EmitStore(il, (InstMemoryStore)inst, nameof(MemoryHelpers.StoreI32_8));
                    break;
                case WasmOpCode.I32Store16:
                    EmitStore(il, (InstMemoryStore)inst, nameof(MemoryHelpers.StoreI32_16));
                    break;
                case WasmOpCode.I64Store8:
                    EmitStore(il, (InstMemoryStore)inst, nameof(MemoryHelpers.StoreI64_8));
                    break;
                case WasmOpCode.I64Store16:
                    EmitStore(il, (InstMemoryStore)inst, nameof(MemoryHelpers.StoreI64_16));
                    break;
                case WasmOpCode.I64Store32:
                    EmitStore(il, (InstMemoryStore)inst, nameof(MemoryHelpers.StoreI64_32));
                    break;

                // === memory.size / memory.grow ===
                case WasmOpCode.MemorySize:
                    EmitMemorySize(il, (InstMemorySize)inst);
                    break;
                case WasmOpCode.MemoryGrow:
                    EmitMemoryGrow(il, (InstMemoryGrow)inst);
                    break;

                default:
                    throw new TranspilerException($"MemoryEmitter: unhandled opcode {op}");
            }
        }

        /// <summary>
        /// Emit a memory load: stack has [address], result is [value].
        /// call MemoryHelpers.LoadXxx(ctx.Memories[memIdx], address, offset)
        /// </summary>
        private static void EmitLoad(ILGenerator il, InstMemoryLoad inst, string helperName)
        {
            // Stack: [address (i32)]
            // We need: MemoryHelpers.LoadXxx(byte[] mem, int address, long offset)

            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);      // save address

            il.Emit(OpCodes.Ldarg_0);                // ThinContext
            il.Emit(OpCodes.Ldfld, MemoriesField);   // MemoryInstance[]
            il.Emit(OpCodes.Ldc_I4, inst.MemIndex);  // memIdx
            il.Emit(OpCodes.Ldelem_Ref);              // MemoryInstance
            il.Emit(OpCodes.Ldfld, MemoryDataField);  // byte[] Data

            il.Emit(OpCodes.Ldloc, addrLocal);        // address
            il.Emit(OpCodes.Ldc_I8, inst.MemOffset);  // offset

            var method = typeof(MemoryHelpers).GetMethod(helperName,
                BindingFlags.Public | BindingFlags.Static)!;
            il.Emit(OpCodes.Call, method);
        }

        /// <summary>
        /// Emit a memory store: stack has [address, value], result is [].
        /// call MemoryHelpers.StoreXxx(byte[] mem, int address, long offset, value)
        /// </summary>
        private static void EmitStore(ILGenerator il, InstMemoryStore inst, string helperName)
        {
            var method = typeof(MemoryHelpers).GetMethod(helperName,
                BindingFlags.Public | BindingFlags.Static)!;
            var paramTypes = method.GetParameters();
            var valueType = paramTypes[3].ParameterType; // 4th param is the value

            // Stack: [address (i32), value]
            var valueLocal = il.DeclareLocal(valueType);
            il.Emit(OpCodes.Stloc, valueLocal);       // save value

            var addrLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, addrLocal);        // save address

            il.Emit(OpCodes.Ldarg_0);                  // ThinContext
            il.Emit(OpCodes.Ldfld, MemoriesField);     // MemoryInstance[]
            il.Emit(OpCodes.Ldc_I4, inst.MemIndex);    // memIdx
            il.Emit(OpCodes.Ldelem_Ref);                // MemoryInstance
            il.Emit(OpCodes.Ldfld, MemoryDataField);    // byte[] Data

            il.Emit(OpCodes.Ldloc, addrLocal);          // address
            il.Emit(OpCodes.Ldc_I8, inst.MemOffset);    // offset
            il.Emit(OpCodes.Ldloc, valueLocal);          // value

            il.Emit(OpCodes.Call, method);
        }

        private static void EmitMemorySize(ILGenerator il, InstMemorySize inst)
        {
            // Push ctx.Memories[memIdx].Data.Length / PageSize
            il.Emit(OpCodes.Ldarg_0);                  // ThinContext
            il.Emit(OpCodes.Ldfld, MemoriesField);     // MemoryInstance[]
            il.Emit(OpCodes.Ldc_I4, inst.MemIndex);    // memIdx
            il.Emit(OpCodes.Ldelem_Ref);                // MemoryInstance
            il.Emit(OpCodes.Ldfld, MemoryDataField);    // byte[] Data
            il.Emit(OpCodes.Ldlen);                     // length (native int)
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4, 65536);             // PageSize
            il.Emit(OpCodes.Div);
        }

        private static void EmitMemoryGrow(ILGenerator il, InstMemoryGrow inst)
        {
            // Stack: [delta pages (i32)]
            // call MemoryHelpers.Grow(ThinContext, memIdx, delta) → i32 (old size or -1)
            var deltaLocal = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, deltaLocal);

            il.Emit(OpCodes.Ldarg_0);                  // ThinContext
            il.Emit(OpCodes.Ldc_I4, inst.MemIndex);    // memIdx
            il.Emit(OpCodes.Ldloc, deltaLocal);         // delta

            il.Emit(OpCodes.Call, typeof(MemoryHelpers).GetMethod(
                nameof(MemoryHelpers.MemoryGrow),
                BindingFlags.Public | BindingFlags.Static)!);
        }
    }

    /// <summary>
    /// Static helper methods called by transpiled code for memory access.
    /// These handle bounds checking and byte-level access, keeping the
    /// emitted IL simple. Performance-sensitive paths can be inlined later.
    /// </summary>
    public static class MemoryHelpers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void BoundsCheck(byte[] mem, long ea, int width)
        {
            if (ea < 0 || ea + width > mem.Length)
                throw new TrapException("out of bounds memory access");
        }

        // === i32 loads ===
        public static int LoadI32(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            return Unsafe.ReadUnaligned<int>(ref mem[(int)ea]);
        }

        public static int LoadI32_8S(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 1);
            return (sbyte)mem[(int)ea];
        }

        public static int LoadI32_8U(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 1);
            return mem[(int)ea];
        }

        public static int LoadI32_16S(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 2);
            return Unsafe.ReadUnaligned<short>(ref mem[(int)ea]);
        }

        public static int LoadI32_16U(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 2);
            return Unsafe.ReadUnaligned<ushort>(ref mem[(int)ea]);
        }

        // === i64 loads ===
        public static long LoadI64(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            return Unsafe.ReadUnaligned<long>(ref mem[(int)ea]);
        }

        public static long LoadI64_8S(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 1);
            return (sbyte)mem[(int)ea];
        }

        public static long LoadI64_8U(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 1);
            return mem[(int)ea];
        }

        public static long LoadI64_16S(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 2);
            return Unsafe.ReadUnaligned<short>(ref mem[(int)ea]);
        }

        public static long LoadI64_16U(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 2);
            return Unsafe.ReadUnaligned<ushort>(ref mem[(int)ea]);
        }

        public static long LoadI64_32S(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            int raw = Unsafe.ReadUnaligned<int>(ref mem[(int)ea]);
            System.IO.File.AppendAllText("/tmp/mem_trace.log",$"  L32S mem={mem.GetHashCode()} ea={ea} bytes=[{mem[(int)ea]:X2},{mem[(int)ea+1]:X2},{mem[(int)ea+2]:X2},{mem[(int)ea+3]:X2}] raw=0x{raw:X8}={raw}\n");
            return raw;
        }

        public static long LoadI64_32U(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            return (uint)Unsafe.ReadUnaligned<int>(ref mem[(int)ea]);
        }

        // === float loads ===
        public static float LoadF32(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            return Unsafe.ReadUnaligned<float>(ref mem[(int)ea]);
        }

        public static double LoadF64(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            return Unsafe.ReadUnaligned<double>(ref mem[(int)ea]);
        }

        // === i32 stores ===
        public static void StoreI32(byte[] mem, int addr, long offset, int value)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            Unsafe.WriteUnaligned(ref mem[(int)ea], value);
        }

        public static void StoreI32_8(byte[] mem, int addr, long offset, int value)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 1);
            mem[(int)ea] = (byte)value;
            if (ea < 4)
                System.IO.File.AppendAllText("/tmp/mem_trace.log",$"  S8 mem={mem.GetHashCode()} ea={ea} val=0x{value:X8} byte=0x{(byte)value:X2} mem[0..3]=[{mem[0]:X2},{mem[1]:X2},{mem[2]:X2},{mem[3]:X2}]\n");
        }

        public static void StoreI32_16(byte[] mem, int addr, long offset, int value)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 2);
            Unsafe.WriteUnaligned(ref mem[(int)ea], (short)value);
        }

        // === i64 stores ===
        public static void StoreI64(byte[] mem, int addr, long offset, long value)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            Unsafe.WriteUnaligned(ref mem[(int)ea], value);
        }

        public static void StoreI64_8(byte[] mem, int addr, long offset, long value)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 1);
            mem[(int)ea] = (byte)value;
        }

        public static void StoreI64_16(byte[] mem, int addr, long offset, long value)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 2);
            Unsafe.WriteUnaligned(ref mem[(int)ea], (short)value);
        }

        public static void StoreI64_32(byte[] mem, int addr, long offset, long value)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            Unsafe.WriteUnaligned(ref mem[(int)ea], (int)value);
        }

        // === float stores ===
        public static void StoreF32(byte[] mem, int addr, long offset, float value)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            Unsafe.WriteUnaligned(ref mem[(int)ea], value);
        }

        public static void StoreF64(byte[] mem, int addr, long offset, double value)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            Unsafe.WriteUnaligned(ref mem[(int)ea], value);
        }

        // ================================================================
        // SIMD memory operations
        // ================================================================

        // v128.load: load 16 bytes
        public static V128 LoadV128(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 16);
            return Unsafe.ReadUnaligned<V128>(ref mem[(int)ea]);
        }

        // v128.store: store 16 bytes
        public static void StoreV128(byte[] mem, int addr, long offset, V128 value)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 16);
            Unsafe.WriteUnaligned(ref mem[(int)ea], value);
        }

        // v128.load8x8_s: load 8 bytes, sign-extend each to i16
        public static V128 LoadV128_8x8S(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            return new V128(
                (short)(sbyte)mem[(int)ea], (short)(sbyte)mem[(int)ea+1],
                (short)(sbyte)mem[(int)ea+2], (short)(sbyte)mem[(int)ea+3],
                (short)(sbyte)mem[(int)ea+4], (short)(sbyte)mem[(int)ea+5],
                (short)(sbyte)mem[(int)ea+6], (short)(sbyte)mem[(int)ea+7]);
        }

        // v128.load8x8_u: load 8 bytes, zero-extend each to i16
        public static V128 LoadV128_8x8U(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            return new V128(
                (short)mem[(int)ea], (short)mem[(int)ea+1],
                (short)mem[(int)ea+2], (short)mem[(int)ea+3],
                (short)mem[(int)ea+4], (short)mem[(int)ea+5],
                (short)mem[(int)ea+6], (short)mem[(int)ea+7]);
        }

        // v128.load16x4_s: load 8 bytes as 4 i16, sign-extend to i32
        public static V128 LoadV128_16x4S(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            return new V128(
                (int)Unsafe.ReadUnaligned<short>(ref mem[(int)ea]),
                (int)Unsafe.ReadUnaligned<short>(ref mem[(int)ea+2]),
                (int)Unsafe.ReadUnaligned<short>(ref mem[(int)ea+4]),
                (int)Unsafe.ReadUnaligned<short>(ref mem[(int)ea+6]));
        }

        // v128.load16x4_u
        public static V128 LoadV128_16x4U(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            return new V128(
                (int)Unsafe.ReadUnaligned<ushort>(ref mem[(int)ea]),
                (int)Unsafe.ReadUnaligned<ushort>(ref mem[(int)ea+2]),
                (int)Unsafe.ReadUnaligned<ushort>(ref mem[(int)ea+4]),
                (int)Unsafe.ReadUnaligned<ushort>(ref mem[(int)ea+6]));
        }

        // v128.load32x2_s: load 8 bytes as 2 i32, sign-extend to i64
        public static V128 LoadV128_32x2S(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            return new V128(
                (long)Unsafe.ReadUnaligned<int>(ref mem[(int)ea]),
                (long)Unsafe.ReadUnaligned<int>(ref mem[(int)ea+4]));
        }

        // v128.load32x2_u
        public static V128 LoadV128_32x2U(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            return new V128(
                (long)(uint)Unsafe.ReadUnaligned<int>(ref mem[(int)ea]),
                (long)(uint)Unsafe.ReadUnaligned<int>(ref mem[(int)ea+4]));
        }

        // v128.load8_splat
        public static V128 LoadV128_8Splat(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 1);
            byte v = mem[(int)ea];
            return new V128(v,v,v,v,v,v,v,v,v,v,v,v,v,v,v,v);
        }

        // v128.load16_splat
        public static V128 LoadV128_16Splat(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 2);
            ushort v = Unsafe.ReadUnaligned<ushort>(ref mem[(int)ea]);
            return new V128(v,v,v,v,v,v,v,v);
        }

        // v128.load32_splat
        public static V128 LoadV128_32Splat(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            uint v = Unsafe.ReadUnaligned<uint>(ref mem[(int)ea]);
            return new V128(v,v,v,v);
        }

        // v128.load64_splat
        public static V128 LoadV128_64Splat(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            ulong v = Unsafe.ReadUnaligned<ulong>(ref mem[(int)ea]);
            return new V128(v,v);
        }

        // v128.load32_zero: load 4 bytes into lane 0, rest zero
        public static V128 LoadV128_32Zero(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            uint v = Unsafe.ReadUnaligned<uint>(ref mem[(int)ea]);
            return new V128(v, 0u, 0u, 0u);
        }

        // v128.load64_zero: load 8 bytes into lane 0, rest zero
        public static V128 LoadV128_64Zero(byte[] mem, int addr, long offset)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            ulong v = Unsafe.ReadUnaligned<ulong>(ref mem[(int)ea]);
            return new V128(v, 0UL);
        }

        // v128.loadN_lane: load N bits from memory into a lane of existing v128
        public static V128 LoadV128_8Lane(byte[] mem, int addr, long offset, V128 vec, byte lane)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 1);
            MV128 result = vec;
            result[(byte)lane] = mem[(int)ea];
            return result;
        }

        public static V128 LoadV128_16Lane(byte[] mem, int addr, long offset, V128 vec, byte lane)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 2);
            MV128 result = vec;
            result[(ushort)lane] = Unsafe.ReadUnaligned<ushort>(ref mem[(int)ea]);
            return result;
        }

        public static V128 LoadV128_32Lane(byte[] mem, int addr, long offset, V128 vec, byte lane)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            MV128 result = vec;
            result[(uint)lane] = Unsafe.ReadUnaligned<uint>(ref mem[(int)ea]);
            return result;
        }

        public static V128 LoadV128_64Lane(byte[] mem, int addr, long offset, V128 vec, byte lane)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            MV128 result = vec;
            result[(ulong)lane] = Unsafe.ReadUnaligned<ulong>(ref mem[(int)ea]);
            return result;
        }

        // v128.storeN_lane: store a lane to memory
        public static void StoreV128_8Lane(byte[] mem, int addr, long offset, V128 vec, byte lane)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 1);
            mem[(int)ea] = vec[(byte)lane];
        }

        public static void StoreV128_16Lane(byte[] mem, int addr, long offset, V128 vec, byte lane)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 2);
            Unsafe.WriteUnaligned(ref mem[(int)ea], vec[(ushort)lane]);
        }

        public static void StoreV128_32Lane(byte[] mem, int addr, long offset, V128 vec, byte lane)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 4);
            Unsafe.WriteUnaligned(ref mem[(int)ea], vec[(uint)lane]);
        }

        public static void StoreV128_64Lane(byte[] mem, int addr, long offset, V128 vec, byte lane)
        {
            long ea = (uint)addr + offset;
            BoundsCheck(mem, ea, 8);
            Unsafe.WriteUnaligned(ref mem[(int)ea], vec[(ulong)lane]);
        }

        // === memory.grow ===
        public static int MemoryGrow(ThinContext ctx, int memIdx, int deltaPages)
        {
            // Both framework and standalone use MemoryInstance.Grow() —
            // it resizes Data in place, visible to all sharing modules.
            var memInst = ctx.Memories[memIdx];
            long oldSize = memInst.Size;

            if (deltaPages < 0) return -1;
            if (deltaPages == 0) return (int)oldSize;

            if (!memInst.Grow(deltaPages))
                return -1;
            return (int)oldSize;
        }
    }
}
