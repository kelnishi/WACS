// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Threading.Tasks;
using Wacs.Core.Runtime.Concurrency;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Component Model async <c>task</c> primitive — one in-flight
    /// async call within the component. Carries:
    ///
    /// <list type="bullet">
    ///   <item>A wasm-side <see cref="ContInstance"/> Continuation
    ///     — the same artifact the Stack Switching substrate
    ///     allocates. <c>task = continuation</c> per the WASIp3
    ///     architecture constraint; the dispatcher reaches the
    ///     wasm frame through this reference.</item>
    ///   <item>A host-side <see cref="TaskCompletionSource{T}"/>
    ///     so embedders <c>await</c> the task as a CLR
    ///     <see cref="System.Threading.Tasks.Task{T}"/> — the
    ///     bidirectional bridge that lets host await and wasm
    ///     suspend share one substrate.</item>
    /// </list>
    ///
    /// <para>Allocation goes through
    /// <see cref="AsyncHandleTable{T}"/> — the handle this
    /// instance carries is the table slot identifying it to
    /// canon-ABI calls (<c>task.return</c>, <c>task.cancel</c>,
    /// etc.).</para>
    ///
    /// <para>Instances are typically constructed by the
    /// AsyncDispatcher (Slice C); the constructor is public to
    /// support direct host-side scenarios (JSPI-style promise
    /// adoption) where the dispatcher wraps a pre-existing CLR
    /// task.</para>
    /// </summary>
    public sealed class ComponentTask
    {
        /// <summary>Handle this task was allocated under in its owning table.</summary>
        public int Handle { get; }

        /// <summary>The wasm-side coroutine this task hosts.
        /// Suspends inside the body propagate through here.</summary>
        public ContInstance Continuation { get; }

        /// <summary>
        /// Host-side completion source. <c>task.return</c> resolves
        /// this with the lifted return value; <c>task.cancel</c>
        /// transitions it to canceled; an escaped exception in the
        /// body faults it. Embedders <c>await</c> the
        /// <see cref="TaskCompletionSource{T}.Task"/> to observe
        /// the wasm-side outcome.
        ///
        /// <para>Constructed with
        /// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>
        /// so a <c>SetResult</c> from inside the dispatch loop
        /// doesn't reenter wasm via the continuation chain.</para>
        /// </summary>
        public TaskCompletionSource<object?> Completion { get; }

        /// <summary>Current lifecycle state. Set by the dispatcher
        /// at each transition; spec-level callers read this to
        /// gate which canon ops are legal (e.g. <c>task.return</c>
        /// requires <see cref="ComponentTaskState.Started"/>).</summary>
        public ComponentTaskState State { get; internal set; }

        public ComponentTask(int handle, ContInstance continuation)
        {
            Handle = handle;
            Continuation = continuation;
            Completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            State = ComponentTaskState.Starting;
        }
    }
}
