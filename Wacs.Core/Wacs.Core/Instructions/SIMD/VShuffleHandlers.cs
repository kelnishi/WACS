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

namespace Wacs.Core.Instructions.SIMD
{
    /// <summary>
    /// [OpHandler] entries for the byte-shuffle / byte-swizzle ops.
    ///
    /// i8x16.shuffle carries a 16-byte lane-index immediate (each byte is a lane
    /// index into the concatenation of the two source vectors, lanes 0..15 select
    /// from a, 16..31 from b). We reuse the V128 type as a 16-byte container for the
    /// immediate and decode each lane via V128's byte indexer.
    ///
    /// i8x16.swizzle takes no immediate — it reads the indices from its second stack
    /// operand. Out-of-range indices (>=16) produce 0 in the result lane, matching
    /// the spec.
    /// </summary>
    internal static class VShuffleHandlers
    {
        [OpHandler(SimdCode.I8x16Shuffle)]
        private static V128 I8x16Shuffle([Imm] V128 lanes, V128 a, V128 b)
        {
            MV128 r = new V128();
            for (byte i = 0; i < 16; i++)
            {
                byte idx = lanes[i];
                r[i] = idx < 16 ? a[idx] : b[(byte)(idx - 16)];
            }
            return r;
        }

        [OpHandler(SimdCode.I8x16Swizzle)]
        private static V128 I8x16Swizzle(V128 a, V128 idxs)
        {
            MV128 r = new V128();
            for (byte i = 0; i < 16; i++)
            {
                byte idx = idxs[i];
                r[i] = idx < 16 ? a[idx] : (byte)0;
            }
            return r;
        }
    }
}
