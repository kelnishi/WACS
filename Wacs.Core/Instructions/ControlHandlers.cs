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
using Wacs.Core.Runtime.Exceptions;
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
        // 0x00 unreachable — trap.
        [OpHandler(OpCode.Unreachable)]
        private static void Unreachable()
            => throw new TrapException("unreachable");

        // 0x01 nop — do nothing. Kept as an explicit handler (instead of elision in the
        // compiler) so the op still occupies a byte in the stream — useful for debugging
        // and benchmarking (you can see the cost of one fetch+dispatch cycle).
        [OpHandler(OpCode.Nop)]
        private static void Nop() { }

        // 0x0C br — unconditional branch. Stream: [target_pc:u32][results_height:u32][arity:u32].
        // results_height is the final OpStack height after the branch (label entry height +
        // arity). ShiftResults preserves the top `arity` values, truncates in between, and
        // leaves exactly results_height entries on the stack.
        [OpHandler(OpCode.Br)]
        private static void Br(ExecContext ctx, ref int pc,
                               [Imm] uint targetPc, [Imm] uint resultsHeight, [Imm] uint arity)
        {
            ctx.OpStack.ShiftResults((int)arity, (int)resultsHeight);
            pc = (int)targetPc;
        }

        // 0x0D br_if — conditional branch. Same stream layout as br. Pops the i32 condition
        // first; branches only if non-zero. Falls through otherwise (pc already past its
        // 12 bytes of immediates by the time the handler is called).
        [OpHandler(OpCode.BrIf)]
        private static void BrIf(ExecContext ctx, ref int pc,
                                 [Imm] uint targetPc, [Imm] uint resultsHeight, [Imm] uint arity)
        {
            int cond = ctx.OpStack.PopI32();
            if (cond != 0)
            {
                ctx.OpStack.ShiftResults((int)arity, (int)resultsHeight);
                pc = (int)targetPc;
            }
        }

        // 0x04 if — pops an i32 condition. If zero, jump to else_pc (first instruction of the
        // else-body, or past End if the if has no else). Otherwise fall through into the
        // then-body. Stream: [else_pc:u32].
        [OpHandler(OpCode.If)]
        private static void If(ExecContext ctx, ref int pc, [Imm] uint elsePc)
        {
            if (ctx.OpStack.PopI32() == 0)
                pc = (int)elsePc;
        }

        // 0x05 else — unconditional jump past End. Reached only when falling through from the
        // end of the then-body; the then-body skips this via the If handler. Stream: [end_pc:u32].
        [OpHandler(OpCode.Else)]
        private static void Else(ref int pc, [Imm] uint endPc)
            => pc = (int)endPc;

        // 0x0E br_table — indirect branch via an inline jump table. Stream layout:
        //   [opcode][count:u32][triple × count (indexed)][default triple] ; triple = 12 bytes.
        // Pop the i32 selector. If in [0, count), take triple i; otherwise take the default
        // (which sits at slot `count`). Each triple drives the same ShiftResults + pc jump
        // as plain br.
        [OpHandler(OpCode.BrTable)]
        private static void BrTable(ExecContext ctx, System.ReadOnlySpan<byte> code, ref int pc)
        {
            uint count = StreamReader.ReadU32(code, ref pc);
            int i = ctx.OpStack.PopI32();
            int entryIdx = ((uint)i < count) ? i : (int)count;
            // pc now points at the first byte of triple[0]. Skip to triple[entryIdx].
            int triplePc = pc + entryIdx * 12;
            uint targetPc      = StreamReader.ReadU32(code, ref triplePc);
            uint resultsHeight = StreamReader.ReadU32(code, ref triplePc);
            uint arity         = StreamReader.ReadU32(code, ref triplePc);
            ctx.OpStack.ShiftResults((int)arity, (int)resultsHeight);
            pc = (int)targetPc;
        }

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
