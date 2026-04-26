// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.HostBinding;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    public class WallClockTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        [Fact]
        public void WallClock_Now_returns_seconds_since_unix_epoch()
        {
            // Sanity: the seconds field should be in the
            // "around now" range — between the 2025 release date
            // (1735689600 = 2025-01-01) and 2100 (4102444800).
            var clock = new WallClock();
            var d = clock.Now();
            Assert.True(d.Seconds > 1_735_689_600UL,
                $"datetime.seconds {d.Seconds} predates 2025");
            Assert.True(d.Seconds < 4_102_444_800UL,
                $"datetime.seconds {d.Seconds} is past 2100");
            Assert.True(d.Nanoseconds < 1_000_000_000U,
                "datetime.nanoseconds out of [0, 1e9) range");
        }

        [Fact]
        public void BindWasiInstance_writes_record_to_guest_retArea()
        {
            // wasi-wall-clock-component imports wall-clock.now ->
            // datetime, exports wall-now -> tuple<u64, u32> that
            // reads back the (seconds, nanoseconds) the host
            // wrote into retArea via canon-lower wrapper. The
            // BindRecordReturnMethod path computes per-field
            // canonical-ABI offsets (0 for u64, 8 for u32) and
            // writes both fields little-endian.
            //
            // Stub the host so we can assert exact values.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-wall-clock-component", "wc.component.wasm"));
            var stub = new StubWallClock(
                seconds: 1_700_000_000UL,
                nanoseconds: 123_456_789U);
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                runtime.BindWasiInstance(
                    "wasi:clocks/wall-clock@0.2.3", stub));

            var tuple = ((ulong, uint))ci.Invoke("wall-now")!;
            Assert.Equal(1_700_000_000UL, tuple.Item1);
            Assert.Equal(123_456_789U, tuple.Item2);
        }

        private sealed class StubWallClock : IWallClock
        {
            private readonly Datetime _value;
            public StubWallClock(ulong seconds, uint nanoseconds)
            {
                _value = new Datetime
                {
                    Seconds = seconds,
                    Nanoseconds = nanoseconds,
                };
            }
            public Datetime Now() => _value;
            public Datetime Resolution() => new Datetime
            {
                Seconds = 0,
                Nanoseconds = 100,
            };
        }
    }
}
