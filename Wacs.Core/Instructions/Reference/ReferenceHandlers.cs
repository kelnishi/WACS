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
    }
}
