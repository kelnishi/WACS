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
                    il.Emit(OpCodes.Ldarg_0); // ThinContext
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

            il.Emit(OpCodes.Ldarg_0); // ThinContext
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
    /// Called from transpiled IL — these need ThinContext for Store access.
    /// </summary>
    public static class BulkHelpers
    {
        /// <summary>
        /// Zero-copy active-segment install: copy <paramref name="len"/> bytes
        /// from an RVA-mapped <see cref="ReadOnlySpan{Byte}"/> (typically
        /// produced by <c>RuntimeHelpers.CreateSpan&lt;byte&gt;</c> over a
        /// <c>DefineInitializedData</c> field) into <paramref name="dst"/>'s
        /// linear-memory backing at <paramref name="dstOffset"/>. Routes
        /// through <see cref="MemoryInstance.AsSpan"/> so both
        /// <c>ManagedArray</c> and <c>NativePointer</c> backings work.
        /// Bounds are transpile-time constants; no defensive checks here.
        /// </summary>
        public static void CopySegmentToMemory(ReadOnlySpan<byte> src, MemoryInstance dst, int dstOffset, int len)
        {
            src.Slice(0, len).CopyTo(dst.AsSpan(dstOffset, len));
        }

        /// <summary>
        /// Register a passive data segment in <see cref="ModuleInit"/> at
        /// the given transpile-time id, materializing the RVA-mapped span
        /// to a heap <see cref="byte"/> array. Called from AotLinked ctor
        /// IL once per Module instance — the underlying registry call is
        /// idempotent on repeat invocations (in-process pre-pass already
        /// populated the id; cross-process load needs the populate).
        /// </summary>
        public static void RegisterPassiveDataSegment(int id, ReadOnlySpan<byte> bytes)
        {
            ModuleInit.RegisterDataSegmentAt(id, bytes.ToArray());
        }

        /// <summary>
        /// Register a passive element segment in <see cref="ModuleInit"/>
        /// from a compact <c>int[]</c> encoding: <c>-1</c> = typed null
        /// funcref; any other value is the function index. Called from
        /// AotLinked ctor IL once per Module instance — the registry
        /// call is idempotent. Funcref / null mix only; GC-typed
        /// element values are gated out by the feasibility check.
        /// </summary>
        public static void RegisterPassiveElemSegment(int id, int[] encodedFuncIndices)
        {
            var values = new Value[encodedFuncIndices.Length];
            for (int j = 0; j < encodedFuncIndices.Length; j++)
            {
                int idx = encodedFuncIndices[j];
                values[j] = idx == -1
                    ? new Value(Wacs.Core.Types.Defs.ValType.FuncRef)
                    : new Value(Wacs.Core.Types.Defs.ValType.FuncRef, idx);
            }
            ModuleInit.RegisterElemSegmentAt(id, values);
        }

        public static void MemoryCopy(ThinContext ctx, int dstMemIdx, int srcMemIdx,
            int dst, int src, int len)
        {
            var dstMem = ctx.Memories[dstMemIdx];
            var srcMem = ctx.Memories[srcMemIdx];
            // Widen each i32 wasm address to nuint up front so the
            // arithmetic and the AsSpan dispatch stay in the unsigned
            // range that NativePointer-backed memories use past 2 GiB.
            // Raw `int` would hit the AsSpan(int, int) overload and
            // wrap to a negative pointer offset on the high half.
            nuint srcEa = (uint)src;
            nuint dstEa = (uint)dst;
            nuint nlen = (uint)len;
            // WASM spec: bounds check applies even when len == 0
            if ((ulong)srcEa + (ulong)nlen > (ulong)srcMem.ByteLength ||
                (ulong)dstEa + (ulong)nlen > (ulong)dstMem.ByteLength)
                throw new TrapException("out of bounds memory access");
            if (len == 0) return;
            // Span<byte>.CopyTo dispatches to Buffer.Memmove and
            // handles overlap correctly across both modes.
            srcMem.AsSpan(srcEa, len).CopyTo(dstMem.AsSpan(dstEa, len));
        }

        public static void MemoryFill(ThinContext ctx, int memIdx,
            int dst, int val, int len)
        {
            var mem = ctx.Memories[memIdx];
            nuint dstEa = (uint)dst;
            nuint nlen = (uint)len;
            // WASM spec: bounds check applies even when len == 0
            if ((ulong)dstEa + (ulong)nlen > (ulong)mem.ByteLength)
                throw new TrapException("out of bounds memory access");
            if (len == 0) return;
            mem.AsSpan(dstEa, len).Fill((byte)val);
        }

        public static void MemoryInit(ThinContext ctx, int memIdx, int dataIdx,
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
                // Standalone: use registered data segments with base offset
                segData = ModuleInit.GetDataSegmentData(ctx.DataSegmentBaseId + dataIdx)
                    ?? Array.Empty<byte>();
            }

            // Source side is a byte[] data segment — bounded ≤ 2 GiB by
            // Array.MaxLength, so int arithmetic on `src` is safe by
            // construction. The dst (guest memory) is the side that
            // can sit past 2 GiB under NativePointer mode.
            nuint dstEa = (uint)dst;
            nuint nlen = (uint)len;
            if ((long)(uint)src + (long)(uint)len > segData.Length ||
                (ulong)dstEa + (ulong)nlen > (ulong)mem.ByteLength)
                throw new TrapException("out of bounds memory access");
            if (len == 0) return;
            new ReadOnlySpan<byte>(segData, src, len).CopyTo(mem.AsSpan(dstEa, len));
        }

        public static void DataDrop(ThinContext ctx, int dataIdx)
        {
            if (ctx.Store != null && ctx.Module != null)
            {
                var dataAddr = ctx.Module.DataAddrs[(DataIdx)dataIdx];
                ctx.Store.DropData(dataAddr);
            }
            else
            {
                // Standalone: replace segment with empty array in registry
                ModuleInit.DropDataSegment(ctx.DataSegmentBaseId + dataIdx);
            }
        }

        public static void TableInit(ThinContext ctx, int tableIdx, int elemIdx,
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
                // Standalone mode: use registered element segments with base offset
                var elemData = ModuleInit.GetElemSegment(ctx.ElemSegmentBaseId + elemIdx);
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

        public static void ElemDrop(ThinContext ctx, int elemIdx)
        {
            if (ctx.Store != null && ctx.Module != null)
            {
                var elemAddr = ctx.Module.ElemAddrs[(ElemIdx)elemIdx];
                ctx.Store.DropElement(elemAddr);
            }
            else
            {
                ModuleInit.DropElemSegment(ctx.ElemSegmentBaseId + elemIdx);
            }
        }

        public static void TableCopy(ThinContext ctx, int dstTableIdx, int srcTableIdx,
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
