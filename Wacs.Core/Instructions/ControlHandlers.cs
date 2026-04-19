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

        internal static void InvokeWasm(ExecContext ctx, FunctionInstance func)
        {
            // Save the caller's frame *once*. The tail-call loop replaces the frame
            // in place on each iteration but always restores to the same caller on
            // exit — one InvokeWasm invocation, regardless of how many tail jumps.
            var savedFrame = ctx.Frame;

            while (true)
            {
                // ---- 1. Compiled form: field cache on the FunctionInstance ------------
                // Field read + one null check replaces a ConcurrentDictionary lookup. The
                // compile is deterministic, so a benign race just duplicates work; the last
                // writer wins and either compiled form is equally valid.
                var compiled = func.SwitchCompiled;
                if (compiled == null)
                {
                    compiled = BytecodeCompiler.Compile(
                        func.Body.Instructions.Flatten().ToArray(),
                        func.Type,
                        localsCount: func.Type.ParameterTypes.Arity + func.Locals.Length);
                    func.SwitchCompiled = compiled;
                }

                int paramCount = func.Type.ParameterTypes.Arity;
                int declaredCount = func.Locals.Length;
                int totalCount = paramCount + declaredCount;

                // ---- 2. Locals: pooled Value[] instead of per-call heap alloc ---------
                // ArrayPool returns an array of AT LEAST totalCount elements; we slice into
                // a Memory<Value> of exactly totalCount so LocalGet/Set don't see padding.
                var rented = ArrayPool<Value>.Shared.Rent(totalCount);
                var locals = new System.Memory<Value>(rented, 0, totalCount);
                var span = locals.Span;
                for (int i = paramCount - 1; i >= 0; i--)
                    span[i] = ctx.OpStack.PopAny();
                for (int i = 0; i < declaredCount; i++)
                    span[paramCount + i] = new Value(func.Locals[i]);

                // ---- 3. Frame: pooled via ExecContext._framePool ----------------------
                var frame = ctx.RentFrame();
                frame.Module = func.Module;
                frame.Locals = locals;

                // ---- 3b. Bounded-depth check ------------------------------------------
                // The managed C# stack is finite and small (~32 MiB on our worker thread).
                // A runaway WASM recursion must surface as a WasmRuntimeException ("call
                // stack exhausted") before blowing the native stack or the OpStack array —
                // otherwise we crash the process instead of failing the test cleanly. This
                // mirrors ExecContext.PushFrame's MaxCallStack check on the polymorphic
                // path. Tail calls do NOT increment depth: they reuse this InvokeWasm.
                if (ctx.SwitchCallDepth >= ctx.Attributes.SwitchMaxCallStack)
                {
                    ArrayPool<Value>.Shared.Return(rented, clearArray: true);
                    ctx.ReturnFrame(frame);
                    throw new Wacs.Core.Runtime.Exceptions.WasmRuntimeException(
                        $"Runtime call stack exhausted {ctx.SwitchCallDepth}");
                }

                ctx.Frame = frame;
                ctx.SwitchCallDepth++;
                bool tailContinue = false;
                try
                {
                    // Pass the CompiledFunction (not just Bytecode) so Run can consult the
                    // HandlerTable sidecar and resume WASM exceptions at matching catch
                    // clauses instead of propagating them out of the function.
                    SwitchRuntime.Run(ctx, compiled);

                    // A return_call handler signals a tail-dispatch by stashing the next
                    // callee on ctx.TailCallPending and setting pc to int.MaxValue so Run
                    // exits immediately. We loop here with the new callee, keeping the
                    // same C# stack frame — that's how true TCO is implemented on top of
                    // a managed recursion model. SwitchCallDepth is decremented and re-
                    // incremented across the iteration so it stays flat across tail jumps.
                    if (ctx.TailCallPending != null)
                    {
                        func = ctx.TailCallPending;
                        ctx.TailCallPending = null;
                        tailContinue = true;
                    }
                }
                finally
                {
                    ctx.SwitchCallDepth--;
                    ctx.Frame = savedFrame;
                    ctx.ReturnFrame(frame);
                    // clearArray:true to drop any GcRef references in the slots before
                    // putting the array back in the pool, keeping the GC graph tidy.
                    ArrayPool<Value>.Shared.Return(rented, clearArray: true);
                }

                if (!tailContinue) return;
            }
        }
    }
}
