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
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.Reference
{
    /// <summary>
    /// [OpSource] / [OpHandler] entry points for the stack-only reference instructions:
    /// ref.is_null (0xD1), ref.as_non_null (0xD4), ref.eq (0xD3). The more involved
    /// reference ops — ref.null (takes a ValType immediate), ref.func (takes a FuncIdx
    /// and refines the reference type at runtime), and br_on_null/br_on_non_null
    /// (conditional branches with the same triple format as br) — come in a later
    /// commit.
    /// </summary>
    internal static class ReferenceHandlers
    {
        // 0xD1 ref.is_null — pop ref, push 1 if null else 0.
        [OpSource(OpCode.RefIsNull)]
        private static int RefIsNull(Value r) => r.IsNullRef ? 1 : 0;

        // 0xD4 ref.as_non_null — pop ref, trap if null else push back.
        [OpSource(OpCode.RefAsNonNull)]
        private static Value RefAsNonNull(Value r)
        {
            if (r.IsNullRef) throw new TrapException("ref.as_non_null: null reference");
            return r;
        }

        // 0xD3 ref.eq — equal-reference comparison. Needs ExecContext to reach
        // Module.Types for structural compat checks, so it's [OpHandler] not [OpSource].
        [OpHandler(OpCode.RefEq)]
        private static int RefEq(ExecContext ctx, Value v1, Value v2)
            => v1.RefEquals(v2, ctx.Frame.Module.Types) ? 1 : 0;

        // 0xD0 ref.null — takes a ValType immediate (with the nullable-ref flag set,
        // encoded as the underlying int). Pushes a null reference of that type.
        [OpHandler(OpCode.RefNull)]
        private static Value RefNull([Imm] int refTypeBits)
            => Value.Null((ValType)refTypeBits);

        // 0xD2 ref.func — takes a FuncIdx immediate. Pushes a funcref to that function,
        // with the reference type refined to the function's specific def-type (needed by
        // the GC proposal's ref.test / ref.cast precision).
        [OpHandler(OpCode.RefFunc)]
        private static Value RefFunc(ExecContext ctx, [Imm] uint funcIdx)
        {
            var a = ctx.Frame.Module.FuncAddrs[(FuncIdx)funcIdx];
            var func = ctx.Store[a];
            var val = new Value(ValType.FuncRef, a.Value);
            if (func is FunctionInstance funcInst)
                val.Type = ValType.Ref | (ValType)funcInst.DefType.DefIndex;
            return val;
        }

        // 0xD5 br_on_null — pop ref; if null, branch (shifting label-arity results down);
        // else push ref back and fall through. Stream: same triple format as br.
        [OpHandler(OpCode.BrOnNull)]
        private static void BrOnNull(ExecContext ctx, ref int pc,
                                     [Imm] uint targetPc, [Imm] uint resultsHeight, [Imm] uint arity,
                                     Value refVal)
        {
            if (refVal.IsNullRef)
            {
                ctx.OpStack.ShiftResults((int)arity, (int)resultsHeight);
                pc = (int)targetPc;
            }
            else
            {
                // Fall through with the ref back on the stack.
                ctx.OpStack.PushValue(refVal);
            }
        }

        // 0xD6 br_on_non_null — pop ref; if non-null, push it back (it's part of the
        // label's arity per the GC proposal) and branch; else fall through with ref
        // discarded. Same 12-byte triple as br.
        [OpHandler(OpCode.BrOnNonNull)]
        private static void BrOnNonNull(ExecContext ctx, ref int pc,
                                        [Imm] uint targetPc, [Imm] uint resultsHeight, [Imm] uint arity,
                                        Value refVal)
        {
            if (!refVal.IsNullRef)
            {
                ctx.OpStack.PushValue(refVal);
                ctx.OpStack.ShiftResults((int)arity, (int)resultsHeight);
                pc = (int)targetPc;
            }
            // else: ref was already popped, nothing to do.
        }
    }
}
