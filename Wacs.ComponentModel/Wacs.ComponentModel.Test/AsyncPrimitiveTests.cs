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

        // ---- AsyncHandleTable ---------------------------------------------

        [Fact]
        public void Table_allocates_handles_starting_at_one()
        {
            // Handle 0 is reserved as the null sentinel by canon
            // spec — the table must never return 0.
            var t = new AsyncHandleTable<object>();
            var h1 = t.Allocate(new object());
            var h2 = t.Allocate(new object());
            Assert.Equal(1, h1);
            Assert.Equal(2, h2);
        }

        [Fact]
        public void Table_get_returns_allocated_value_and_null_for_unknown()
        {
            var t = new AsyncHandleTable<string>();
            var h = t.Allocate("hello");
            Assert.Equal("hello", t.Get(h));
            Assert.Null(t.Get(999));
        }

        [Fact]
        public void Table_drop_returns_value_and_removes_entry()
        {
            var t = new AsyncHandleTable<string>();
            var h = t.Allocate("world");
            Assert.True(t.Contains(h));
            Assert.Equal("world", t.Drop(h));
            Assert.False(t.Contains(h));
            Assert.Null(t.Drop(h)); // second drop = absent
        }

        [Fact]
        public void Table_freelist_recycles_dropped_handles()
        {
            // Recycling stabilizes long-running components — the
            // handle counter doesn't drift unbounded under steady
            // allocate/drop churn.
            var t = new AsyncHandleTable<object>();
            var h1 = t.Allocate(new object());
            var h2 = t.Allocate(new object());
            t.Drop(h1);
            var h3 = t.Allocate(new object()); // pops h1 from freelist
            Assert.Equal(h1, h3);
            Assert.Equal(2, t.Count);
            Assert.NotEqual(h2, h3);
        }

        [Fact]
        public void Table_count_reflects_live_entries()
        {
            var t = new AsyncHandleTable<object>();
            Assert.Equal(0, t.Count);
            var a = t.Allocate(new object());
            var b = t.Allocate(new object());
            Assert.Equal(2, t.Count);
            t.Drop(a);
            Assert.Equal(1, t.Count);
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
