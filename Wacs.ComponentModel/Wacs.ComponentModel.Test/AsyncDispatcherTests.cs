// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Types;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Phase 3 Slice C contract tests for
    /// <see cref="AsyncDispatcher"/>. Covers the canon-async
    /// methods that don't require <see cref="IContinuationContext"/>
    /// suspend integration (Slice D). The stubbed methods throw
    /// <see cref="NotImplementedException"/> with a clear "Slice D"
    /// marker — pinned here so future-me notices when changing the
    /// contract.
    /// </summary>
    public class AsyncDispatcherTests
    {
        private static ContInstance MakeCont()
        {
            var store = new ContinuationStore();
            return store.Allocate((TypeIdx)0, (Delegate)(Func<int>)(() => 0));
        }

        // ---- Stream handle table ---------------------------------------

        [Fact]
        public void StreamNew_allocates_distinct_handles()
        {
            var d = new AsyncDispatcher();
            var h1 = d.StreamNew(typeIdx: 0);
            var h2 = d.StreamNew(typeIdx: 0);
            Assert.NotEqual(h1, h2);
            Assert.True(h1 > 0); // 0 is reserved (canon null)
            Assert.True(h2 > 0);
        }

        [Fact]
        public void Stream_round_trips_bytes()
        {
            var d = new AsyncDispatcher();
            var h = d.StreamNew(0);
            Assert.True(d.StreamTryWrite(h, 0x42));
            Assert.True(d.StreamTryRead(h, out var b));
            Assert.Equal(0x42, b);
            Assert.False(d.StreamTryRead(h, out _));
        }

        [Fact]
        public void Stream_write_to_unknown_handle_throws()
        {
            var d = new AsyncDispatcher();
            Assert.Throws<InvalidOperationException>(
                () => d.StreamTryWrite(999, 1));
        }

        [Fact]
        public void Stream_drop_writable_completes_writer_but_keeps_reader_addressable()
        {
            // Spec semantics: drop-writable closes the writer side
            // so the reader observes EOS once the buffer drains, but
            // the handle remains valid for the reader until
            // drop-readable also fires. Both halves dropped ↦ the
            // slot is released.
            var d = new AsyncDispatcher();
            var h = d.StreamNew(0);
            d.StreamTryWrite(h, 1);

            Assert.True(d.StreamDropWritable(h));
            // Reader can still drain the buffered byte.
            Assert.True(d.StreamTryRead(h, out var b));
            Assert.Equal(1, b);
            // Reader half also drops ↦ slot released.
            Assert.True(d.StreamDropReadable(h));
            // Subsequent drops on either half return false (slot gone).
            Assert.False(d.StreamDropWritable(h));
            Assert.False(d.StreamDropReadable(h));
        }

        // ---- Future handle table ---------------------------------------

        [Fact]
        public async Task Future_write_resolves_read_task()
        {
            var d = new AsyncDispatcher();
            var h = d.FutureNew(0);
            Assert.True(d.FutureWrite(h, "hello"));
            var v = await d.FutureReadAsync(h);
            Assert.Equal("hello", v);
        }

        [Fact]
        public void Future_double_write_returns_false()
        {
            var d = new AsyncDispatcher();
            var h = d.FutureNew(0);
            Assert.True(d.FutureWrite(h, 1));
            Assert.False(d.FutureWrite(h, 2));
        }

        [Fact]
        public async Task Future_drop_readable_cancels_pending_read()
        {
            var d = new AsyncDispatcher();
            var h = d.FutureNew(0);
            var task = d.FutureReadAsync(h);
            Assert.True(d.FutureDropReadable(h));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await task);
        }

        // ---- Error context ---------------------------------------------

        [Fact]
        public void ErrorContext_round_trips_debug_message()
        {
            var d = new AsyncDispatcher();
            var h = d.ErrorContextNew("kaboom");
            Assert.Equal("kaboom", d.ErrorContextDebugMessage(h));
            Assert.True(d.ErrorContextDrop(h));
            Assert.False(d.ErrorContextDrop(h));
        }

        [Fact]
        public void ErrorContext_debug_message_throws_on_unknown_handle()
        {
            var d = new AsyncDispatcher();
            Assert.Throws<InvalidOperationException>(
                () => d.ErrorContextDebugMessage(999));
        }

        // ---- Waitable set ----------------------------------------------

        [Fact]
        public void WaitableSet_allocate_and_drop()
        {
            var d = new AsyncDispatcher();
            var h = d.WaitableSetNew();
            Assert.True(h > 0);
            Assert.True(d.WaitableSetDrop(h));
            Assert.False(d.WaitableSetDrop(h));
        }

        [Fact]
        public void WaitableJoin_adds_handle_to_set()
        {
            var d = new AsyncDispatcher();
            var setHandle = d.WaitableSetNew();
            d.WaitableJoin(setHandle, waitableHandle: 7);
            d.WaitableJoin(setHandle, waitableHandle: 9);
            d.WaitableJoin(setHandle, waitableHandle: 7); // idempotent
            var ws = d.WaitableSets.Get(setHandle);
            Assert.NotNull(ws);
            Assert.Equal(2, ws!.Count);
            Assert.True(ws.Contains(7));
            Assert.True(ws.Contains(9));
        }

        [Fact]
        public void WaitableJoin_throws_on_unknown_set()
        {
            var d = new AsyncDispatcher();
            Assert.Throws<InvalidOperationException>(
                () => d.WaitableJoin(999, 1));
        }

        // ---- Task lifecycle (registration only — full ops in Slice D) ----

        [Fact]
        public void RegisterTask_assigns_handle_and_stores_task()
        {
            var d = new AsyncDispatcher();
            var task = d.RegisterTask(MakeCont());
            Assert.True(task.Handle > 0);
            Assert.Same(task, d.Tasks.Get(task.Handle));
            Assert.Equal(ComponentTaskState.Starting, task.State);
        }

        // ---- Subtask basics --------------------------------------------

        [Fact]
        public void Subtask_drop_releases_handle()
        {
            var d = new AsyncDispatcher();
            var parent = d.RegisterTask(MakeCont());
            var child = d.RegisterTask(MakeCont());
            var subHandle = d.Subtasks.Allocate(
                h => new ComponentSubtask(h, child, parent));
            Assert.True(d.SubtaskDrop(subHandle));
            Assert.False(d.SubtaskDrop(subHandle));
        }

        // ---- Current-task stack + task lifecycle (Slice D) -------------

        [Fact]
        public void PushCurrentTask_transitions_Starting_to_Started()
        {
            var d = new AsyncDispatcher();
            var task = d.RegisterTask(MakeCont());
            Assert.Equal(ComponentTaskState.Starting, task.State);
            d.PushCurrentTask(task);
            Assert.Equal(ComponentTaskState.Started, task.State);
            Assert.Same(task, d.CurrentTask);
        }

        [Fact]
        public void PopCurrentTask_returns_task_and_leaves_stack_empty()
        {
            var d = new AsyncDispatcher();
            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            var popped = d.PopCurrentTask();
            Assert.Same(task, popped);
            Assert.Null(d.CurrentTask);
            Assert.Null(d.PopCurrentTask()); // empty stack returns null
        }

        [Fact]
        public async Task TaskReturn_sets_completion_with_value_and_transitions_state()
        {
            var d = new AsyncDispatcher();
            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            d.TaskReturn(null!, "lifted-result");
            Assert.Equal(ComponentTaskState.Returned, task.State);
            Assert.Equal("lifted-result", await task.Completion.Task);
        }

        [Fact]
        public void TaskReturn_throws_when_no_ambient_task()
        {
            var d = new AsyncDispatcher();
            Assert.Throws<InvalidOperationException>(
                () => d.TaskReturn(null!, "x"));
        }

        [Fact]
        public async Task TaskCancel_cancels_completion()
        {
            var d = new AsyncDispatcher();
            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            d.TaskCancel(null!);
            Assert.Equal(ComponentTaskState.Cancelled, task.State);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await task.Completion.Task);
        }

        [Fact]
        public async Task TaskFail_faults_completion_with_exception()
        {
            var d = new AsyncDispatcher();
            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            d.TaskFail(new InvalidOperationException("body-threw"));
            Assert.Equal(ComponentTaskState.Failed, task.State);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await task.Completion.Task);
            Assert.Equal("body-threw", ex.Message);
        }

        // ---- Backpressure ----------------------------------------------

        [Fact]
        public void Backpressure_increments_decrements_and_set_clears()
        {
            var d = new AsyncDispatcher();
            Assert.Equal(0, d.BackpressureLevel);
            d.BackpressureInc();
            d.BackpressureInc();
            Assert.Equal(2, d.BackpressureLevel);
            d.BackpressureDec();
            Assert.Equal(1, d.BackpressureLevel);
            d.BackpressureSet(); // clear to 0
            Assert.Equal(0, d.BackpressureLevel);
            d.BackpressureDec(); // floors at 0
            Assert.Equal(0, d.BackpressureLevel);
        }

        // ---- Context (per-task slots) ----------------------------------

        [Fact]
        public void ContextSet_then_Get_round_trips()
        {
            var d = new AsyncDispatcher();
            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            d.ContextSet(slotIdx: 3, new Value(42));
            var read = d.ContextGet(3);
            Assert.Equal(42, read.Data.Int32);
        }

        [Fact]
        public void ContextGet_returns_default_when_slot_unwritten()
        {
            var d = new AsyncDispatcher();
            var task = d.RegisterTask(MakeCont());
            d.PushCurrentTask(task);
            // Reading an unwritten slot returns the default Value
            // (Type byte 0) without throwing — slots are sparse.
            // Compare the raw type byte rather than via op_Equality
            // (which rejects Undefined ValType comparisons).
            var v = d.ContextGet(99);
            Assert.Equal((Wacs.Core.Types.Defs.ValType)0, v.Type);
        }

        [Fact]
        public void ContextGet_throws_when_no_ambient_task()
        {
            var d = new AsyncDispatcher();
            Assert.Throws<InvalidOperationException>(
                () => d.ContextGet(0));
        }

        // ---- Subtask cancel propagation --------------------------------

        [Fact]
        public async Task SubtaskCancel_transitions_child_state_and_cancels_completion()
        {
            var d = new AsyncDispatcher();
            var parent = d.RegisterTask(MakeCont());
            var child = d.RegisterTask(MakeCont());
            // child must be in a non-terminal state to be cancelable.
            d.PushCurrentTask(child); // transitions to Started
            d.PopCurrentTask();

            var subHandle = d.Subtasks.Allocate(
                h => new ComponentSubtask(h, child, parent));

            d.SubtaskCancel(subHandle, asyncFlag: false);
            Assert.Equal(ComponentTaskState.Cancelled, child.State);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await child.Completion.Task);
        }

        // ---- WaitableSetPoll -------------------------------------------

        [Fact]
        public void WaitableSetPoll_returns_zero_when_no_member_ready()
        {
            var d = new AsyncDispatcher();
            var ws = d.WaitableSetNew();
            var f = d.FutureNew(0);
            d.WaitableJoin(ws, f);
            // Future hasn't been written — not deliverable.
            Assert.Equal(0, d.WaitableSetPoll(null!, ws, 0, false));
        }

        [Fact]
        public void WaitableSetPoll_returns_member_handle_when_future_resolved()
        {
            var d = new AsyncDispatcher();
            var ws = d.WaitableSetNew();
            var f = d.FutureNew(0);
            d.WaitableJoin(ws, f);
            d.FutureWrite(f, "ready");
            Assert.Equal(f, d.WaitableSetPoll(null!, ws, 0, false));
        }

        [Fact]
        public void WaitableSetPoll_picks_first_deliverable_when_multiple_join()
        {
            var d = new AsyncDispatcher();
            var ws = d.WaitableSetNew();
            var f1 = d.FutureNew(0);
            var f2 = d.FutureNew(0);
            d.WaitableJoin(ws, f1);
            d.WaitableJoin(ws, f2);
            d.FutureWrite(f2, "two-is-ready");
            // f2 is deliverable, f1 isn't.
            Assert.Equal(f2, d.WaitableSetPoll(null!, ws, 0, false));
        }

        // ---- Stream/Future cancel-* (now wired in Slice D) -------------

        [Fact]
        public void StreamCancelRead_drops_readable_side()
        {
            var d = new AsyncDispatcher();
            var h = d.StreamNew(0);
            Assert.True(d.StreamCancelRead(h, asyncFlag: false));
        }

        [Fact]
        public async Task FutureCancelRead_cancels_pending_read()
        {
            var d = new AsyncDispatcher();
            var h = d.FutureNew(0);
            var read = d.FutureReadAsync(h);
            Assert.True(d.FutureCancelRead(h, asyncFlag: false));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                async () => await read);
        }

        // ---- Thread.yield no-op for single-task model ------------------

        [Fact]
        public void ThreadYield_is_synchronous_noop()
        {
            var d = new AsyncDispatcher();
            // Should return without throwing or suspending; the
            // single-task model has nothing to yield to.
            d.ThreadYield(null!, cancellable: false);
            d.ThreadYield(null!, cancellable: true);
        }

        // ---- WaitableSetWait (now implemented as blocking WaitAny) -----

        [Fact]
        public void WaitableSetWait_returns_zero_for_empty_set()
        {
            var d = new AsyncDispatcher();
            var ws = d.WaitableSetNew();
            // No members joined — wait returns 0 (canon null) immediately.
            Assert.Equal(0, d.WaitableSetWait(null!, ws, 0, false));
        }

        [Fact]
        public void WaitableSetWait_returns_member_already_deliverable()
        {
            var d = new AsyncDispatcher();
            var ws = d.WaitableSetNew();
            var f = d.FutureNew(0);
            d.WaitableJoin(ws, f);
            d.FutureWrite(f, "ready"); // future already resolved

            // Pre-deliverable member — wait returns it without blocking.
            Assert.Equal(f, d.WaitableSetWait(null!, ws, 0, false));
        }

        [Fact]
        public async Task WaitableSetWait_unblocks_when_future_resolves_asynchronously()
        {
            var d = new AsyncDispatcher();
            var ws = d.WaitableSetNew();
            var f = d.FutureNew(0);
            d.WaitableJoin(ws, f);

            // Kick off the blocking wait on a background thread.
            var waitTask = Task.Run(() =>
                d.WaitableSetWait(null!, ws, 0, false));

            // Future not yet resolved — wait should still be running.
            await Task.Delay(50);
            Assert.False(waitTask.IsCompleted);

            // Resolve the future — wait wakes and returns the handle.
            d.FutureWrite(f, "now-ready");
            var deliverable = await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(f, deliverable);
        }

        [Fact]
        public async Task WaitableSetWait_returns_first_resolver_when_multiple_pending()
        {
            var d = new AsyncDispatcher();
            var ws = d.WaitableSetNew();
            var f1 = d.FutureNew(0);
            var f2 = d.FutureNew(0);
            d.WaitableJoin(ws, f1);
            d.WaitableJoin(ws, f2);

            var waitTask = Task.Run(() =>
                d.WaitableSetWait(null!, ws, 0, false));

            await Task.Delay(20);
            d.FutureWrite(f2, "two-first"); // f2 resolves first
            var deliverable = await waitTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(f2, deliverable);
        }
    }
}
