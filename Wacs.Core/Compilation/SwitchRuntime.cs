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

namespace Wacs.Core.Compilation
{
    /// <summary>
    /// Optional byte-stream interpreter. Drives <see cref="GeneratedDispatcher.TryDispatch"/>
    /// directly off a <see cref="ReadOnlySpan{Byte}"/> — no <c>InstructionBase</c> instances
    /// are allocated per opcode.
    ///
    /// Bytecode format: each instruction starts with its WASM primary byte. Primary bytes in
    /// <c>0xFB..0xFF</c> indicate an extended opcode and are followed by the secondary byte
    /// (GC / misc / SIMD / threads / WACS-synthetic respectively). Each handler then reads
    /// its own immediates inline from the stream.
    ///
    /// The original runtime in <c>WasmRuntimeExecution</c> is unaffected — this is a parallel,
    /// opt-in execution path. AOT-safe: no reflection or runtime emit on the hot path.
    /// </summary>
    public static class SwitchRuntime
    {
        /// <summary>
        /// Runs <paramref name="code"/> against <paramref name="ctx"/> until the stream is
        /// exhausted. Throws <see cref="NotSupportedException"/> on an opcode without
        /// <c>[OpSource]</c>/<c>[OpHandler]</c> coverage.
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
        /// Top-level invocation for the switch runtime. Push <paramref name="args"/> onto
        /// the OpStack of <paramref name="ctx"/>, run <paramref name="func"/> through the
        /// Call-handler's standard frame setup + compile-on-demand path, then collect the
        /// return values. Use this to exercise compiled functions from tests and from
        /// higher-level invokers.
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
