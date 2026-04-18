// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Runtime.ExceptionServices;
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
        /// </summary>
        internal Value[] InvokeViaSwitch(FunctionInstance func, FunctionType funcType, object[] args)
        {
            Context.OpStack.PushScalars(funcType.ParameterTypes, args);

            try
            {
                ControlHandlers.InvokeWasm(Context, func);
            }
            catch (TrapException exc)
            {
                // Match the polymorphic path's behavior: clear whatever was left on
                // the stack so the runtime is usable for the next invocation, then
                // re-raise the original exception.
                FlushCallStackForSwitch();
                ExceptionDispatchInfo.Throw(exc);
            }
            catch (WasmException we)
            {
                // A WASM exception escaped every frame's try_table — surface it the
                // way the polymorphic path does, as UnhandledWasmException, so the
                // spec-test harness (which catches that type) sees a consistent view.
                FlushCallStackForSwitch();
                throw new UnhandledWasmException($"Unhandled exception {we.Exn}");
            }
            catch (Exception exc) when (exc is not AggregateException)
            {
                FlushCallStackForSwitch();
                ExceptionDispatchInfo.Throw(exc);
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
