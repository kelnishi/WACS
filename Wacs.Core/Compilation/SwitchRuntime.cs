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
            while (pc < code.Length)
            {
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
                catch (WasmException we) when (handlers.Length > 0 &&
                                                TryResumeWithHandler(ctx, handlers, ref pc, we.Exn))
                {
                    // The filter ran the handler lookup; if it matched, we fall into this
                    // catch block having already mutated pc and the stack. Just continue.
                }
            }
        }

        /// <summary>
        /// Scans <paramref name="handlers"/> in reverse (inner try_table wins) for an
        /// entry whose pc-range covers <paramref name="pc"/> and whose kind/tag matches
        /// <paramref name="exn"/>. On match, pushes exception values per the catch kind,
        /// shifts the stack, updates <paramref name="pc"/>, and returns true.
        /// </summary>
        private static bool TryResumeWithHandler(ExecContext ctx, HandlerEntry[] handlers,
                                                  ref int pc, ExnInstance exn)
        {
            uint upc = (uint)pc;
            for (int i = handlers.Length - 1; i >= 0; i--)
            {
                var h = handlers[i];
                if (upc < h.StartPc || upc >= h.EndPc) continue;

                var kind = (CatchFlags)h.Kind;
                bool matches = kind switch
                {
                    CatchFlags.CatchAll or CatchFlags.CatchAllRef => true,
                    CatchFlags.None or CatchFlags.CatchRef =>
                        ctx.Frame.Module.TagAddrs[(TagIdx)h.TagIdx].Equals(exn.Tag),
                    _ => false,
                };
                if (!matches) continue;

                // Push the exception's carried values per the catch kind.
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
            return false;
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
