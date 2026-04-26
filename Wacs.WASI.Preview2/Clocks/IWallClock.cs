// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Clocks
{
    /// <summary>
    /// Host-side surface for <c>wasi:clocks/wall-clock@0.2.x</c>.
    /// Wall-clock time — seconds since the Unix epoch — as
    /// opposed to the monotonic clock in
    /// <see cref="IMonotonicClock"/>. Subject to time zone
    /// adjustment, NTP corrections, leap seconds, etc., so
    /// guests should use it for human-facing timestamps but
    /// never for measuring elapsed time.
    /// <code>
    /// interface wall-clock {
    ///     record datetime { seconds: u64, nanoseconds: u32 }
    ///     now: func() -&gt; datetime;
    ///     resolution: func() -&gt; datetime;
    /// }
    /// </code>
    /// </summary>
    public interface IWallClock
    {
        /// <summary>Current wall-clock reading. Seconds count
        /// from the Unix epoch (1970-01-01 UTC); nanoseconds
        /// add sub-second precision in the [0, 1_000_000_000)
        /// range.</summary>
        Datetime Now();

        /// <summary>Smallest non-zero step the clock can
        /// distinguish, expressed as a datetime delta —
        /// typically &lt;1 second + some nanoseconds. The seconds
        /// field is conventionally 0 unless the underlying clock
        /// is unusually coarse.</summary>
        Datetime Resolution();
    }
}
