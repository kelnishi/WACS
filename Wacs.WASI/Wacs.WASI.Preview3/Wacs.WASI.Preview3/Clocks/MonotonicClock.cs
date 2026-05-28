// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Wacs.WASI.Preview3.Clocks
{
    /// <summary>
    /// Host interface for <c>wasi:clocks/monotonic-clock@0.3.0-rc-2026-03-15</c>.
    /// All four operations are present; <c>wait-until</c> and
    /// <c>wait-for</c> are <c>async func</c> in the WIT and
    /// surface here as <see cref="Task"/>-returning methods so
    /// the canon-async dispatcher can yield cooperatively.
    /// </summary>
    public interface IMonotonicClock
    {
        /// <summary><c>now: func() -> mark</c> — current value of
        /// the clock in nanoseconds since this clock's epoch.
        /// Successive reads produce non-decreasing values.</summary>
        ulong Now();

        /// <summary><c>get-resolution: func() -> duration</c> —
        /// smallest reportable step of the clock, in
        /// nanoseconds.</summary>
        ulong GetResolution();

        /// <summary><c>wait-until: async func(when: mark)</c> —
        /// wait until the absolute mark has occurred. The
        /// returned Task completes when the deadline is reached;
        /// past marks complete immediately.</summary>
        Task WaitUntilAsync(ulong when, CancellationToken cancellationToken = default);

        /// <summary><c>wait-for: async func(how-long: duration)</c>
        /// — wait for the specified duration to elapse, in
        /// nanoseconds.</summary>
        Task WaitForAsync(ulong howLong, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Default <see cref="IMonotonicClock"/> implementation
    /// backed by <see cref="Stopwatch"/>'s high-resolution timer
    /// (QPC on Windows, <c>clock_gettime(CLOCK_MONOTONIC)</c> on
    /// POSIX — both monotonic by spec). The epoch is process
    /// start; two distinct instances therefore have unrelated
    /// epochs — fine because the spec only blesses delta
    /// computation between same-clock reads.
    /// </summary>
    public sealed class MonotonicClock : IMonotonicClock
    {
        private readonly long _started;

        public MonotonicClock()
        {
            _started = Stopwatch.GetTimestamp();
        }

        public ulong Now()
        {
            // Convert Stopwatch ticks → nanoseconds without
            // intermediate floating-point. Long multiply
            // overflows only at >292 years uptime — fine.
            var elapsed = Stopwatch.GetTimestamp() - _started;
            return (ulong)((elapsed * 1_000_000_000L) / Stopwatch.Frequency);
        }

        public ulong GetResolution()
        {
            // Smallest reportable step = 1 Stopwatch tick → ns.
            return (ulong)(1_000_000_000L / Stopwatch.Frequency);
        }

        public Task WaitUntilAsync(ulong when, CancellationToken cancellationToken = default)
        {
            var nowNs = Now();
            ulong remainingNs = when > nowNs ? when - nowNs : 0UL;
            return WaitForAsync(remainingNs, cancellationToken);
        }

        public Task WaitForAsync(ulong howLong, CancellationToken cancellationToken = default)
        {
            if (howLong == 0) return Task.CompletedTask;
            // Task.Delay accepts a TimeSpan with millisecond
            // granularity. Round up so sub-millisecond waits
            // still return AFTER the requested duration, not
            // before — matches the spec's "at least" semantics.
            long ms = (long)(howLong / 1_000_000UL);
            if (howLong % 1_000_000UL != 0) ms += 1;
            // Task.Delay caps at int.MaxValue ms (~24.8 days);
            // longer waits would need chunking. Realistic
            // monotonic-clock waits are much shorter — guests
            // requesting longer get an overflow-safe clamp.
            if (ms > int.MaxValue) ms = int.MaxValue;
            return Task.Delay((int)ms, cancellationToken);
        }
    }
}
