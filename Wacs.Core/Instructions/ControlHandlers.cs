// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Buffers;
using System.Linq;
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

        // 0x12 return_call — tail call. Behaviorally equivalent to `call x; return`.
        // True tail-call optimization (reusing the current C# stack frame) would require
        // an explicit SwitchFrame stack in SwitchRuntime; the plan flagged this. For now
        // the implementation is correct but still grows the managed call stack.
        [OpHandler(OpCode.ReturnCall)]
        private static void ReturnCall(ExecContext ctx, ref int pc, [Imm] uint funcIdx)
        {
            var addr = ctx.Frame.Module.FuncAddrs[(FuncIdx)funcIdx];
            var target = ctx.Store[addr];
            if (target is FunctionInstance wasmFunc)
            {
                // True tail call: hand the callee to the enclosing InvokeWasm loop so
                // it replaces our frame in place rather than growing C# stack. The
                // caller's try_tables drop out of scope automatically because we exit
                // Run immediately (pc = MAX) before any throw from the callee fires.
                ctx.TailCallPending = wasmFunc;
            }
            else
            {
                // Host tail call has no TCO benefit — the host side runs on a single
                // managed frame already. Invoke it directly.
                target.Invoke(ctx);
            }
            pc = int.MaxValue;
        }

        // 0x11 call_indirect — indirect call through a table. Stream:
        // [opcode][typeIdx:u32][tableIdx:u32]. Pops i32 selector, resolves the table
        // slot to a funcref, type-checks against the expected signature, dispatches.
        [OpHandler(OpCode.CallIndirect)]
        private static void CallIndirect(ExecContext ctx, [Imm] uint typeIdx, [Imm] uint tableIdx, uint idx)
        {
            var tab = ctx.Store[ctx.Frame.Module.TableAddrs[(TableIdx)tableIdx]];
            if (idx >= (uint)tab.Elements.Count)
                throw new TrapException($"call_indirect: undefined element ({idx} >= {tab.Elements.Count})");

            var reference = tab.Elements[(int)idx];
            if (reference.IsNullRef)
                throw new TrapException("call_indirect: null reference");

            var ftExpect = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var funcAddr = reference.GetFuncAddr(ctx.Frame.Module.Types);
            var funcInst = ctx.Store[funcAddr];

            // Type check: defensive — WASM modules must validate at instantiation, but a
            // table slot can be written post-instantiation via table.set, so recheck here.
            if (funcInst is FunctionInstance ftAct &&
                !ftAct.DefType.Matches(ftExpect, ctx.Frame.Module.Types))
            {
                throw new TrapException("call_indirect: indirect call type mismatch");
            }
            if (!funcInst.Type.Matches(ftExpect.Unroll.Body, ctx.Frame.Module.Types))
                throw new TrapException("call_indirect: indirect call type mismatch");

            if (funcInst is FunctionInstance wasmFn)
            {
                ControlHandlers.InvokeWasm(ctx, wasmFn);
                return;
            }
            funcInst.Invoke(ctx);
        }

        // 0x14 call_ref — indirect call through a typed funcref (no table). Stream:
        // [typeIdx:u32]. Pops the funcref, type-checks, dispatches. Traps on null.
        [OpHandler(OpCode.CallRef)]
        private static void CallRef(ExecContext ctx, [Imm] uint typeIdx, Value refVal)
        {
            if (refVal.IsNullRef)
                throw new TrapException("call_ref: null reference");
            var ftExpect = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var funcInst = ctx.Store[refVal.GetFuncAddr(ctx.Frame.Module.Types)];
            if (!funcInst.Type.Matches((FunctionType)ftExpect.Expansion, ctx.Frame.Module.Types))
                throw new TrapException("call_ref: type mismatch");
            if (funcInst is FunctionInstance wasmFn)
            {
                ControlHandlers.InvokeWasm(ctx, wasmFn);
                return;
            }
            funcInst.Invoke(ctx);
        }

        // 0x15 return_call_ref — tail-call variant. Same caveat as other tail calls:
        // behaviorally correct, not stack-optimized under managed recursion.
        [OpHandler(OpCode.ReturnCallRef)]
        private static void ReturnCallRef(ExecContext ctx, ref int pc, [Imm] uint typeIdx, Value refVal)
        {
            if (refVal.IsNullRef)
                throw new TrapException("return_call_ref: null reference");
            var ftExpect = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var funcInst = ctx.Store[refVal.GetFuncAddr(ctx.Frame.Module.Types)];
            if (!funcInst.Type.Matches((FunctionType)ftExpect.Expansion, ctx.Frame.Module.Types))
                throw new TrapException("return_call_ref: type mismatch");
            if (funcInst is FunctionInstance wasmFn)
                ctx.TailCallPending = wasmFn;
            else
                funcInst.Invoke(ctx);
            pc = int.MaxValue;
        }

        // 0x13 return_call_indirect — tail-call variant of call_indirect. Same caveat as
        // return_call: semantically correct, not stack-optimized.
        [OpHandler(OpCode.ReturnCallIndirect)]
        private static void ReturnCallIndirect(ExecContext ctx, ref int pc,
                                                [Imm] uint typeIdx, [Imm] uint tableIdx, uint idx)
        {
            var tab = ctx.Store[ctx.Frame.Module.TableAddrs[(TableIdx)tableIdx]];
            if (idx >= (uint)tab.Elements.Count)
                throw new TrapException($"return_call_indirect: undefined element ({idx} >= {tab.Elements.Count})");
            var reference = tab.Elements[(int)idx];
            if (reference.IsNullRef)
                throw new TrapException("return_call_indirect: null reference");
            var ftExpect = ctx.Frame.Module.Types[(TypeIdx)typeIdx];
            var funcInst = ctx.Store[reference.GetFuncAddr(ctx.Frame.Module.Types)];
            if (funcInst is FunctionInstance ftAct &&
                !ftAct.DefType.Matches(ftExpect, ctx.Frame.Module.Types))
            {
                throw new TrapException("return_call_indirect: indirect call type mismatch");
            }
            if (!funcInst.Type.Matches(ftExpect.Unroll.Body, ctx.Frame.Module.Types))
                throw new TrapException("return_call_indirect: indirect call type mismatch");
            if (funcInst is FunctionInstance wasmFn)
                ctx.TailCallPending = wasmFn;
            else
                funcInst.Invoke(ctx);
            pc = int.MaxValue;
        }

        /// <summary>
        /// Top-level entry point for invoking a WASM function under the switch runtime.
        /// Phase M: sets up the initial Frame + locals, then invokes the iterative
        /// dispatcher which drives the entire call tree inside a single native frame.
        /// Call/CallIndirect/CallRef opcodes (and their tail-call variants) push/pop
        /// <see cref="Wacs.Core.Compilation.SwitchCallFrame"/> records on
        /// <c>ctx._switchCallStack</c> — no native recursion per WASM call.
        /// </summary>
        internal static void InvokeWasm(ExecContext ctx, FunctionInstance func)
        {
            var savedFrame = ctx.Frame;
            int savedPcBefore = ctx.SwitchPcBefore;

            // ---- Compile on demand --------------------------------------------------
            var compiled = func.SwitchCompiled;
            if (compiled == null)
            {
                compiled = BytecodeCompiler.Compile(
                    func.Body.Instructions.Flatten().ToArray(),
                    func.Type,
                    localsCount: func.Type.ParameterTypes.Arity + func.Locals.Length,
                    useSuperInstructions: ctx.Attributes.UseSwitchSuperInstructions,
                    declaredLocalTypes: func.Locals);
                func.SwitchCompiled = compiled;
            }

            int paramCount = compiled.ParamCount;
            int totalCount = compiled.LocalsCount;

            // ---- Rent entry frame's locals + pop args off caller's OpStack ---------
            var rented = ArrayPool<Value>.Shared.Rent(totalCount);
            var locals = new System.Memory<Value>(rented, 0, totalCount);
            var span = locals.Span;
            if (compiled.DefaultLocalsTemplate != null)
                System.Array.Copy(compiled.DefaultLocalsTemplate, 0, rented, 0, totalCount);
            for (int i = paramCount - 1; i >= 0; i--)
                span[i] = ctx.OpStack.PopAny();

            var frame = ctx.RentFrame();
            frame.Module = func.Module;
            frame.Locals = locals;
            ctx.Frame = frame;

            try
            {
                // Dispatch the whole call tree. Run iterates through every nested
                // call/return until the entry frame exits naturally or throws.
                Wacs.Core.Compilation.SwitchRuntime.Run(ctx, compiled);
            }
            finally
            {
                // Defensive unwind for any residue left on ctx._switchCallStack — can
                // happen when a non-WasmException (WasmRuntimeException, out-of-range,
                // null-ref…) propagates out of Run and we never entered the unwinder.
                // Each pop: the stack entry records the CALLER's state (WasmFrame) and
                // the CALLEE's rented locals (CalleeRentedLocals); at this moment
                // ctx.Frame is the callee (we switched to it on push), so we return it
                // here and walk back up to the caller.
                while (ctx._switchCallStack.Count > 0)
                {
                    var popped = ctx._switchCallStack.Pop();
                    ArrayPool<Value>.Shared.Return(popped.CalleeRentedLocals, clearArray: true);
                    ctx.ReturnFrame(ctx.Frame);
                    ctx.Frame = popped.WasmFrame;
                }
                // After the loop ctx.Frame is the entry frame we set at the top. Return
                // it + the entry rented array, then restore the outer caller's frame.
                ctx.Frame = savedFrame;
                ctx.SwitchPcBefore = savedPcBefore;
                ctx.ReturnFrame(frame);
                ArrayPool<Value>.Shared.Return(rented, clearArray: true);
            }
        }
    }
}
