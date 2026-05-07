// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// One entry in a function's exception-handler sidecar table, produced at compile
    /// time from <c>InstTryTable.Catches</c>. A thrown exception at pc P selects the
    /// matching entry via <c>Start &lt;= P &lt; End</c> plus a tag-or-catchall check,
    /// and the handler jumps to <see cref="HandlerPc"/> after shifting the stack.
    ///
    /// Catch kind codes match <c>Wacs.Core.Types.Defs.CatchFlags</c>:
    /// 0 = None (catch x), 1 = CatchRef (catch_ref x),
    /// 2 = CatchAll, 3 = CatchAllRef.
    /// </summary>
    public readonly struct HandlerEntry
    {
        public readonly uint StartPc;
        public readonly uint EndPc;
        /// <summary>
        /// Tag index within the module. Meaningful only when <see cref="Kind"/> is
        /// None or CatchRef. For CatchAll / CatchAllRef this is <see cref="uint.MaxValue"/>.
        /// </summary>
        public readonly uint TagIdx;
        public readonly uint HandlerPc;
        public readonly uint ResultsHeight;
        public readonly uint Arity;
        public readonly byte Kind;

        public HandlerEntry(uint startPc, uint endPc, uint tagIdx, uint handlerPc,
                            uint resultsHeight, uint arity, byte kind)
        {
            StartPc = startPc;
            EndPc = endPc;
            TagIdx = tagIdx;
            HandlerPc = handlerPc;
            ResultsHeight = resultsHeight;
            Arity = arity;
            Kind = kind;
        }
    }
}
