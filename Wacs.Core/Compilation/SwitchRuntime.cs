// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Optional byte-stream interpreter. Drives <see cref="GeneratedDispatcher.TryDispatch"/>
    /// directly off a <see cref="ReadOnlySpan{Byte}"/> (for low-level tests) or a full
    /// <see cref="CompiledFunction"/> (for production — enables exception handling via the
    /// HandlerTable sidecar).
    ///
    /// Bytecode format: each instruction starts with its WASM primary byte. Primary bytes
    /// in <c>0xFB..0xFF</c> indicate an extended opcode and are followed by the secondary
    /// byte. Each handler then reads its own immediates inline from the stream.
    ///
    /// The original runtime in <c>WasmRuntimeExecution</c> is unaffected — this is a
    /// parallel, opt-in execution path. AOT-safe: no reflection or runtime emit on the
    /// hot path.
    /// </summary>
    public static class SwitchRuntime
    {
        /// <summary>
        /// Low-level byte-stream driver. No handler-table awareness; any <c>WasmException</c>
        /// from an opcode propagates out unhandled. Useful for testing raw bytecode
        /// snippets; production invocation uses the <see cref="CompiledFunction"/> overload.
        /// </summary>
        public static void Run(ExecContext ctx, ReadOnlySpan<byte> code)
        {
            int pc = 0;
            while (pc < code.Length)
            {
                byte primary = code[pc++];
                ushort op;
                if (primary >= 0xFB && primary <= 0xFF)
                {
                    if (pc >= code.Length)
                        throw new InvalidProgramException(
                            $"Truncated bytecode: prefix 0x{primary:X2} at end of stream.");
                    byte secondary = code[pc++];
                    op = (ushort)((primary << 8) | secondary);
                }
                else
                {
                    op = (ushort)(primary << 8);
                }

                if (!GeneratedDispatcher.TryDispatch(ctx, code, ref pc, op))
                    throw new NotSupportedException(
                        $"Opcode 0x{op:X4} has no [OpSource]/[OpHandler] coverage in GeneratedDispatcher.");
            }
        }

        /// <summary>
        /// Full-function driver. Wraps dispatch in a <c>WasmException</c> catcher that
        /// consults <see cref="CompiledFunction.HandlerTable"/> — on match, resumes at
        /// the catch handler; on miss, rethrows so the caller's frame can do its own
        /// lookup one managed-frame up.
        /// </summary>
        public static void Run(ExecContext ctx, CompiledFunction func)
        {
            var code = func.Bytecode;
            var handlers = func.HandlerTable;
            int pc = 0;
            int pcBeforeDispatch = 0;
            while (pc < code.Length)
            {
                // Snapshot pc before fetch/dispatch. Handler-range checks on throw use
                // this — the in-range semantics is "throw's opcode was inside the
                // try_table's body," not "pc after the throw advanced past its
                // immediates is inside." A throw-as-last-instruction before End would
                // miss otherwise.
                pcBeforeDispatch = pc;
                try
                {
                    byte primary = code[pc++];
                    ushort op;
                    if (primary >= 0xFB && primary <= 0xFF)
                    {
                        if (pc >= code.Length)
                            throw new InvalidProgramException(
                                $"Truncated bytecode: prefix 0x{primary:X2} at end of stream.");
                        byte secondary = code[pc++];
                        op = (ushort)((primary << 8) | secondary);
                    }
                    else
                    {
                        op = (ushort)(primary << 8);
                    }

                    if (!GeneratedDispatcher.TryDispatch(ctx, code, ref pc, op))
                        throw new NotSupportedException(
                            $"Opcode 0x{op:X4} has no [OpSource]/[OpHandler] coverage.");
                }
                catch (WasmException we)
                {
                    // Catch unconditionally (not via a filter) because C# filters evaluate
                    // during first-pass unwinding, before the callee's InvokeWasm finally
                    // restores ctx.Frame. That would leave the wrong module's TagAddrs
                    // visible during tag comparison. Catching here guarantees we run only
                    // after every intermediate finally has executed.
                    if (handlers.Length == 0 ||
                        !TryResumeWithHandler(ctx, handlers, ref pc, we.Exn, pcBeforeDispatch))
                        throw;
                }
            }
        }

        /// <summary>
        /// Walk the handler table searching for a matching catch clause. The spec's
        /// semantics are: try the innermost enclosing <c>try_table</c> first, checking
        /// its catches in declaration order; if none matches, fall through to the next
        /// enclosing <c>try_table</c>.
        ///
        /// <para>The flat <c>HandlerEntry[]</c> is in emission order (outer try_tables
        /// first, catches within a try_table in declaration order). Each try_table has
        /// a unique <see cref="HandlerEntry.StartPc"/>. The outer loop picks the
        /// largest <c>StartPc</c> still containing <paramref name="pcForRangeCheck"/>
        /// (= innermost not-yet-tried try_table); the inner loop then checks its
        /// catches in natural order. Move one try_table outward on a miss.</para>
        /// </summary>
        private static bool TryResumeWithHandler(ExecContext ctx, HandlerEntry[] handlers,
                                                  ref int pc, ExnInstance exn, int pcForRangeCheck)
        {
            uint upc = (uint)pcForRangeCheck;
            uint lastStart = uint.MaxValue;

            while (true)
            {
                // Find innermost (largest StartPc) try_table covering pc, with StartPc
                // strictly less than the one we just finished checking.
                uint candidateStart = 0;
                bool hasCandidate = false;
                for (int i = 0; i < handlers.Length; i++)
                {
                    var h = handlers[i];
                    if (upc < h.StartPc || upc >= h.EndPc) continue;
                    if (h.StartPc >= lastStart) continue;
                    if (!hasCandidate || h.StartPc > candidateStart)
                    {
                        candidateStart = h.StartPc;
                        hasCandidate = true;
                    }
                }
                if (!hasCandidate) return false;

                // Scan this try_table's catches in declaration order.
                for (int i = 0; i < handlers.Length; i++)
                {
                    var h = handlers[i];
                    if (h.StartPc != candidateStart) continue;

                    var kind = (CatchFlags)h.Kind;
                    bool matches = kind switch
                    {
                        CatchFlags.CatchAll or CatchFlags.CatchAllRef => true,
                        CatchFlags.None or CatchFlags.CatchRef =>
                            ctx.Frame.Module.TagAddrs[(TagIdx)h.TagIdx].Equals(exn.Tag),
                        _ => false,
                    };
                    if (!matches) continue;

                    switch (kind)
                    {
                        case CatchFlags.None:
                            ctx.OpStack.PushResults(exn.Fields);
                            break;
                        case CatchFlags.CatchRef:
                            ctx.OpStack.PushResults(exn.Fields);
                            ctx.OpStack.PushValue(new Value(ValType.Exn, exn));
                            break;
                        case CatchFlags.CatchAll:
                            break;
                        case CatchFlags.CatchAllRef:
                            ctx.OpStack.PushValue(new Value(ValType.Exn, exn));
                            break;
                    }
                    ctx.OpStack.ShiftResults((int)h.Arity, (int)h.ResultsHeight);
                    pc = (int)h.HandlerPc;
                    return true;
                }

                // No catch in this try_table matched — try the next-outer try_table.
                lastStart = candidateStart;
            }
        }

        /// <summary>
        /// Top-level invocation for the switch runtime. Push <paramref name="args"/> onto
        /// the OpStack of <paramref name="ctx"/>, run <paramref name="func"/> through the
        /// Call-handler's standard frame setup + compile-on-demand path, then collect the
        /// return values.
        /// </summary>
        public static Value[] Invoke(ExecContext ctx, FunctionInstance func, params Value[] args)
        {
            foreach (var v in args) ctx.OpStack.PushValue(v);
            // InvokeWasm sets up the frame, pops args into locals, runs the compiled body,
            // and restores the caller's frame on return. Results are on OpStack after it
            // returns.
            ControlHandlers.InvokeWasm(ctx, func);

            int arity = func.Type.ResultType.Arity;
            var results = new Value[arity];
            for (int i = arity - 1; i >= 0; i--) results[i] = ctx.OpStack.PopAny();
            return results;
        }
    }
}
