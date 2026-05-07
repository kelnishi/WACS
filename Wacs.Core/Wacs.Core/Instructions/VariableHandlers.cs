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
using Wacs.Core.Types;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// [OpHandler] entry points for local/global access on the monolithic-switch path.
    /// Each reads a u32 index out of the annotated stream (fixed-width, pre-decoded from
    /// LEB128 at compile time) and interacts with <c>ctx.Frame.Locals</c> or the
    /// Store-backed global slot. The original polymorphic Execute methods remain untouched.
    /// </summary>
    internal static class VariableHandlers
    {
        // 0x20 local.get — stream: [u32 local index]
        [OpHandler(OpCode.LocalGet)]
        private static void LocalGet(ExecContext ctx, [Imm] uint idx)
            => ctx.OpStack.PushValue(ctx.Frame.Locals.Span[(int)idx]);

        // 0x21 local.set — stream: [u32 local index]; pops one value off the stack.
        [OpHandler(OpCode.LocalSet)]
        private static void LocalSet(ExecContext ctx, [Imm] uint idx, Value v)
            => ctx.Frame.Locals.Span[(int)idx] = v;

        // 0x22 local.tee — stream: [u32 local index]; pops+pushes, writes to local.
        [OpHandler(OpCode.LocalTee)]
        private static void LocalTee(ExecContext ctx, [Imm] uint idx)
        {
            var v = ctx.OpStack.PopAny();
            ctx.Frame.Locals.Span[(int)idx] = v;
            ctx.OpStack.PushValue(v);
        }

        // 0x23 global.get — stream: [u32 global index]
        [OpHandler(OpCode.GlobalGet)]
        private static void GlobalGet(ExecContext ctx, [Imm] uint idx)
        {
            var addr = ctx.Frame.Module.GlobalAddrs[(GlobalIdx)idx];
            var glob = ctx.Store[addr];
            // Layer 5c: thread-local globals route through the per-thread slot.
            var val = glob.IsThreadLocal
                ? ctx.GetThreadLocalGlobalValue(addr, glob.Value)
                : glob.Value;
            ctx.OpStack.PushValue(val);
        }

        // 0x24 global.set — stream: [u32 global index]; pops one value.
        [OpHandler(OpCode.GlobalSet)]
        private static void GlobalSet(ExecContext ctx, [Imm] uint idx, Value v)
        {
            var addr = ctx.Frame.Module.GlobalAddrs[(GlobalIdx)idx];
            var glob = ctx.Store[addr];
            if (glob.IsThreadLocal)
            {
                ctx.SetThreadLocalGlobalValue(addr, v);
            }
            else
            {
                glob.Value = v;
            }
        }
    }
}
