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
    /// [OpHandler] entry points for parametric ops (drop, select). The typed-select form
    /// (<c>SelectT</c>, 0x1C) carries a variable-length value-type vector as its immediate
    /// and is deferred — the annotated-stream format will encode it as
    /// <c>[count:u32][types:u8 × count]</c> once select_t lands.
    /// </summary>
    internal static class ParametricHandlers
    {
        // 0x1A drop — pops and discards the top stack value.
        [OpHandler(OpCode.Drop)]
        private static void Drop(ExecContext ctx)
            => ctx.OpStack.PopAny();

        // 0x1B select — pops cond, v2, v1 (in that order); pushes v1 if cond!=0 else v2.
        // Parameters map to stack pops in reverse order, so the signature below produces
        // exactly that pop sequence.
        [OpHandler(OpCode.Select)]
        private static Value Select(Value v1, Value v2, int cond)
            => cond != 0 ? v1 : v2;
    }
}
