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
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.GC
{
    /// <summary>
    /// [OpHandler] entries for ref.test / ref.test_null / ref.cast / ref.cast_null.
    /// Each carries a 4-byte immediate: the heap-type bits encoded as ValType (an int-
    /// backed flags enum). The nullable-ref flag is part of those bits, so
    /// <c>ValType.Matches</c> handles null acceptance correctly without needing a
    /// separate flag in the stream — the .null-suffixed opcodes just have the flag set
    /// in their emitted heap type.
    /// </summary>
    internal static class RefCastHandlers
    {
        // 0xFB 14 ref.test — pop ref, push 1 if the ref's type matches the target heap type,
        // else 0. Non-null variant: nullable ref never matches a non-nullable heap type.
        [OpHandler(GcCode.RefTest)]
        private static int RefTest(ExecContext ctx, [Imm] int heapTypeBits, Value refVal)
            => ((ValType)heapTypeBits).Matches(refVal, ctx.Frame.Module.Types) ? 1 : 0;

        // 0xFB 15 ref.test null — same shape; the emitted heap type already includes the
        // nullable-ref flag. Kept as a separate handler so the dispatcher table stays
        // flat — the generator doesn't fan out one handler to multiple opcodes.
        [OpHandler(GcCode.RefTestNull)]
        private static int RefTestNull(ExecContext ctx, [Imm] int heapTypeBits, Value refVal)
            => ((ValType)heapTypeBits).Matches(refVal, ctx.Frame.Module.Types) ? 1 : 0;

        // 0xFB 16 ref.cast — pop ref, trap if the type doesn't match, else push ref back.
        [OpHandler(GcCode.RefCast)]
        private static Value RefCast(ExecContext ctx, [Imm] int heapTypeBits, Value refVal)
        {
            if (!((ValType)heapTypeBits).Matches(refVal, ctx.Frame.Module.Types))
                throw new TrapException("ref.cast: cast failure");
            return refVal;
        }

        // 0xFB 17 ref.cast null — same as ref.cast but the emitted heap type carries the
        // nullable flag, so null refs pass through without trapping.
        [OpHandler(GcCode.RefCastNull)]
        private static Value RefCastNull(ExecContext ctx, [Imm] int heapTypeBits, Value refVal)
        {
            if (!((ValType)heapTypeBits).Matches(refVal, ctx.Frame.Module.Types))
                throw new TrapException("ref.cast null: cast failure");
            return refVal;
        }
    }
}
