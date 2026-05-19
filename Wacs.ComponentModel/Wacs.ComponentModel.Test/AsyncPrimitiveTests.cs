// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Shape-level coverage for the Component Model async ABI
    /// primitives (Phase 3 Slice A): <see cref="AsyncHandleTable{T}"/>,
    /// <see cref="ComponentTask"/>, <see cref="ComponentSubtask"/>,
    /// <see cref="ComponentWaitableSet"/>. Execution semantics
    /// (dispatcher) come in Slice C.
    /// </summary>
    public class AsyncPrimitiveTests
    {
        // Synthetic ContinuationStore for the Task primitive's
        // wasm-side reference. Standalone allocation; no execution.
        private static ContInstance MakeCont()
        {
            var store = new ContinuationStore();
            return store.Allocate((TypeIdx)0, (Delegate)(Func<int>)(() => 0));
        }

        // ---- AsyncHandleTable (typed facade over shared store) ----------

        [Fact]
        public void Table_allocates_handles_starting_at_one()
        {
            // Handle 0 is reserved as the null sentinel by canon
            // spec — the dispatcher's shared store must never
            // return 0. Exercised via the ErrorContexts facade
            // (any kind would do; the counter is shared).
            var d = new AsyncDispatcher();
            var h1 = d.ErrorContexts.Allocate("a");
            var h2 = d.ErrorContexts.Allocate("b");
            Assert.Equal(1, h1);
            Assert.Equal(2, h2);
        }

        [Fact]
        public void Table_get_returns_allocated_value_and_null_for_unknown()
        {
            var d = new AsyncDispatcher();
            var h = d.ErrorContexts.Allocate("hello");
            Assert.Equal("hello", d.ErrorContexts.Get(h));
            Assert.Null(d.ErrorContexts.Get(999));
        }

        [Fact]
        public void Table_drop_returns_value_and_removes_entry()
        {
            var d = new AsyncDispatcher();
            var h = d.ErrorContexts.Allocate("world");
            Assert.True(d.ErrorContexts.Contains(h));
            Assert.Equal("world", d.ErrorContexts.Drop(h));
            Assert.False(d.ErrorContexts.Contains(h));
            Assert.Null(d.ErrorContexts.Drop(h)); // second drop = absent
        }

        [Fact]
        public void Table_freelist_recycles_dropped_handles()
        {
            // Recycling stabilizes long-running components. The
            // freelist is SHARED across kinds — a dropped Task
            // handle gets reused by the next allocation of any kind.
            var d = new AsyncDispatcher();
            var h1 = d.ErrorContexts.Allocate("x");
            var h2 = d.ErrorContexts.Allocate("y");
            d.ErrorContexts.Drop(h1);
            var h3 = d.ErrorContexts.Allocate("z");
            Assert.Equal(h1, h3);
            Assert.Equal(2, d.ErrorContexts.Count);
            Assert.NotEqual(h2, h3);
        }

        [Fact]
        public void Table_count_reflects_live_entries_for_kind()
        {
            var d = new AsyncDispatcher();
            Assert.Equal(0, d.ErrorContexts.Count);
            var a = d.ErrorContexts.Allocate("a");
            var b = d.ErrorContexts.Allocate("b");
            Assert.Equal(2, d.ErrorContexts.Count);
            d.ErrorContexts.Drop(a);
            Assert.Equal(1, d.ErrorContexts.Count);
        }

        [Fact]
        public void Cross_kind_get_returns_null_for_same_handle()
        {
            // Spec-aligned single-namespace lookup: a handle
            // allocated under one kind is never visible to a
            // different kind's facade. With the per-kind tables
            // the old code had at handle 1 from Tasks AND handle 1
            // from Futures simultaneously — Slice L worked around
            // that by union-checking; the shared-store refactor
            // makes it impossible.
            var d = new AsyncDispatcher();
            var ecHandle = d.ErrorContexts.Allocate("error");
            Assert.NotNull(d.ErrorContexts.Get(ecHandle));
            // Same integer, different kind facade — must be null.
            Assert.Null(d.Futures.Get(ecHandle));
            Assert.Null(d.Streams.Get(ecHandle));
            Assert.False(d.Futures.Contains(ecHandle));
        }

        [Fact]
        public void Cross_kind_drop_does_not_release_wrong_kind()
        {
            // Dropping via the wrong facade returns null and
            // leaves the handle live in its real facade. Prevents
            // a hostile / mis-merged path from releasing a slot
            // it doesn't own.
            var d = new AsyncDispatcher();
            var h = d.ErrorContexts.Allocate("hello");
            Assert.Null(d.Futures.Drop(h));
            Assert.True(d.ErrorContexts.Contains(h));
            Assert.Equal("hello", d.ErrorContexts.Get(h));
        }

        [Fact]
        public void GetHandleKind_returns_kind_for_live_handle_and_null_for_absent()
        {
            var d = new AsyncDispatcher();
            var h = d.ErrorContexts.Allocate("hello");
            Assert.Equal(WaitableKind.ErrorContext, d.GetHandleKind(h));
            Assert.Null(d.GetHandleKind(999));
        }

        [Fact]
        public void Shared_counter_does_not_overlap_across_kinds()
        {
            // The spec-aligned property: a fresh allocation in
            // ANY kind takes the next integer from the SHARED
            // counter, so two different kinds never share a handle.
            var d = new AsyncDispatcher();
            var ec = d.ErrorContexts.Allocate("a");
            var ws = d.WaitableSets.Allocate(
                h => new ComponentWaitableSet(h));
            Assert.NotEqual(ec, ws);
            Assert.Equal(ec + 1, ws);
        }

        // ---- ComponentTask -------------------------------------------------

        [Fact]
        public void Task_starts_in_Starting_state_with_pending_completion()
        {
            var task = new ComponentTask(handle: 1, MakeCont());
            Assert.Equal(1, task.Handle);
            Assert.Equal(ComponentTaskState.Starting, task.State);
            Assert.False(task.Completion.Task.IsCompleted);
        }

        [Fact]
        public void Task_holds_continuation_reference()
        {
            var cont = MakeCont();
            var task = new ComponentTask(7, cont);
            Assert.Same(cont, task.Continuation);
        }

        [Fact]
        public void Task_completion_runs_continuations_asynchronously()
        {
            // RunContinuationsAsynchronously prevents reentry into
            // wasm from a SetResult call inside the dispatch loop.
            // The runtime option is on the TCS itself; assert by
            // observing the TCS's options through the Task.
            var task = new ComponentTask(1, MakeCont());
            Assert.True(
                (task.Completion.Task.CreationOptions
                 & System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously) != 0);
        }

        // ---- ComponentSubtask ----------------------------------------------

        [Fact]
        public void Subtask_links_child_and_parent_tasks()
        {
            var parent = new ComponentTask(1, MakeCont());
            var child = new ComponentTask(2, MakeCont());
            var sub = new ComponentSubtask(handle: 3, child: child, parent: parent);
            Assert.Equal(3, sub.Handle);
            Assert.Same(child, sub.Child);
            Assert.Same(parent, sub.Parent);
        }

        // ---- ComponentWaitableSet ------------------------------------------

        [Fact]
        public void WaitableSet_join_and_remove_are_idempotent()
        {
            var ws = new ComponentWaitableSet(handle: 1);
            ws.Join(10);
            ws.Join(10); // idempotent
            Assert.Equal(1, ws.Count);
            Assert.True(ws.Contains(10));
            Assert.True(ws.Remove(10));
            Assert.False(ws.Remove(10)); // already removed
            Assert.Equal(0, ws.Count);
        }

        [Fact]
        public void WaitableSet_tracks_multiple_members()
        {
            var ws = new ComponentWaitableSet(1);
            ws.Join(5);
            ws.Join(7);
            ws.Join(11);
            Assert.Equal(3, ws.Count);
            Assert.Contains(5, ws.Members);
            Assert.Contains(7, ws.Members);
            Assert.Contains(11, ws.Members);
        }
    }
}
