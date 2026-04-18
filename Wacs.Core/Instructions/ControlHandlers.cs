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

namespace Wacs.Core.Instructions
{
    /// <summary>
    /// [OpHandler] entry points for control-flow ops on the monolithic-switch path.
    /// Only the "exits the current function cleanly" and direct call families are
    /// handled here; branches (br/br_if/br_table) and block-structure ops
    /// (block/loop/if/else) come in the control-flow phase along with their
    /// pre-resolved target triples.
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

        // 0x10 call — direct call by function index.
        // Resolves the callee via the caller's module, compiles its body on demand,
        // pops args into a fresh frame, runs the callee's compiled stream via
        // managed recursion, then restores the caller's frame. Host functions
        // delegate to the existing polymorphic .Invoke — it already reads args
        // off the OpStack and pushes results, which is all we need.
        [OpHandler(OpCode.Call)]
        private static void Call(ExecContext ctx, [Imm] uint funcIdx)
        {
            var addr = ctx.Frame.Module.FuncAddrs[(FuncIdx)funcIdx];
            var target = ctx.Store[addr];
            if (target is FunctionInstance wasmFunc)
            {
                // Qualified call so the body stays resolvable when the generator
                // inlines it into GeneratedDispatcher (different class, same assembly).
                ControlHandlers.InvokeWasm(ctx, wasmFunc);
                return;
            }
            // HostFunction and anything else — hand off to the canonical polymorphic
            // invocation, which knows how to marshal host params/returns via the
            // OpStack.
            target.Invoke(ctx);
        }

        internal static void InvokeWasm(ExecContext ctx, FunctionInstance func)
        {
            var compiled = CompiledFunctionCache.GetOrCompile(func);

            // Args were pushed left-to-right on the OpStack; pop right-to-left into
            // the locals array starting at the param slot, then zero-init declared
            // locals for the tail.
            int paramCount = func.Type.ParameterTypes.Arity;
            int declaredCount = func.Locals.Length;
            var locals = new Value[paramCount + declaredCount];
            for (int i = paramCount - 1; i >= 0; i--)
            {
                locals[i] = ctx.OpStack.PopAny();
            }
            for (int i = 0; i < declaredCount; i++)
            {
                locals[paramCount + i] = new Value(func.Locals[i]);
            }

            // Swap the frame for the duration of the call. The simple Frame construction
            // matches what LocalGet/Set expect (Locals Memory + Module reference); we're
            // not using the existing ObjectPool here to keep the switch path standalone.
            var savedFrame = ctx.Frame;
            ctx.Frame = new Frame { Module = func.Module, Locals = locals };
            try
            {
                SwitchRuntime.Run(ctx, compiled.Bytecode);
            }
            finally
            {
                ctx.Frame = savedFrame;
            }
        }
    }
}
