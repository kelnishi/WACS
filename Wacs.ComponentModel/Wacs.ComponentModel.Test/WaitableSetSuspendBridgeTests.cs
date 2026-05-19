// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Concurrency;
using Wacs.Core.Types;
using Xunit;

namespace Wacs.ComponentModel.Test
{
    /// <summary>
    /// Phase 3 Slice L coverage: the WaitableSet wait/poll suspend
    /// bridge — <see cref="AsyncDispatcher.WaitableSetWaitAsync"/>
    /// cooperative-yield variant + the lift adapter's
    /// <see cref="AsyncLiftAdapter.InvokeAsync(AsyncDispatcher, ContInstance, Func{Task})"/>
    /// async-body overload.
    /// </summary>
    public class WaitableSetSuspendBridgeTests
    {
        private static ContInstance MakeCont()
        {
            var store = new ContinuationStore();
            return store.Allocate(
                (TypeIdx)0, (Delegate)(Func<int>)(() => 0));
        }

        // ---- Sync entry preserved -------------------------------------

        [Fact]
        public void WaitableSetWait_returns_first_already_deliverable_member()
        {
            // Sync entry must still return immediately when a
            // member is already deliverable — no blocking.
            var d = new AsyncDispatcher();
            var wsHandle = d.WaitableSetNew();

            // Allocate a future and complete it.
            var futureHandle = d.FutureNew(typeIdx: 0);
            d.FutureWrite(futureHandle, /* ok */ null);

            d.WaitableJoin(wsHandle, futureHandle);

            var got = d.WaitableSetWait(
                ctx: null!, wsHandle, memoryIdx: 0, cancellable: false);
            Assert.Equal(futureHandle, got);
        }

        [Fact]
        public void WaitableSetWait_returns_zero_on_empty_set()
        {
            var d = new AsyncDispatcher();
            var wsHandle = d.WaitableSetNew();
            Assert.Equal(0, d.WaitableSetWait(
                null!, wsHandle, 0, false));
        }

        [Fact]
        public void WaitableSetWait_unknown_handle_throws()
        {
            var d = new AsyncDispatcher();
            var ex = Assert.Throws<InvalidOperationException>(
                () => d.WaitableSetWait(null!, 999, 0, false));
            Assert.Contains("999", ex.Message);
        }

        // ---- Async entry yields cooperatively -------------------------

        [Fact]
        public async Task WaitableSetWaitAsync_returns_first_already_deliverable()
        {
            var d = new AsyncDispatcher();
            var wsHandle = d.WaitableSetNew();
            var futureHandle = d.FutureNew(0);
            d.FutureWrite(futureHandle, null);
            d.WaitableJoin(wsHandle, futureHandle);

            var got = await d.WaitableSetWaitAsync(
                null!, wsHandle, 0, false);
            Assert.Equal(futureHandle, got);
        }

        [Fact]
        public async Task WaitableSetWaitAsync_completes_when_member_becomes_ready()
        {
            var d = new AsyncDispatcher();
            var wsHandle = d.WaitableSetNew();
            var futureHandle = d.FutureNew(0);
            d.WaitableJoin(wsHandle, futureHandle);

            // Start the wait; nothing is deliverable yet.
            var waitTask = d.WaitableSetWaitAsync(
                null!, wsHandle, 0, false);
            Assert.False(waitTask.IsCompleted);

            // Complete the future on the thread-pool; the wait
            // task should observe and return promptly.
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                d.FutureWrite(futureHandle, null);
            });

            var got = await waitTask;
            Assert.Equal(futureHandle, got);
        }

        [Fact]
        public async Task WaitableSetWaitAsync_does_not_block_the_calling_thread()
        {
            // While the wait is parked the calling task can do
            // other CLR work. Demonstrate by interleaving a counter
            // increment with the wait — the increment should run
            // before the wait completes.
            var d = new AsyncDispatcher();
            var wsHandle = d.WaitableSetNew();
            var futureHandle = d.FutureNew(0);
            d.WaitableJoin(wsHandle, futureHandle);

            int counter = 0;
            var counterRunning = Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(10);
                    Interlocked.Increment(ref counter);
                }
                d.FutureWrite(futureHandle, null);
            });

            var got = await d.WaitableSetWaitAsync(
                null!, wsHandle, 0, false);

            await counterRunning;
            Assert.Equal(5, counter);
            Assert.Equal(futureHandle, got);
        }

        [Fact]
        public async Task WaitableSetWaitAsync_honors_cancellation_token()
        {
            var d = new AsyncDispatcher();
            var wsHandle = d.WaitableSetNew();
            var futureHandle = d.FutureNew(0);
            d.WaitableJoin(wsHandle, futureHandle);

            using var cts = new CancellationTokenSource();
            var waitTask = d.WaitableSetWaitAsync(
                null!, wsHandle, 0, false, cts.Token);

            cts.CancelAfter(20);

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => waitTask);
        }

        // ---- Async lift-adapter overload ------------------------------

        [Fact]
        public async Task InvokeAsync_func_task_runs_body_and_completes()
        {
            var d = new AsyncDispatcher();
            bool bodyRan = false;
            var hostTask = AsyncLiftAdapter.InvokeAsync(
                d, MakeCont(), async () =>
                {
                    await Task.Yield();
                    bodyRan = true;
                });

            Assert.Null(await hostTask);
            Assert.True(bodyRan);
            Assert.Null(d.CurrentTask);
        }

        [Fact]
        public async Task InvokeAsync_func_task_body_yields_via_WaitableSetWaitAsync()
        {
            // The async body cooperatively yields via the dispatcher's
            // async wait. The host-side producer wakes the wait;
            // the body resumes, calls task.return, and the host
            // observes the lifted result.
            var d = new AsyncDispatcher();
            var futureHandle = d.FutureNew(0);

            var hostTask = AsyncLiftAdapter.InvokeAsync(
                d, MakeCont(), async () =>
                {
                    var ws = d.WaitableSetNew();
                    d.WaitableJoin(ws, futureHandle);
                    int got = await d.WaitableSetWaitAsync(
                        null!, ws, 0, false);
                    d.TaskReturn(null!, got);
                });

            // Producer thread completes the future after a delay.
            _ = Task.Run(async () =>
            {
                await Task.Delay(30);
                d.FutureWrite(futureHandle, null);
            });

            var result = await hostTask;
            Assert.Equal(futureHandle, result);
        }

        [Fact]
        public async Task InvokeAsync_func_task_body_exception_faults_task()
        {
            var d = new AsyncDispatcher();
            var hostTask = AsyncLiftAdapter.InvokeAsync(
                d, MakeCont(), async () =>
                {
                    await Task.Yield();
                    throw new InvalidOperationException("boom");
                });

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => hostTask);
            Assert.Equal("boom", ex.Message);
        }

        [Fact]
        public async Task InvokeAsync_func_task_body_cancellation_marks_cancelled()
        {
            // OperationCanceledException from the body is treated
            // as a soft cancellation — the task transitions to
            // Cancelled and the awaiter sees a canceled task.
            var d = new AsyncDispatcher();
            var hostTask = AsyncLiftAdapter.InvokeAsync(
                d, MakeCont(), async () =>
                {
                    await Task.Yield();
                    throw new OperationCanceledException();
                });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => hostTask);
        }

        [Fact]
        public async Task Two_async_bodies_can_interleave_on_one_thread()
        {
            // The whole point of the cooperative-yield path: two
            // tasks that each take a "long" wait can complete in
            // ~max(t1, t2) wall time on a single CLR thread,
            // because each one yields while parked.
            //
            // Smoke-test: kick off two async bodies that each wait
            // on a different future, both wakes happen ~together,
            // both bodies complete promptly. This wouldn't be
            // possible with sync WaitableSetWait if both ran on
            // one thread; with the async path the runtime can
            // schedule both on the thread pool.
            var d = new AsyncDispatcher();
            var f1 = d.FutureNew(0);
            var f2 = d.FutureNew(0);

            async Task<object?> Body(int futureHandle)
            {
                return await AsyncLiftAdapter.InvokeAsync(
                    d, MakeCont(), async () =>
                    {
                        var ws = d.WaitableSetNew();
                        d.WaitableJoin(ws, futureHandle);
                        int got = await d.WaitableSetWaitAsync(
                            null!, ws, 0, false);
                        d.TaskReturn(null!, got);
                    });
            }

            var t1 = Body(f1);
            var t2 = Body(f2);

            _ = Task.Run(async () =>
            {
                await Task.Delay(30);
                d.FutureWrite(f1, null);
                d.FutureWrite(f2, null);
            });

            var results = await Task.WhenAll(t1, t2);
            Assert.Equal(f1, results[0]);
            Assert.Equal(f2, results[1]);
        }
    }
}
