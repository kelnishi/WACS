// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading.Tasks;
using Wacs.Core.Runtime.Concurrency;

namespace Wacs.ComponentModel.Async
{
    /// <summary>
    /// Helper for wrapping a wasm-body invocation with the task
    /// lifecycle the Component Model async ABI expects. The
    /// wit-component <c>canon lift f opts</c> with the
    /// <c>async</c> option produces a wrapper at the component
    /// boundary that:
    ///
    /// <list type="number">
    ///   <item>Allocates a <see cref="ComponentTask"/> bound to a
    ///     <see cref="ContInstance"/> for the body's frame.</item>
    ///   <item>Marks the task as the ambient
    ///     <see cref="AsyncDispatcher.CurrentTask"/> for the
    ///     body's execution.</item>
    ///   <item>Invokes the body once (synchronous in Phase 3's
    ///     scope — suspending bodies that yield via Stack
    ///     Switching arrive when <see cref="WaitableSetWait_pending"/>
    ///     suspend integration lands).</item>
    ///   <item>Settles the task's
    ///     <see cref="ComponentTask.Completion"/> on body exit
    ///     and pops the ambient stack.</item>
    /// </list>
    ///
    /// <para>Returns the
    /// <see cref="System.Threading.Tasks.Task{TResult}"/> the
    /// host awaits. Per the
    /// <c>feedback_jspi_on_cm_async</c> constraint, CLR
    /// <c>Task&lt;T&gt;</c> is the canonical host async type —
    /// embedders consume the returned task with idiomatic
    /// <c>await</c>.</para>
    ///
    /// <para><b>Scope:</b> the helpers here cover the
    /// <i>synchronous</i> body case — the body runs to
    /// completion on the calling stack. A body that suspends
    /// via stack switching needs the Phase 1 substrate to park
    /// + resume; that integration lands together with
    /// <see cref="AsyncDispatcher.WaitableSetWait"/> in a later
    /// slice.</para>
    /// </summary>
    public static class AsyncLiftAdapter
    {
        /// <summary>
        /// Wrap a void-returning synchronous body invocation
        /// with task lifecycle management. The body is invoked
        /// once; the resulting <see cref="ComponentTask.Completion"/>
        /// is returned as a <see cref="Task{T}"/>.
        ///
        /// <para>If the body explicitly called
        /// <see cref="AsyncDispatcher.TaskReturn"/>, that result
        /// surfaces. Otherwise the task is implicitly returned
        /// with <c>null</c>. If the body threw an exception that
        /// wasn't a <c>TaskCancel</c>-driven transition, the
        /// task is marked <see cref="ComponentTaskState.Failed"/>
        /// and the exception faults the completion.</para>
        /// </summary>
        public static Task<object?> InvokeAsync(
            AsyncDispatcher dispatcher,
            ContInstance continuation,
            Action body)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (continuation == null)
                throw new ArgumentNullException(nameof(continuation));
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            var task = dispatcher.RegisterTask(continuation);
            dispatcher.PushCurrentTask(task);
            try
            {
                body();
                // Body returned without calling task.return
                // explicitly — treat as implicit return with
                // null. The dispatcher's task.return / task.cancel
                // already transitioned the state, so only step
                // in if the task is still Started.
                if (task.State == ComponentTaskState.Started)
                {
                    task.State = ComponentTaskState.Returned;
                    task.Completion.TrySetResult(null);
                }
            }
            catch (Exception ex)
            {
                // Body exceptions surface via the returned Task
                // (faulted), not via a synchronous throw — embedders
                // observe wasm failures through the same await they
                // use for normal results. Only mark Failed if the
                // dispatcher didn't already settle the task (e.g.
                // task.cancel transitions to Cancelled before the
                // body unwinds).
                if (task.State == ComponentTaskState.Started)
                {
                    task.State = ComponentTaskState.Failed;
                    task.Completion.TrySetException(ex);
                }
            }
            finally
            {
                dispatcher.PopCurrentTask();
            }

            return task.Completion.Task;
        }

        /// <summary>
        /// Wrap a value-returning synchronous body. The return
        /// value is treated as an implicit
        /// <see cref="AsyncDispatcher.TaskReturn"/> unless the
        /// body called <c>task.return</c> explicitly.
        /// </summary>
        public static Task<object?> InvokeAsync<T>(
            AsyncDispatcher dispatcher,
            ContInstance continuation,
            Func<T> body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            return InvokeAsync(dispatcher, continuation, () =>
            {
                var result = body();
                // Auto-return if the body didn't call task.return.
                var task = dispatcher.CurrentTask;
                if (task != null && task.State == ComponentTaskState.Started)
                {
                    task.State = ComponentTaskState.Returned;
                    task.Completion.TrySetResult(result);
                }
            });
        }
    }
}
