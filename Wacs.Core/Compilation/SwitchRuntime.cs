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
    /// Public entry point for the monolithic-switch interpreter. The actual dispatch
    /// loop lives inside <see cref="GeneratedDispatcher.Run"/> — this class is a thin
    /// wrapper that adds WASM-exception handling (via the <see cref="CompiledFunction"/>
    /// overload's per-function handler table) and the top-level <c>Invoke</c> surface.
    ///
    /// <para>The original polymorphic runtime in <c>WasmRuntimeExecution</c> is unaffected
    /// — this is a parallel, opt-in execution path. AOT-safe: no reflection or runtime
    /// emit on the hot path.</para>
    /// </summary>
    public static class SwitchRuntime
    {
        /// <summary>
        /// Global toggle — when true, <see cref="Run(ExecContext, CompiledFunction)"/>
        /// routes dispatch through <see cref="MinimalDispatcher"/> instead of the
        /// source-generated dispatcher. Used to prototype/measure hand-written
        /// one-method dispatch against the generator output.
        /// </summary>
        public static bool UseMinimal = false;

        /// <summary>
        /// Low-level byte-stream driver. No handler-table awareness; any
        /// <c>WasmException</c> from an opcode propagates out unhandled. Useful for
        /// testing raw bytecode snippets; production invocation uses the
        /// <see cref="CompiledFunction"/> overload.
        /// </summary>
        public static void Run(ExecContext ctx, byte[] code)
        {
            ctx.SwitchPc = 0;
            ctx.SwitchPcBefore = 0;
            GeneratedDispatcher.Run(ctx, new CompiledFunction(code, 0, null!, System.Array.Empty<HandlerEntry>()));
        }


        /// <summary>
        /// Full-function driver. Phase M: exception handling happens inside
        /// <c>GeneratedDispatcher.Run</c>'s outer try/catch, which walks the per-frame
        /// handler table AND <c>ctx._switchCallStack</c>. This wrapper is now trivial
        /// — just seeds <c>ctx.SwitchPc</c> and dispatches.
        /// </summary>
        public static void Run(ExecContext ctx, CompiledFunction func)
        {
            if (UseMinimal)
            {
                MinimalDispatcher.Run(ctx, func.Bytecode);
                return;
            }

            ctx.SwitchPc = 0;
            ctx.SwitchPcBefore = 0;
            GeneratedDispatcher.Run(ctx, func);
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
        /// <summary>
        /// Public overload of <see cref="TryResumeWithHandler"/> that also returns the
        /// matching handler's pc via an out parameter (rather than writing to
        /// <c>ctx.SwitchPc</c>). Used by the iterative <c>GeneratedDispatcher.Run</c>
        /// unwind loop which manages pc as a method-local instead of a context field.
        /// </summary>
        internal static bool TryResumeWithHandlerTable(ExecContext ctx, HandlerEntry[] handlers,
                                                        ExnInstance exn, int pcForRangeCheck,
                                                        out int handlerPc)
        {
            if (TryResumeWithHandler(ctx, handlers, exn, pcForRangeCheck))
            {
                handlerPc = ctx.SwitchPc;
                return true;
            }
            handlerPc = 0;
            return false;
        }

        private static bool TryResumeWithHandler(ExecContext ctx, HandlerEntry[] handlers,
                                                  ExnInstance exn, int pcForRangeCheck)
        {
            uint upc = (uint)pcForRangeCheck;
            uint lastStart = uint.MaxValue;

            while (true)
            {
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
                    ctx.SwitchPc = (int)h.HandlerPc;
                    return true;
                }

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
            ControlHandlers.InvokeWasm(ctx, func);

            int arity = func.Type.ResultType.Arity;
            var results = new Value[arity];
            for (int i = arity - 1; i >= 0; i--) results[i] = ctx.OpStack.PopAny();
            return results;
        }
    }
}
