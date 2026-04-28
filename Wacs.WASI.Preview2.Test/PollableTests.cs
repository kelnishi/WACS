// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Wacs.WASI.Preview2.Io;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    /// <summary>
    /// Verifies the pollable primitives —
    /// <see cref="ManualResetPollable"/>,
    /// <see cref="TimerPollable"/>, the always-ready base
    /// class, and <see cref="PollSource"/>'s
    /// <c>Task.WhenAny</c>-driven multi-pollable wait.
    /// </summary>
    public class PollableTests
    {
        // ---------- ManualResetPollable ------------------------

        [Fact]
        public void ManualResetPollable_starts_unsignaled_then_Signal_unblocks()
        {
            var p = new ManualResetPollable();
            Assert.False(p.Ready());

            int blocked = 0;
            var t = new Thread(() =>
            {
                p.Block();
                Interlocked.Increment(ref blocked);
            });
            t.Start();

            // Give the worker a moment to enter Block.
            Assert.Equal(0, Volatile.Read(ref blocked));
            p.Signal();
            Assert.True(t.Join(TimeSpan.FromSeconds(5)),
                "thread should unblock after Signal");
            Assert.Equal(1, blocked);
            Assert.True(p.Ready());
        }

        [Fact]
        public void ManualResetPollable_initially_signaled_returns_immediately()
        {
            var p = new ManualResetPollable(initiallySignaled: true);
            Assert.True(p.Ready());
            // Block returns synchronously — no thread needed.
            p.Block();
        }

        [Fact]
        public void ManualResetPollable_AsTask_completes_on_Signal()
        {
            var p = new ManualResetPollable();
            var task = p.AsTask();
            Assert.False(task.IsCompleted);

            p.Signal();
            // Task.Wait with timeout to surface deadlocks rather
            // than hang the test runner.
            Assert.True(task.Wait(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void ManualResetPollable_Reset_returns_to_unsignaled_state()
        {
            var p = new ManualResetPollable(initiallySignaled: true);
            Assert.True(p.Ready());
            p.Reset();
            Assert.False(p.Ready());
        }

        // ---------- TimerPollable ------------------------------

        [Fact]
        public void TimerPollable_zero_duration_is_immediately_ready()
        {
            var p = new TimerPollable(TimeSpan.Zero);
            Assert.True(p.Ready());
        }

        [Fact]
        public void TimerPollable_small_delay_fires_within_reasonable_time()
        {
            var p = new TimerPollable(TimeSpan.FromMilliseconds(50));
            Assert.False(p.Ready());
            // Block waits exactly the delay; bound the
            // assertion at 5s so a hung test runner surfaces.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            p.Block();
            sw.Stop();
            Assert.True(p.Ready());
            Assert.InRange(sw.ElapsedMilliseconds, 30L, 5000L);
        }

        [Fact]
        public void TimerPollable_AsTask_completes_after_delay()
        {
            var p = new TimerPollable(TimeSpan.FromMilliseconds(20));
            Assert.True(p.AsTask().Wait(TimeSpan.FromSeconds(5)));
        }

        // ---------- PollSource (Task.WhenAny driven) -----------

        [Fact]
        public void PollSource_returns_indices_of_ready_pollables_immediately()
        {
            var poll = new PollSource();
            var p1 = new TimerPollable(TimeSpan.Zero);   // ready
            var p2 = new ManualResetPollable();          // not ready
            var p3 = new TimerPollable(TimeSpan.Zero);   // ready

            var indices = poll.Poll(new IPollable[] { p1, p2, p3 });
            Assert.Equal(new uint[] { 0, 2 }, indices);
        }

        [Fact]
        public void PollSource_blocks_when_none_ready_then_returns_first_to_fire()
        {
            var poll = new PollSource();
            var p1 = new ManualResetPollable();   // signal later
            var p2 = new ManualResetPollable();   // never

            // Schedule a signal on a worker after a short delay.
            Task.Run(async () =>
            {
                await Task.Delay(50);
                p1.Signal();
            });

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var indices = poll.Poll(new IPollable[] { p1, p2 });
            sw.Stop();

            Assert.Single(indices);
            Assert.Equal(0u, indices[0]);
            Assert.True(sw.ElapsedMilliseconds < 5000);
        }

        [Fact]
        public void PollSource_empty_input_returns_empty_array_immediately()
        {
            var poll = new PollSource();
            var indices = poll.Poll(System.Array.Empty<IPollable>());
            Assert.Empty(indices);
        }

        // ---------- Default Pollable still always-ready --------

        [Fact]
        public void Default_Pollable_remains_always_ready_for_back_compat()
        {
            // The bare base class is the "stub" — guests that
            // get a default Pollable see it complete
            // immediately. Concrete subclasses override.
            var p = new Pollable();
            Assert.True(p.Ready());
            p.Block();   // returns immediately
            Assert.True(p.AsTask().IsCompleted);
        }
    }
}
