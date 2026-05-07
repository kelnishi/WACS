// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;

namespace Wacs.WASI.Preview2.Clocks
{
    /// <summary>
    /// Default <see cref="IWallClock"/> implementation backed
    /// by <see cref="DateTimeOffset.UtcNow"/>. Tick precision
    /// (100 ns on Windows; varies elsewhere) sets the floor on
    /// reportable resolution.
    /// </summary>
    public sealed class WallClock : IWallClock
    {
        // Unix epoch as a DateTime so we can compute deltas
        // without allocating intermediates per call.
        private static readonly DateTime UnixEpoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public Datetime Now()
        {
            var nowUtc = DateTime.UtcNow;
            var delta = nowUtc - UnixEpoch;
            // delta.Ticks is 100 ns units since the epoch.
            var totalNs = delta.Ticks * 100L;
            return new Datetime
            {
                Seconds = (ulong)(totalNs / 1_000_000_000L),
                Nanoseconds = (uint)(totalNs % 1_000_000_000L),
            };
        }

        public Datetime Resolution()
        {
            // .NET DateTime ticks are 100 ns — that's the
            // smallest reportable step. Returned as 0 seconds
            // + 100 nanoseconds.
            return new Datetime
            {
                Seconds = 0,
                Nanoseconds = 100,
            };
        }
    }
}
