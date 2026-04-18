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
    /// [OpHandler] entry points for control-flow ops on the monolithic-switch path.
    /// Only the "exits the current function cleanly" family is handled here; branches
    /// (br/br_if/br_table) and block-structure ops (block/loop/if/else) come in the
    /// control-flow phase of the plan along with their pre-resolved target triples.
    /// </summary>
    internal static class ControlHandlers
    {
        // 0x0F return — terminates the current function body.
        // Pushing pc past the end of the stream exits SwitchRuntime.Run's while loop
        // cleanly, leaving whatever result values the producer already put on the
        // OpStack in place. (Validation guarantees the stack shape matches the
        // function's result type at this point.)
        [OpHandler(OpCode.Return)]
        private static void Return(ref int pc) => pc = int.MaxValue;
    }
}
