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

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// [OpHandler] entry points for the super-instructions produced by
    /// <see cref="Wacs.Core.Compilation.StreamFusePass"/>. Each fuses a common
    /// 2-op or 3-op wasm sequence into a single dispatch — the inlined handler body
    /// does the combined work in one switch case, so we save the per-op
    /// fetch/decode/dispatch cost for the elided ops.
    ///
    /// <para>Super-op encodings all live under the <c>0xFF</c> <see cref="WacsCode"/>
    /// prefix. The fuser runs over the annotated bytecode stream; when it matches
    /// one of these patterns with no branch target landing mid-pattern, it
    /// substitutes the super-op encoding in place, shrinking the stream and
    /// remapping branch targets on the fly.</para>
    ///
    /// <para>All super-ops here are redundant for correctness — the fuser can be
    /// disabled via <see cref="Runtime.RuntimeAttributes.UseSwitchSuperInstructions"/>
    /// and the runtime will execute the unfused sequences with identical semantics.</para>
    /// </summary>
    internal static class SuperInstructionHandlers
    {
        // Pattern: local.get $from ; local.set $to
        // Encoding: [0xFF][0x40][from:u32][to:u32]
        // Body: a single Value-copy between the two local slots. No stack traffic.
        [OpHandler(WacsCode.LocalGetSet)]
        private static void LocalGetSet(ExecContext ctx, [Imm] uint from, [Imm] uint to)
        {
            var locals = ctx.Frame.Locals.Span;
            locals[(int)to] = locals[(int)from];
        }

        // Pattern: i32.const $k ; local.set $to
        // Encoding: [0xFF][0x41][k:s32][to:u32]
        // Body: write a new i32 Value into the local slot. Skips the OpStack
        // push-then-pop that the unfused sequence would do.
        [OpHandler(WacsCode.LocalConstSet)]
        private static void LocalI32ConstSet(ExecContext ctx, [Imm] int k, [Imm] uint to)
        {
            ctx.Frame.Locals.Span[(int)to] = new Value(k);
        }

        // Pattern: i64.const $k ; local.set $to
        // Encoding: [0xFF][0x42][k:s64][to:u32]
        [OpHandler(WacsCode.LocalI64ConstSet)]
        private static void LocalI64ConstSet(ExecContext ctx, [Imm] long k, [Imm] uint to)
        {
            ctx.Frame.Locals.Span[(int)to] = new Value(k);
        }

        // Pattern: local.get $idx ; i32.const $k ; i32.add
        // Encoding: [0xFF][0x30][idx:u32][k:s32]
        // Body: push locals[idx].i32 + k. Skips the intermediate stack traffic
        // (push local, push const, pop+pop+add+push).
        [OpHandler(WacsCode.I32FusedAdd)]
        private static int I32FusedAdd(ExecContext ctx, [Imm] uint idx, [Imm] int k)
            => ctx.Frame.Locals.Span[(int)idx].Data.Int32 + k;

        // Pattern: local.get $idx ; i32.const $k ; i32.sub
        // Encoding: [0xFF][0x31][idx:u32][k:s32]
        [OpHandler(WacsCode.I32FusedSub)]
        private static int I32FusedSub(ExecContext ctx, [Imm] uint idx, [Imm] int k)
            => ctx.Frame.Locals.Span[(int)idx].Data.Int32 - k;

        // Pattern: local.get $idx ; i32.const $k ; i32.mul
        // Encoding: [0xFF][0x32][idx:u32][k:s32]
        [OpHandler(WacsCode.I32FusedMul)]
        private static int I32FusedMul(ExecContext ctx, [Imm] uint idx, [Imm] int k)
            => unchecked(ctx.Frame.Locals.Span[(int)idx].Data.Int32 * k);

        // Pattern: local.get $idx ; i32.const $k ; i32.and
        // Encoding: [0xFF][0x33][idx:u32][k:s32]
        [OpHandler(WacsCode.I32FusedAnd)]
        private static uint I32FusedAnd(ExecContext ctx, [Imm] uint idx, [Imm] uint k)
            => ctx.Frame.Locals.Span[(int)idx].Data.UInt32 & k;

        // Pattern: local.get $idx ; i64.const $k ; i64.add
        // Encoding: [0xFF][0x38][idx:u32][k:s64]
        [OpHandler(WacsCode.I64FusedAdd)]
        private static long I64FusedAdd(ExecContext ctx, [Imm] uint idx, [Imm] long k)
            => ctx.Frame.Locals.Span[(int)idx].Data.Int64 + k;
    }
}
