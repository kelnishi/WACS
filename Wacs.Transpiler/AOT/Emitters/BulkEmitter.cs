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
using Wacs.Core.Instructions;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Transpiler.AOT.Emitters
{
    /// <summary>
    /// Emits CIL for 0xFC prefix bulk memory and table instructions.
    /// These are complex runtime operations that access Store for data/element segments,
    /// so they dispatch through static helper methods.
    /// </summary>
    internal static class BulkEmitter
    {
        public static bool CanEmit(ExtCode op)
        {
            return op == ExtCode.MemoryInit
                || op == ExtCode.DataDrop
                || op == ExtCode.MemoryCopy
                || op == ExtCode.MemoryFill
                || op == ExtCode.TableInit
                || op == ExtCode.ElemDrop
                || op == ExtCode.TableCopy;
        }

        public static void Emit(ILGenerator il, InstructionBase inst, ExtCode op)
        {
            switch (op)
            {
                case ExtCode.MemoryCopy:
                {
                    var mc = (InstMemoryCopy)inst;
                    // Stack: [dst, src, len]
                    EmitThreeArgHelper(il, mc.DstMemIndex, mc.SrcMemIndex,
                        nameof(BulkHelpers.MemoryCopy));
                    break;
                }
                case ExtCode.MemoryFill:
                {
                    var mf = (InstMemoryFill)inst;
                    // Stack: [dst, val, len]
                    EmitThreeArgWithOneImm(il, mf.MemoryIndex,
                        nameof(BulkHelpers.MemoryFill));
                    break;
                }
                case ExtCode.MemoryInit:
                {
                    var mi = (InstMemoryInit)inst;
                    // Stack: [dst, src, len]
                    EmitThreeArgHelper(il, mi.MemoryIndex, mi.DataIndex,
                        nameof(BulkHelpers.MemoryInit));
                    break;
                }
                case ExtCode.DataDrop:
                {
                    var dd = (InstDataDrop)inst;
                    // Stack: []
                    il.Emit(OpCodes.Ldarg_0); // TranspiledContext
                    il.Emit(OpCodes.Ldc_I4, dd.DataIndex);
                    il.Emit(OpCodes.Call, typeof(BulkHelpers).GetMethod(
                        nameof(BulkHelpers.DataDrop), BindingFlags.Public | BindingFlags.Static)!);
                    break;
                }
                case ExtCode.TableInit:
                {
                    var ti = (InstTableInit)inst;
                    EmitThreeArgHelper(il, ti.TableIndex, ti.ElemIndex,
                        nameof(BulkHelpers.TableInit));
                    break;
                }
                case ExtCode.ElemDrop:
                {
                    var ed = (InstElemDrop)inst;
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldc_I4, ed.ElemIndex);
                    il.Emit(OpCodes.Call, typeof(BulkHelpers).GetMethod(
                        nameof(BulkHelpers.ElemDrop), BindingFlags.Public | BindingFlags.Static)!);
                    break;
                }
                case ExtCode.TableCopy:
                {
                    var tc = (InstTableCopy)inst;
                    EmitThreeArgHelper(il, tc.DstTableIndex, tc.SrcTableIndex,
                        nameof(BulkHelpers.TableCopy));
                    break;
                }
                default:
                    throw new TranspilerException($"BulkEmitter: unhandled opcode {op}");
            }
        }

        /// <summary>
        /// Emit: spill 3 stack args, push ctx + imm1 + imm2 + args, call helper(ctx, imm1, imm2, a0, a1, a2)
        /// </summary>
        private static void EmitThreeArgHelper(ILGenerator il, int imm1, int imm2, string helperName)
        {
            var a2 = il.DeclareLocal(typeof(int));
            var a1 = il.DeclareLocal(typeof(int));
            var a0 = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, a2);
            il.Emit(OpCodes.Stloc, a1);
            il.Emit(OpCodes.Stloc, a0);

            il.Emit(OpCodes.Ldarg_0); // TranspiledContext
            il.Emit(OpCodes.Ldc_I4, imm1);
            il.Emit(OpCodes.Ldc_I4, imm2);
            il.Emit(OpCodes.Ldloc, a0);
            il.Emit(OpCodes.Ldloc, a1);
            il.Emit(OpCodes.Ldloc, a2);

            il.Emit(OpCodes.Call, typeof(BulkHelpers).GetMethod(
                helperName, BindingFlags.Public | BindingFlags.Static)!);
        }

        private static void EmitThreeArgWithOneImm(ILGenerator il, int imm, string helperName)
        {
            var a2 = il.DeclareLocal(typeof(int));
            var a1 = il.DeclareLocal(typeof(int));
            var a0 = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Stloc, a2);
            il.Emit(OpCodes.Stloc, a1);
            il.Emit(OpCodes.Stloc, a0);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, imm);
            il.Emit(OpCodes.Ldloc, a0);
            il.Emit(OpCodes.Ldloc, a1);
            il.Emit(OpCodes.Ldloc, a2);

            il.Emit(OpCodes.Call, typeof(BulkHelpers).GetMethod(
                helperName, BindingFlags.Public | BindingFlags.Static)!);
        }
    }

    /// <summary>
    /// Static helpers for bulk memory and table operations.
    /// Called from transpiled IL — these need TranspiledContext for Store access.
    /// </summary>
    public static class BulkHelpers
    {
        public static void MemoryCopy(TranspiledContext ctx, int dstMemIdx, int srcMemIdx,
            int dst, int src, int len)
        {
            var dstMem = ctx.Memories[dstMemIdx];
            var srcMem = ctx.Memories[srcMemIdx];
            // WASM spec: bounds check applies even when len == 0
            if ((long)(uint)src + (long)(uint)len > srcMem.Length ||
                (long)(uint)dst + (long)(uint)len > dstMem.Length)
                throw new TrapException("out of bounds memory access");
            if (len == 0) return;
            Buffer.BlockCopy(srcMem, src, dstMem, dst, len);
        }

        public static void MemoryFill(TranspiledContext ctx, int memIdx,
            int dst, int val, int len)
        {
            var mem = ctx.Memories[memIdx];
            // WASM spec: bounds check applies even when len == 0
            if ((long)(uint)dst + (long)(uint)len > mem.Length)
                throw new TrapException("out of bounds memory access");
            if (len == 0) return;
            Array.Fill(mem, (byte)val, dst, len);
        }

        public static void MemoryInit(TranspiledContext ctx, int memIdx, int dataIdx,
            int dst, int src, int len)
        {
            var mem = ctx.Memories[memIdx];
            byte[] segData;

            if (ctx.Store != null && ctx.Module != null)
            {
                var dataAddr = ctx.Module.DataAddrs[(DataIdx)dataIdx];
                var data = ctx.Store[dataAddr];
                segData = data.Data;
            }
            else
            {
                // Standalone: use registered data segments
                segData = ModuleInit.GetDataSegmentData(dataIdx) ?? Array.Empty<byte>();
            }

            // WASM spec: bounds check applies even when len == 0
            if ((long)(uint)src + (long)(uint)len > segData.Length ||
                (long)(uint)dst + (long)(uint)len > mem.Length)
                throw new TrapException("out of bounds memory access");
            if (len == 0) return;
            Buffer.BlockCopy(segData, src, mem, dst, len);
        }

        public static void DataDrop(TranspiledContext ctx, int dataIdx)
        {
            if (ctx.Store != null && ctx.Module != null)
            {
                var dataAddr = ctx.Module.DataAddrs[(DataIdx)dataIdx];
                ctx.Store.DropData(dataAddr);
            }
            // Standalone: data segments are static, dropping is a no-op
            // (the spec says dropped segments behave as empty for subsequent init)
        }

        public static void TableInit(TranspiledContext ctx, int tableIdx, int elemIdx,
            int dst, int src, int len)
        {
            var table = ctx.Tables[tableIdx];

            if (ctx.Store != null && ctx.Module != null)
            {
                // Framework mode
                var elemAddr = ctx.Module.ElemAddrs[(ElemIdx)elemIdx];
                var elem = ctx.Store[elemAddr];
                if ((long)(uint)src + (long)(uint)len > elem.Elements.Count ||
                    (long)(uint)dst + (long)(uint)len > table.Elements.Count)
                    throw new TrapException("out of bounds table access");
                if (len == 0) return;
                for (int i = 0; i < len; i++)
                    table.Elements[dst + i] = elem.Elements[src + i];
            }
            else
            {
                // Standalone mode: use registered element segments
                var elemData = ModuleInit.GetElemSegment(elemIdx);
                if (elemData == null)
                    throw new TrapException("out of bounds table access");
                if ((long)(uint)src + (long)(uint)len > elemData.Length ||
                    (long)(uint)dst + (long)(uint)len > table.Elements.Count)
                    throw new TrapException("out of bounds table access");
                if (len == 0) return;
                for (int i = 0; i < len; i++)
                    table.Elements[dst + i] = elemData[src + i];
            }
        }

        public static void ElemDrop(TranspiledContext ctx, int elemIdx)
        {
            if (ctx.Store != null && ctx.Module != null)
            {
                var elemAddr = ctx.Module.ElemAddrs[(ElemIdx)elemIdx];
                ctx.Store.DropElement(elemAddr);
            }
            else
            {
                ModuleInit.DropElemSegment(elemIdx);
            }
        }

        public static void TableCopy(TranspiledContext ctx, int dstTableIdx, int srcTableIdx,
            int dst, int src, int len)
        {
            var dstTable = ctx.Tables[dstTableIdx];
            var srcTable = ctx.Tables[srcTableIdx];
            if ((long)(uint)src + (long)(uint)len > srcTable.Elements.Count ||
                (long)(uint)dst + (long)(uint)len > dstTable.Elements.Count)
                throw new TrapException("out of bounds table access");
            if (len == 0) return;
            // Handle overlapping copy
            if (dst <= src)
            {
                for (int i = 0; i < len; i++)
                    dstTable.Elements[dst + i] = srcTable.Elements[src + i];
            }
            else
            {
                for (int i = len - 1; i >= 0; i--)
                    dstTable.Elements[dst + i] = srcTable.Elements[src + i];
            }
        }
    }
}
