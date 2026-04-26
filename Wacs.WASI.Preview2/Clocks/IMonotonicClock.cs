// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

namespace Wacs.WASI.Preview2.Clocks
{
    /// <summary>
    /// Host-side surface for <c>wasi:clocks/monotonic-clock@0.2.x</c>.
    /// A clock that's guaranteed to advance at a roughly steady
    /// rate without ever going backwards — what
    /// <c>System.Diagnostics.Stopwatch</c> exposes on .NET.
    /// <code>
    /// interface monotonic-clock {
    ///     type instant = u64;
    ///     type duration = u64;
    ///     now: func() -&gt; instant;
    ///     resolution: func() -&gt; duration;
    /// }
    /// </code>
    /// <para>Both return values are <c>u64</c> nanoseconds —
    /// <c>now</c> is the elapsed-since-arbitrary-epoch reading,
    /// <c>resolution</c> is the smallest non-zero step the clock
    /// can report.</para>
    /// </summary>
    public interface IMonotonicClock
    {
        /// <summary>Current monotonic-clock reading, in
        /// nanoseconds since an arbitrary fixed epoch (typically
        /// process start). Two consecutive calls return values
        /// where <c>second &gt;= first</c>.</summary>
        ulong Now();

        /// <summary>Smallest non-zero step the clock can
        /// distinguish, in nanoseconds. Loosely "1 / clock-Hz"
        /// — the minimum delta between two distinct
        /// <see cref="Now"/> readings under maximum sampling
        /// pressure.</summary>
        ulong Resolution();
    }
}
