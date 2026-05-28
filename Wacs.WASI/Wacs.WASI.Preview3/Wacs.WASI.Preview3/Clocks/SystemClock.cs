// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview3.Clocks
{
    /// <summary>
    /// Spec <c>record instant { seconds: s64, nanoseconds: u32 }</c>
    /// from <c>wasi:clocks/system-clock</c>. Wire layout (canon
    /// ABI): 16 bytes, 8-aligned — s64 at +0, u32 at +8, 4-byte
    /// tail pad. Note: even when <see cref="Seconds"/> is
    /// negative, incrementing <see cref="Nanoseconds"/> always
    /// moves forward in time per the spec definition.
    /// </summary>
    public readonly struct Instant
    {
        public long Seconds { get; }
        public uint Nanoseconds { get; }

        public Instant(long seconds, uint nanoseconds)
        {
            Seconds = seconds;
            Nanoseconds = nanoseconds;
        }
    }

    /// <summary>
    /// Host interface for <c>wasi:clocks/system-clock@0.3.0-rc-2026-03-15</c>.
    /// Replaces Preview 2's <c>wall-clock</c> — same shape but the
    /// time-since-epoch is now signed and the record is named
    /// <c>instant</c>. The clock isn't necessarily monotonic
    /// (resets are allowed), so it's only safe for "what time is
    /// it" use, not for elapsed-time measurement.
    /// </summary>
    public interface ISystemClock
    {
        /// <summary><c>now: func() -> instant</c> — current point
        /// in time as seconds + nanoseconds since the Unix epoch
        /// (1970-01-01T00:00:00Z).</summary>
        Instant Now();

        /// <summary><c>get-resolution: func() -> duration</c> —
        /// smallest reportable distinguishable interval, in
        /// nanoseconds.</summary>
        ulong GetResolution();
    }

    /// <summary>
    /// Default <see cref="ISystemClock"/> implementation backed
    /// by <see cref="DateTimeOffset.UtcNow"/>. Tick precision
    /// (100 ns on most platforms) sets the resolution floor.
    /// </summary>
    public sealed class SystemClock : ISystemClock
    {
        private static readonly DateTime UnixEpoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public Instant Now()
        {
            var nowUtc = DateTime.UtcNow;
            // delta.Ticks is signed 100-ns units since the epoch.
            // Negative values are possible if the clock has been
            // skewed earlier than 1970 (rare but spec-permitted).
            long ticks = (nowUtc - UnixEpoch).Ticks;
            long totalNs = ticks * 100L;

            long seconds;
            uint nanoseconds;
            if (totalNs >= 0)
            {
                seconds = totalNs / 1_000_000_000L;
                nanoseconds = (uint)(totalNs % 1_000_000_000L);
            }
            else
            {
                // Floor division: -1.5s = (seconds: -2, nanos: 500_000_000)
                // per the spec's "incrementing nanoseconds always
                // moves forward" rule.
                long abs = -totalNs;
                long absSec = abs / 1_000_000_000L;
                long absNs = abs % 1_000_000_000L;
                if (absNs == 0)
                {
                    seconds = -absSec;
                    nanoseconds = 0;
                }
                else
                {
                    seconds = -absSec - 1;
                    nanoseconds = (uint)(1_000_000_000L - absNs);
                }
            }
            return new Instant(seconds, nanoseconds);
        }

        public ulong GetResolution()
        {
            // .NET DateTime ticks are 100 ns → that's the
            // smallest distinguishable step the runtime provides.
            return 100UL;
        }
    }
}
