// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.Threading;
using Wacs.Core.Runtime.Types;

namespace Wacs.Core.Runtime.Concurrency
{
    /// <summary>
    /// Default <see cref="IWasmThreadHost"/> — spawns a dedicated
    /// <see cref="System.Threading.Thread"/> per call. Uses <see cref="Thread"/>
    /// rather than <see cref="System.Threading.Tasks.Task.Run(Action)"/> for two
    /// reasons:
    /// <list type="bullet">
    /// <item><description>
    /// wasm threads may block on <c>atomic.wait</c> for long durations; running them
    /// on the thread pool would exhaust it under even moderate wasm-thread counts.
    /// </description></item>
    /// <item><description>
    /// <c>Task.Run</c> can capture a <see cref="SynchronizationContext"/> (e.g. ASP.NET
    /// request context, Unity main-thread context). Wasm threads must not marshal
    /// continuations back to the main thread — Unity especially.
    /// </description></item>
    /// </list>
    ///
    /// <para>Each spawned <see cref="WasmThread"/> is associated with a fresh
    /// <see cref="ExecContext"/> via the runtime's ThreadLocal (Layer 1c) —
    /// the host creates the thread, the first <c>CreateInvoker</c> / <c>invoke</c>
    /// call on that thread lazily provisions its ExecContext, and all shared state
    /// (Store, linked ops, memory/table/globals) is shared by reference through
    /// <see cref="SharedRuntimeState"/>.</para>
    /// </summary>
    public sealed class ThreadBasedHost : IWasmThreadHost
    {
        private readonly WasmRuntime _runtime;
        private int _nextHostId;

        public ThreadBasedHost(WasmRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public WasmThread Spawn(FuncAddr entry, ReadOnlySpan<Value> args, CancellationToken ct = default)
        {
            // Copy args off the caller's span before yielding to the new thread —
            // the span may live on the stack and won't survive this method's return.
            var argArray = args.ToArray();

            var thread = new WasmThread(Interlocked.Increment(ref _nextHostId), ct);

            var t = new Thread(() =>
            {
                try
                {
                    // Bind the CT to the per-thread ExecContext (Layer 1c) before
                    // invoking. The invoke loop observes ctx.Ct at call boundaries
                    // and traps with InterruptedException on cancellation.
                    var ctx = _runtime.ExecContext;
                    ctx.Ct = thread.CancellationToken;
                    ctx.InterruptReason = thread.TrapReason;

                    var invoker = _runtime.CreateStackInvoker(entry);
                    _ = invoker(argArray);
                    thread.Complete(null);
                }
                catch (TrapException trap)
                {
                    thread.Complete(trap);
                }
                catch (Exception exc)
                {
                    thread.Fault(exc);
                }
            })
            {
                IsBackground = true,
                Name = $"WasmThread#{thread.HostId}",
            };
            t.Start();
            return thread;
        }
    }
}
