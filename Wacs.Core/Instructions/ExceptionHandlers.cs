// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Collections.Generic;
using Wacs.Core.Compilation;
using Wacs.Core.OpCodes;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// [OpHandler] entries for the exception-handling opcodes. Throw / throw_ref raise
    /// a C# <see cref="WasmException"/>; the outer <c>SwitchRuntime.Run</c> loop catches
    /// it, scans the current function's HandlerTable for a matching entry covering the
    /// current pc, and either resumes at the catch handler or rethrows to unwind one
    /// managed-recursion frame to the caller.
    ///
    /// try_table itself is elided from the annotated stream — the HandlerTable sidecar
    /// on CompiledFunction (built by BytecodeCompiler's pass 3) carries all of its
    /// state. No runtime work is needed at try_table's position in the stream.
    /// </summary>
    internal static class ExceptionHandlers
    {
        // 0x08 throw — pop N parameters (from tag's type), allocate an ExnInstance,
        // raise via WasmException. The Run-loop handler walks the HandlerTable.
        [OpHandler(OpCode.Throw)]
        private static void Throw(ExecContext ctx, [Imm] uint tagIdx)
        {
            var ta = ctx.Frame.Module.TagAddrs[(TagIdx)tagIdx];
            var ti = ctx.Store[ta];
            var funcType = (FunctionType)ti.Type.Expansion;
            int arity = funcType.ParameterTypes.Arity;

            Stack<Value> valn = new();
            ctx.OpStack.PopResults(arity, ref valn);
            var ea = ctx.Store.AllocateExn(ta, valn);
            var exn = ctx.Store[ea];
            throw new WasmException(exn);
        }

        // 0x0A throw_ref — pop exnref and re-raise. Traps if the reference is null.
        [OpHandler(OpCode.ThrowRef)]
        private static void ThrowRef(ExecContext ctx)
        {
            var exnref = ctx.OpStack.PopAny();
            if (exnref.IsNullRef)
                throw new TrapException("throw_ref: null reference");
            throw new WasmException((ExnInstance)exnref.GcRef);
        }
    }
}
