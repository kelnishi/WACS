// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Compilation;
using Wacs.Core.Instructions;
using Wacs.Core.Runtime.Exceptions;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;

namespace Wacs.Core.Runtime
{
    public partial class WasmRuntime
    {
        /// <summary>
        /// When true, <see cref="CreateInvoker"/> routes top-level invocations of
        /// WASM-defined functions through <c>Wacs.Core.Compilation.SwitchRuntime</c>
        /// (the source-generated monolithic-switch interpreter) instead of the
        /// polymorphic <see cref="ProcessThreadWithOptions"/> dispatch loop. Host
        /// functions always fall through to the polymorphic path — they can't be
        /// compiled to the annotated bytecode stream.
        ///
        /// Off by default so the existing runtime is unchanged. Flip on per-runtime
        /// for spec-test parity runs and benchmarks.
        /// </summary>
        public bool UseSwitchRuntime = false;

        /// <summary>
        /// Switch-runtime invocation path. Mirrors the polymorphic flow's surface
        /// (push scalars, invoke, pop scalars) but skips gas metering, async handling,
        /// and CollectStats — none of those are wired on the switch runtime yet.
        ///
        /// <para>Runs synchronously on the caller's thread. The previous implementation
        /// used a dedicated 32 MiB-stack worker thread because the per-op dispatch
        /// frame (from __Op wrappers + register-bank locals in a monolithic switch)
        /// was large enough to exhaust the default stack at modest recursion depths.
        /// The current generator emits no wrappers and no bank locals — each dispatch
        /// call occupies a normal-sized method frame, so the default process stack is
        /// enough for the spec suite.</para>
        /// </summary>
        internal Value[] InvokeViaSwitch(ExecContext ctx, FunctionInstance func, FunctionType funcType, object[] args)
        {
            // Caller passes the per-thread ExecContext resolved once at the invoker
            // entry point — see GenericDelegate in WasmRuntimeExecution.cs. Avoids
            // repeated dictionary lookups on the hot switch-dispatch path.
            ctx.CheckInterrupt();
            ctx.OpStack.PushScalars(funcType.ParameterTypes, args);
            ctx.SwitchCallDepth = 0;

            try
            {
                ControlHandlers.InvokeWasm(ctx, func);
            }
            catch (WasmException we)
            {
                FlushCallStackForSwitch(ctx);
                throw new UnhandledWasmException($"Unhandled exception {we.Exn}");
            }
            catch (IndexOutOfRangeException ioe)
            {
                // OpStack overflow surfaces as IOOR because the switch path pushes into
                // a fixed-size Value[] rather than going through the polymorphic
                // throw-on-overflow path. Re-surface as a WasmRuntimeException so
                // assert_exhaustion tests see the type they expect.
                FlushCallStackForSwitch(ctx);
                throw new Wacs.Core.Runtime.Exceptions.WasmRuntimeException(
                    "Operand stack exhausted: " + ioe.Message);
            }
            catch
            {
                FlushCallStackForSwitch(ctx);
                throw;
            }

            Value[] results = new Value[funcType.ResultType.Arity];
            var span = results.AsSpan();
            ctx.OpStack.PopScalars(funcType.ResultType, span);

            ctx.GetModule(func.Address)?.DerefTypes(span);

            // Drain any leftover stack values (shouldn't happen for well-formed modules,
            // but the polymorphic path does the same defensive flush for top-level calls).
            FlushCallStackForSwitch(ctx);

            return results;
        }

        private void FlushCallStackForSwitch(ExecContext ctx)
        {
            // A push-overflow (hit during the iterative recursion path — each active
            // frame leaves its leftover stack values on the shared OpStack, so deep
            // recursion can push Count past _registers.Length before the depth guard
            // or any other check fires) leaves Count in an out-of-range state. The
            // naïve PopAny loop would re-throw IOOR on the first read. Zero the stack
            // unconditionally here — we're on an abort path, the data is already gone.
            ctx.OpStack.Count = 0;
        }
    }
}
