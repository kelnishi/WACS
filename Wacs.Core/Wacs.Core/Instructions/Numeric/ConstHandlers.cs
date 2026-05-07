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

namespace Wacs.Core.Instructions.Numeric
{
    /// <summary>
    /// [OpHandler] entry points for the four t.const opcodes on the monolithic-switch path.
    /// Each handler reads its constant out of the annotated bytecode stream (fixed-width,
    /// already pre-decoded from LEB128 / IEEE at validation time) and pushes it onto the
    /// OpStack. The original <c>InstI32Const.Execute</c> et al remain the polymorphic path.
    /// </summary>
    internal static class ConstHandlers
    {
        // 0x41 i32.const — stream: [s32 constant]
        [OpHandler(OpCode.I32Const)]
        private static void I32Const(ExecContext ctx, [Imm] int value)
            => ctx.OpStack.PushI32(value);

        // 0x42 i64.const — stream: [s64 constant]
        [OpHandler(OpCode.I64Const)]
        private static void I64Const(ExecContext ctx, [Imm] long value)
            => ctx.OpStack.PushI64(value);

        // 0x43 f32.const — stream: [4 bytes IEEE]
        [OpHandler(OpCode.F32Const)]
        private static void F32Const(ExecContext ctx, [Imm] float value)
            => ctx.OpStack.PushF32(value);

        // 0x44 f64.const — stream: [8 bytes IEEE]
        [OpHandler(OpCode.F64Const)]
        private static void F64Const(ExecContext ctx, [Imm] double value)
            => ctx.OpStack.PushF64(value);
    }
}
