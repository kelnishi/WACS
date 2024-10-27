using System;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Exceptions;
using Wacs.WASIp1.Types;
using exitcode = System.UInt32;
using signal = System.Int32;

namespace Wacs.WASIp1
{
    public class Proc : IBindable
    {
        private State _state;

        public Proc(State state) => _state = state;

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Action<exitcode>>((module, "proc_exit"), ProcExit);
            runtime.BindHostFunction<Func<signal,ErrNo>>((module, "proc_raise"), ProcRaise);
            runtime.BindHostFunction<Func<ExecContext,ErrNo>>((module, "sched_yield"), SchedYield);
        }

        private void ProcExit(exitcode exitCode)
        {
            _state.ExitCode = (Signal)exitCode;
            throw new SignalException((int)exitCode);
        }

        /// <summary>
        /// Sends a signal to the process.
        /// </summary>
        /// <param name="sig">
        /// The signal number to send to the process.
        /// </param>
        /// <returns>
        /// Returns 0 (<see cref="ErrNo.Success"/>) on success,
        /// or a non-zero WASI error code on failure.
        /// </returns>
        public ErrNo ProcRaise(signal sig)
        {
            Signal signal = (Signal)sig;
            // Handle common signals as per WASI specification.
            switch (signal)
            {
                case Signal.SIGINT: // SIGINT
                case Signal.SIGTERM: // SIGTERM
                case Signal.SIGKILL: // SIGKILL
                    // Update the state to reflect the received signal.
                    _state.LastSignal = signal;

                    // Depending on the signal, perform appropriate actions.
                    // For SIGINT and SIGTERM, we might initiate graceful shutdown.
                    // For demonstration, we'll throw an exception to simulate termination.
                    throw new SignalException((int)signal);

                default:
                    // Unsupported signal.
                    return ErrNo.Inval; // Invalid argument.
            }
        }

        /// <summary>
        /// Temporarily yield execution of the calling thread. Note: This is similar to sched_yield in POSIX.
        /// </summary>
        /// <returns></returns>
        public ErrNo SchedYield(ExecContext ctx)
        {
            //Does nothing for now, perhaps we should notify ctx for suspension

            return ErrNo.Success;
        }
    }
}