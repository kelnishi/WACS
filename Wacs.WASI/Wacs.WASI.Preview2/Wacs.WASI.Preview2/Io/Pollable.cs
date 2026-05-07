// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using Wacs.WASI.Preview2.HostBinding;

namespace Wacs.WASI.Preview2.Io
{
    /// <summary>
    /// Host representation of <c>wasi:io/poll@0.2.x</c>'s
    /// <c>pollable</c> resource.
    /// <code>
    /// interface poll {
    ///     resource pollable {
    ///         ready: func() -&gt; bool;
    ///         block: func();
    ///     }
    /// }
    /// </code>
    ///
    /// <para>A pollable represents some asynchronous condition
    /// (a stream becoming ready, a timer firing, etc.). Hosts
    /// providing pollables — typically as the return type of
    /// other interfaces' methods like
    /// <c>monotonic-clock.subscribe-instant</c> — implement
    /// the conditions internally.</para>
    ///
    /// <para>This base class is the simplest "always-ready"
    /// pollable: <see cref="Ready"/> returns true immediately,
    /// <see cref="Block"/> returns immediately. Useful as a
    /// stub for guests that just want to verify the wiring;
    /// concrete subclasses ship for real readiness:
    /// <list type="bullet">
    /// <item><see cref="ManualResetPollable"/> — explicit
    /// signal/reset via <see cref="ManualResetEventSlim"/>.
    /// Hosts use this when they hold the readiness condition
    /// (e.g. a queue with items appended).</item>
    /// <item><see cref="TimerPollable"/> — fires after a
    /// specified duration. Default for clocks'
    /// subscribe-instant / subscribe-duration.</item>
    /// <item>Cross-cutting: <c>TaskBackedPollable</c>
    /// (HTTP module) wraps a <see cref="Task"/>, and
    /// <c>SocketReadyPollable</c> (Sockets module) wraps a
    /// <see cref="System.Net.Sockets.Socket"/>'s readiness.
    /// </item>
    /// </list></para>
    ///
    /// <para>Subclasses that have a backing <see cref="Task"/>
    /// can override <see cref="AsTask"/> so
    /// <c>IPoll.Poll(IPollable[])</c> implementations can use
    /// <see cref="Task.WhenAny(System.Collections.Generic.IEnumerable{Task})"/>
    /// for efficient multi-pollable waits.</para>
    /// </summary>
    [WasiResource("pollable")]
    public class Pollable : IPollable, IDisposable
    {
        /// <summary>True iff the underlying condition has
        /// already been triggered. Default: always ready.
        /// Subclasses override for conditions that are
        /// genuinely asynchronous.</summary>
        public virtual bool Ready() => true;

        /// <summary>Block (synchronously) until the underlying
        /// condition is met. Default: returns immediately.
        /// Real implementations should integrate with the
        /// host's async machinery — but synchronous block is
        /// a valid degraded mode for guests that don't care
        /// about throughput.</summary>
        public virtual void Block() { }

        /// <summary>Optional fast-path for multi-pollable
        /// waits. When non-null, the value completes when
        /// <see cref="Ready"/> would return true; null falls
        /// back to a polling loop in
        /// <see cref="PollSource"/>. Default returns a
        /// pre-completed task — matches the always-ready base
        /// class's semantics. Subclasses with a backing Task
        /// (HTTP / sockets / timer) override.</summary>
        public virtual Task AsTask() => Task.CompletedTask;

        public virtual void Dispose() { }
    }

    /// <summary>
    /// Pollable backed by a
    /// <see cref="ManualResetEventSlim"/>. Hosts call
    /// <see cref="Signal"/> when the condition fires;
    /// <see cref="Block"/> waits for it. The reset to non-
    /// signaled state is via <see cref="Reset"/> — useful
    /// for reusable pollables (e.g. queue-empty sentinels).
    ///
    /// <para>Thread-safe — designed for the common pattern
    /// where one thread produces (e.g. enqueues a message)
    /// and another waits.</para>
    /// </summary>
    public class ManualResetPollable : Pollable
    {
        private readonly ManualResetEventSlim _ev;
        private TaskCompletionSource<bool>? _tcs;
        private readonly object _lock = new();

        public ManualResetPollable(bool initiallySignaled = false)
        {
            _ev = new ManualResetEventSlim(initiallySignaled);
        }

        public override bool Ready() => _ev.IsSet;

        public override void Block() => _ev.Wait();

        public override Task AsTask()
        {
            if (_ev.IsSet) return Task.CompletedTask;
            lock (_lock)
            {
                if (_ev.IsSet) return Task.CompletedTask;
                if (_tcs == null)
                {
                    _tcs = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
                return _tcs.Task;
            }
        }

        /// <summary>Signal readiness. Idempotent — repeated
        /// calls are no-ops. Wakes any threads
        /// <see cref="Block"/>ed on this pollable + completes
        /// any tasks returned from <see cref="AsTask"/>.
        /// </summary>
        public void Signal()
        {
            _ev.Set();
            TaskCompletionSource<bool>? tcs;
            lock (_lock) { tcs = _tcs; _tcs = null; }
            tcs?.TrySetResult(true);
        }

        /// <summary>Clear the signal. New
        /// <see cref="Block"/> calls will wait again.</summary>
        public void Reset() => _ev.Reset();

        public override void Dispose()
        {
            _ev.Dispose();
            base.Dispose();
        }
    }

    /// <summary>
    /// Pollable that fires after a specified duration. Used
    /// by <c>wasi:clocks/monotonic-clock.subscribe-instant</c>
    /// and <c>subscribe-duration</c> to back wall-clock-aware
    /// timers and timeouts.
    ///
    /// <para>Backed by <see cref="Task.Delay(TimeSpan)"/>
    /// internally — the underlying timer fires at construction
    /// time, so any <see cref="AsTask"/> call returns the
    /// pending delay task directly.</para>
    /// </summary>
    public sealed class TimerPollable : Pollable
    {
        private readonly Task _delay;

        public TimerPollable(TimeSpan duration)
        {
            _delay = duration <= TimeSpan.Zero
                ? Task.CompletedTask
                : Task.Delay(duration);
        }

        public TimerPollable(ulong nanoseconds)
            : this(TimeSpan.FromTicks(
                (long)(nanoseconds / 100UL))) { }

        public override bool Ready() => _delay.IsCompleted;
        public override void Block() => _delay.Wait();
        public override Task AsTask() => _delay;
    }
}
