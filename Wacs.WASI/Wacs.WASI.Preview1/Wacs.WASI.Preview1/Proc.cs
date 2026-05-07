// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.HostBindings;
using Wacs.WASI.Preview1.Types;
using exitcode = System.Int32;

namespace Wacs.WASI.Preview1
{
    public class Proc : IBindable
    {
        private readonly State _state;

        public Proc(State state) => _state = state;

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Action<exitcode>>((module, "proc_exit"), ProcExit);
            runtime.BindHostFunction<Func<int,ErrNo>>((module, "proc_raise"), ProcRaise);
            runtime.BindHostFunction<Func<ExecContext,ErrNo>>((module, "sched_yield"), SchedYield);
        }

        private void ProcExit(exitcode exitCode) => ProcExitCore(_state, exitCode);

        public ErrNo ProcRaise(int signal) => ProcRaiseCore(_state, signal);

        public ErrNo SchedYield(ExecContext ctx) => SchedYieldCore();

        // ============================================================
        // AOT-friendly static entry points
        // ============================================================

        [WacsImport("wasi_snapshot_preview1", "proc_exit")]
        public static void ProcExitCore(State state, exitcode exitCode)
        {
            state.ExitCode = exitCode;
            throw new SystemExitException(exitCode);
        }

        /// <summary>
        /// Sends a signal to the process.
        /// </summary>
        [WacsImport("wasi_snapshot_preview1", "proc_raise")]
        public static ErrNo ProcRaiseCore(State state, int signal)
        {
            // Handle common signals as per WASI specification.
            switch ((Signal)signal)
            {
                case Signal.SIGINT:
                case Signal.SIGTERM:
                case Signal.SIGKILL:
                    state.LastSignal = signal;
                    // For SIGINT/SIGTERM/SIGKILL we surface a SignalException
                    // up through the wasm runtime's trap-propagation machinery.
                    throw new SignalException(signal);
                default:
                    return ErrNo.Inval;
            }
        }

        /// <summary>
        /// Temporarily yield execution of the calling thread. Note: This is
        /// similar to sched_yield in POSIX. No-op today; future work could
        /// notify the host scheduler.
        /// </summary>
        [WacsImport("wasi_snapshot_preview1", "sched_yield")]
        public static ErrNo SchedYieldCore() => ErrNo.Success;
    }
}