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
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.GC
{
    /// <summary>
    /// [OpHandler] entries for br_on_cast / br_on_cast_fail. The stream layout after
    /// the 0xFB 0x18 / 0x19 header:
    ///   - flags:u8     (CastFlags — currently unused at runtime, kept for parity)
    ///   - rt1:i32      (source heap type bits — unused at runtime; validation only)
    ///   - rt2:i32      (target heap type bits — the predicate)
    ///   - targetPc:u32 (pre-resolved branch target)
    ///   - resultsHeight:u32
    ///   - arity:u32
    ///
    /// Semantics: peek the top-of-stack ref value, test whether its runtime type
    /// matches rt2; on match (for BrOnCast) or mismatch (for BrOnCastFail), branch
    /// via the pre-resolved triple. On the other path, fall through.
    /// </summary>
    internal static class BrOnCastHandlers
    {
        // 0xFB 0x18 br_on_cast — branch if the top-of-stack ref matches rt2.
        [OpHandler(GcCode.BrOnCast)]
        private static void BrOnCast(ExecContext ctx, ref int pc,
                                     [Imm] byte flags, [Imm] int rt1, [Imm] int rt2,
                                     [Imm] uint targetPc, [Imm] uint resultsHeight, [Imm] uint arity)
        {
            var refVal = ctx.OpStack.Peek();
            if (((ValType)rt2).Matches(refVal, ctx.Frame.Module.Types))
            {
                ctx.OpStack.ShiftResults((int)arity, (int)resultsHeight);
                pc = (int)targetPc;
            }
        }

        // 0xFB 0x19 br_on_cast_fail — branch if the top-of-stack ref does NOT match rt2.
        [OpHandler(GcCode.BrOnCastFail)]
        private static void BrOnCastFail(ExecContext ctx, ref int pc,
                                         [Imm] byte flags, [Imm] int rt1, [Imm] int rt2,
                                         [Imm] uint targetPc, [Imm] uint resultsHeight, [Imm] uint arity)
        {
            var refVal = ctx.OpStack.Peek();
            if (!((ValType)rt2).Matches(refVal, ctx.Frame.Module.Types))
            {
                ctx.OpStack.ShiftResults((int)arity, (int)resultsHeight);
                pc = (int)targetPc;
            }
        }
    }
}
