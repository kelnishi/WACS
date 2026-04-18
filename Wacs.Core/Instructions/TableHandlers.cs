// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// [OpHandler] entry points for table access + bulk ops on the monolithic-switch path.
    /// table.get / table.set have their own primary opcodes (0x25 / 0x26); the bulk variants
    /// (init / copy / fill / grow / size and elem.drop) share the 0xFC prefix with bulk
    /// memory and select by secondary byte.
    ///
    /// Stream layouts:
    ///   - table.get / table.set               [opcode][tableIdx:u32]               (5 bytes)
    ///   - table.init  [0xFC 0x0C][elemIdx:u32][tableIdx:u32]                        (10)
    ///   - elem.drop   [0xFC 0x0D][elemIdx:u32]                                      (6)
    ///   - table.copy  [0xFC 0x0E][dstIdx:u32][srcIdx:u32]                           (10)
    ///   - table.grow  [0xFC 0x0F][tableIdx:u32]                                     (6)
    ///   - table.size  [0xFC 0x10][tableIdx:u32]                                     (6)
    ///   - table.fill  [0xFC 0x11][tableIdx:u32]                                     (6)
    /// </summary>
    internal static class TableHandlers
    {
        // 0x25 table.get — pop i32 index, push table[idx].
        [OpHandler(OpCode.TableGet)]
        private static Value TableGet(ExecContext ctx, [Imm] uint tableIdx, uint idx)
        {
            var tab = ctx.Store[ctx.Frame.Module.TableAddrs[(TableIdx)tableIdx]];
            if (idx >= (uint)tab.Elements.Count)
                throw new TrapException("table.get: out of bounds table access");
            return tab.Elements[(int)idx];
        }

        // 0x26 table.set — pop ref value (top), pop i32 index, write.
        [OpHandler(OpCode.TableSet)]
        private static void TableSet(ExecContext ctx, [Imm] uint tableIdx, uint idx, Value val)
        {
            var tab = ctx.Store.GetMutableTable(ctx.Frame.Module.TableAddrs[(TableIdx)tableIdx]);
            if (idx >= (uint)tab.Elements.Count)
                throw new TrapException("table.set: out of bounds table access");
            tab.Elements[(int)idx] = val;
        }

        // 0xFC 0C table.init — copy `n` entries from elem[s..s+n] to table[d..d+n].
        [OpHandler(ExtCode.TableInit)]
        private static void TableInit(ExecContext ctx, [Imm] uint elemIdx, [Imm] uint tableIdx,
                                       uint d, uint s, uint n)
        {
            var tab = ctx.Store.GetMutableTable(ctx.Frame.Module.TableAddrs[(TableIdx)tableIdx]);
            var elem = ctx.Store[ctx.Frame.Module.ElemAddrs[(ElemIdx)elemIdx]];
            if ((long)s + n > elem.Elements.Count || (long)d + n > tab.Elements.Count)
                throw new TrapException("table.init: out of bounds table access");
            for (int i = 0; i < n; i++)
                tab.Elements[(int)(d + i)] = elem.Elements[(int)(s + i)];
        }

        // 0xFC 0D elem.drop — invalidate the element segment.
        [OpHandler(ExtCode.ElemDrop)]
        private static void ElemDrop(ExecContext ctx, [Imm] uint elemIdx)
            => ctx.Store.DropElement(ctx.Frame.Module.ElemAddrs[(ElemIdx)elemIdx]);

        // 0xFC 0E table.copy — memmove across (possibly different) tables.
        [OpHandler(ExtCode.TableCopy)]
        private static void TableCopy(ExecContext ctx, [Imm] uint dstIdx, [Imm] uint srcIdx,
                                       uint d, uint s, uint n)
        {
            var src = ctx.Store[ctx.Frame.Module.TableAddrs[(TableIdx)srcIdx]];
            var dst = ctx.Store.GetMutableTable(ctx.Frame.Module.TableAddrs[(TableIdx)dstIdx]);
            if ((long)s + n > src.Elements.Count || (long)d + n > dst.Elements.Count)
                throw new TrapException("table.copy: out of bounds table access");
            // Handle overlap: if destination is above source we must copy from the top,
            // else bottom-up. Matches the spec's memmove semantics for tables.
            if (d <= s)
            {
                for (int i = 0; i < n; i++)
                    dst.Elements[(int)(d + i)] = src.Elements[(int)(s + i)];
            }
            else
            {
                for (int i = (int)n - 1; i >= 0; i--)
                    dst.Elements[(int)(d + i)] = src.Elements[(int)(s + i)];
            }
        }

        // 0xFC 0F table.grow — pop n (count), pop val (init ref); grow. Push old size or
        // -1. Result type matches the table's address type (i32 for 32-bit, i64 for 64-bit).
        [OpHandler(ExtCode.TableGrow)]
        private static Value TableGrow(ExecContext ctx, [Imm] uint tableIdx, Value initVal, uint n)
        {
            var tab = ctx.Store.GetMutableTable(ctx.Frame.Module.TableAddrs[(TableIdx)tableIdx]);
            var at = tab.Type.Limits.AddressType;
            long oldSize = tab.Elements.Count;
            return tab.Grow(n, initVal) ? new Value(at, oldSize) : new Value(at, -1L);
        }

        // 0xFC 10 table.size — push the current element count. Result type matches the
        // table's address type.
        [OpHandler(ExtCode.TableSize)]
        private static Value TableSize(ExecContext ctx, [Imm] uint tableIdx)
        {
            var tab = ctx.Store[ctx.Frame.Module.TableAddrs[(TableIdx)tableIdx]];
            return new Value(tab.Type.Limits.AddressType, (long)tab.Elements.Count);
        }

        // 0xFC 11 table.fill — pop n, val, d; fill table[d..d+n] with val.
        [OpHandler(ExtCode.TableFill)]
        private static void TableFill(ExecContext ctx, [Imm] uint tableIdx,
                                       uint d, Value val, uint n)
        {
            var tab = ctx.Store.GetMutableTable(ctx.Frame.Module.TableAddrs[(TableIdx)tableIdx]);
            if ((long)d + n > tab.Elements.Count)
                throw new TrapException("table.fill: out of bounds table access");
            for (int i = 0; i < n; i++)
                tab.Elements[(int)(d + i)] = val;
        }
    }
}
