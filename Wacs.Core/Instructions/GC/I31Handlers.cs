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
using Wacs.Core.Runtime.GC;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions.GC
{
    /// <summary>
    /// [OpHandler] entry points for the three i31 ops from the GC proposal. These don't
    /// live on the existing polymorphic classes because those Execute methods reference
    /// `this.Op` for error-message mnemonics, which can't be inlined into the generator's
    /// flat dispatcher. Re-implementing here (without the mnemonic-in-error-message
    /// niceties) is cheap and keeps the original class untouched.
    /// </summary>
    internal static class I31Handlers
    {
        // Constants inlined at each use site below — the generator copies handler bodies
        // into GeneratedDispatcher verbatim, so bare references to private consts on this
        // class wouldn't resolve there. Literal values match the masks used by the
        // polymorphic InstRefI31 / InstI31GetS / InstI31GetU.

        // 0xFB 1C ref.i31 — pop an i32 and wrap it in an i31ref.
        [OpHandler(GcCode.RefI31)]
        private static Value RefI31(int raw)
        {
            long i = raw;
            // Sign-extend bit 30 (i31 payload is 31 bits with bit 30 as the sign).
            if ((i & 0x4000_0000) != 0)
                i = (long)(0xFFFF_FFFF_C000_0000ul | unchecked((ulong)i));
            else
                i &= 0x3FFF_FFFF;
            return new Value(ValType.I31NN, i, new I31Ref(i));
        }

        // 0xFB 1D i31.get_s — extract signed payload.
        [OpHandler(GcCode.I31GetS)]
        private static int I31GetS(Value refVal)
        {
            if (refVal.IsNullRef)
                throw new TrapException("i31.get_s: null reference");
            var i31Ref = refVal.GcRef as I31Ref;
            if (i31Ref is null)
                throw new TrapException("i31.get_s: non-i31 reference");
            int j = i31Ref.Value;
            // Restore sign extension that RefI31 trimmed off.
            if (j < 0) j |= 0x4000_0000;
            else       j &= 0x3FFF_FFFF;
            return j;
        }

        // 0xFB 1E i31.get_u — extract unsigned payload.
        [OpHandler(GcCode.I31GetU)]
        private static uint I31GetU(Value refVal)
        {
            if (refVal.IsNullRef)
                throw new TrapException("i31.get_u: null reference");
            var i31Ref = refVal.GcRef as I31Ref;
            if (i31Ref is null)
                throw new TrapException("i31.get_u: non-i31 reference");
            return (uint)i31Ref.Value & 0x7FFF_FFFFu;
        }
    }
}
