// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
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
        /// Stack size for the dedicated switch-runtime thread (<see cref="SwitchRuntimeStackSize"/>
        /// bytes). The default process stack (1 MiB on most platforms) is too small for
        /// modest WASM recursion depths — each WASM call creates multiple managed frames
        /// (SwitchRuntime.Run → generated TryDispatch → call handler → InvokeWasm → back
        /// into Run) and TryDispatch itself has a large stack frame from the monolithic
        /// switch. 32 MiB comfortably fits typical recursive spec tests (fac(25), etc.)
        /// while staying within a single OS-level virtual-memory range.
        /// </summary>
        public const int SwitchRuntimeStackSize = 32 * 1024 * 1024;

        /// <summary>
        /// Switch-runtime invocation path. Mirrors the polymorphic flow's surface
        /// (push scalars, invoke, pop scalars) but skips gas metering, async handling,
        /// and CollectStats — none of those are wired on the switch runtime yet.
        ///
        /// <para>Runs the actual dispatch on a dedicated <see cref="Thread"/> with
        /// <see cref="SwitchRuntimeStackSize"/> bytes of stack so moderate WASM
        /// recursion (fac, fib, etc.) doesn't overflow the process default. The
        /// thread is short-lived (one per top-level invoke); the cost is a handful
        /// of microseconds, dwarfed by any non-trivial WASM body.</para>
        /// </summary>
        internal Value[] InvokeViaSwitch(FunctionInstance func, FunctionType funcType, object[] args)
        {
            Context.OpStack.PushScalars(funcType.ParameterTypes, args);
            // Reset depth accounting per top-level invoke so a prior unwound invocation
            // can't leave SwitchCallDepth above zero and falsely trip the bound early.
            Context.SwitchCallDepth = 0;

            ExceptionDispatchInfo? capturedEdi = null;
            UnhandledWasmException? unhandledWasm = null;
            var worker = new Thread(() =>
            {
                try
                {
                    ControlHandlers.InvokeWasm(Context, func);
                }
                catch (WasmException we)
                {
                    unhandledWasm = new UnhandledWasmException($"Unhandled exception {we.Exn}");
                }
                catch (IndexOutOfRangeException ioe)
                {
                    // OpStack overflow in the middle of a deep recursion — surface it as
                    // a WasmRuntimeException so assert_exhaustion tests see the expected
                    // exception type. The polymorphic OpStack has explicit WasmRuntime-
                    // Exception throws for this; our switch path pushes directly into a
                    // fixed-size array that raises IOOR on overflow.
                    capturedEdi = ExceptionDispatchInfo.Capture(
                        new Wacs.Core.Runtime.Exceptions.WasmRuntimeException(
                            "Operand stack exhausted: " + ioe.Message));
                }
                catch (Exception exc) when (exc is not AggregateException)
                {
                    capturedEdi = ExceptionDispatchInfo.Capture(exc);
                }
            }, SwitchRuntimeStackSize);
            worker.IsBackground = true;
            worker.Start();
            worker.Join();

            if (unhandledWasm is not null)
            {
                FlushCallStackForSwitch();
                throw unhandledWasm;
            }
            if (capturedEdi is not null)
            {
                FlushCallStackForSwitch();
                capturedEdi.Throw();
            }

            Value[] results = new Value[funcType.ResultType.Arity];
            var span = results.AsSpan();
            Context.OpStack.PopScalars(funcType.ResultType, span);

            Context.GetModule(func.Address)?.DerefTypes(span);

            // Drain any leftover stack values (shouldn't happen for well-formed modules,
            // but the polymorphic path does the same defensive flush for top-level calls).
            FlushCallStackForSwitch();

            return results;
        }

        private void FlushCallStackForSwitch()
        {
            while (Context.OpStack.HasValue)
                Context.OpStack.PopAny();
        }
    }
}
