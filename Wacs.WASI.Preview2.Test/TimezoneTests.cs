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
    /// <summary>
    /// Tests for wasi:clocks/timezone — exercises the new
    /// record-of-primitives param canon-lower (datetime
    /// param flattens to (i64 seconds, i32 nanoseconds) on
    /// the wasm stack).
    /// </summary>
    public class TimezoneTests
    {
        private static string FindFixturePath(string fixtureDir, string fileName)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WACS.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "Spec.Test", "components",
                                "fixtures", fixtureDir, "wasm", fileName);
        }

        private sealed class StubTimezone : ITimezone
        {
            private readonly int _offsetSeconds;
            public StubTimezone(int offsetSeconds)
            { _offsetSeconds = offsetSeconds; }

            // Just echo a configured offset — tests don't care
            // about the datetime content.
            public int UtcOffset(Datetime when) => _offsetSeconds;
        }

        [Fact]
        public void UtcOffset_record_param_round_trips_through_wasm()
        {
            // Fixture's ask-offset(seconds, nanos) calls
            // utc-offset(datetime{seconds, nanoseconds}). The
            // record param flattens to (i64, i32) on the wire;
            // the auto-binder reconstructs the Datetime struct
            // on the host side via NewExpression + member init.
            // Stub returns a fixed offset; test verifies it
            // round-trips.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-timezone-component", "tz.component.wasm"));
            var stub = new StubTimezone(offsetSeconds: -28800);   // PST
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                runtime.BindWasiInstance(
                    "wasi:clocks/timezone@0.2.3", stub);
            });

            var got = (int)ci.Invoke("ask-offset",
                1700000000UL, 0u)!;
            Assert.Equal(-28800, got);
        }

        [Fact]
        public void Default_Timezone_returns_local_offset_for_known_datetime()
        {
            // The default Timezone uses TimeZoneInfo.Local.
            // Just sanity-check it returns a value in the
            // valid range — UTC offsets span -12h..+14h so any
            // result outside that is a conversion bug.
            var tz = new Timezone();
            var when = new Datetime
            {
                Seconds = 1700000000UL,
                Nanoseconds = 0,
            };
            var offset = tz.UtcOffset(when);
            Assert.InRange(offset, -12 * 3600, 14 * 3600);
        }
    }
}
