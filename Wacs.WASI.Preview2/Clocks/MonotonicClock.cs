// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.Diagnostics;

namespace Wacs.WASI.Preview2.Clocks
{
    /// <summary>
    /// Default <see cref="IMonotonicClock"/> implementation
    /// backed by <see cref="Stopwatch"/>'s high-resolution timer.
    /// On .NET 8+ this is QPC on Windows / clock_gettime
    /// (CLOCK_MONOTONIC) on POSIX — both monotonic by spec.
    ///
    /// <para>The epoch is process start (the <c>_started</c>
    /// timestamp captured at construction). Two distinct
    /// instances therefore have unrelated epochs — fine for
    /// guests that compute deltas between two of their own
    /// <c>now()</c> calls, which is the only valid use of a
    /// monotonic clock per the WASI spec.</para>
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
            // Convert ticks → nanoseconds. Stopwatch's tick
            // frequency is platform-defined; the multiplier
            // keeps full precision without intermediate
            // floating-point.
            var elapsed = Stopwatch.GetTimestamp() - _started;
            return (ulong)((elapsed * 1_000_000_000L) / Stopwatch.Frequency);
        }

        public ulong Resolution()
        {
            // Smallest reportable step = 1 tick → ns.
            return (ulong)(1_000_000_000L / Stopwatch.Frequency);
        }
    }
}
