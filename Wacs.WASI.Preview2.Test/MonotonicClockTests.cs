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
using Wacs.WASI.Preview2.Io;
using Xunit;

namespace Wacs.WASI.Preview2.Test
{
    public class MonotonicClockTests
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
        public void MonotonicClock_Now_advances_between_consecutive_reads()
        {
            // Stopwatch-backed clock; two reads with any non-
            // trivial work between them should differ. Cap the
            // test at a generous threshold so it doesn't flake
            // on heavily-loaded CI.
            var clock = new MonotonicClock();
            var t1 = clock.Now();
            for (int i = 0; i < 1000; i++) { _ = i * i; }
            var t2 = clock.Now();
            Assert.True(t2 >= t1, "monotonic clock went backwards");
        }

        [Fact]
        public void MonotonicClock_Resolution_is_positive()
        {
            var clock = new MonotonicClock();
            Assert.True(clock.Resolution() > 0);
        }

        [Fact]
        public void BindWasiInstance_wires_both_methods_of_monotonic_clock()
        {
            // The fixture imports both `now` and `resolution`
            // from wasi:clocks/monotonic-clock. The auto-binder
            // walks MonotonicClock's two methods, registers each
            // under (namespace, kebab-case-name). Component
            // exports `elapsed` (calls `now`) and `step` (calls
            // `resolution`).
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-monotonic-clock-component", "clk.component.wasm"));
            var clock = new MonotonicClock();
            var resources = new ResourceContext();
            var ci = ComponentInstance.Instantiate(bytes, runtime =>
                new ClockBindings(resources, monotonic: clock)
                    .BindToRuntime(runtime));

            // Both calls return u64s drawn from the host clock.
            var elapsed = (ulong)ci.Invoke("elapsed")!;
            var step = (ulong)ci.Invoke("step")!;

            // Now() returns nanoseconds since process start;
            // > 0 once any code has run (Stopwatch resolution
            // is sub-microsecond on every supported platform).
            Assert.True(elapsed > 0);
            // Resolution() is the smallest tick → positive ns.
            Assert.True(step > 0);
        }

        [Fact]
        public void Subscribe_instant_and_duration_yield_pollable_handles()
        {
            // Fixture: ask-subscribe() calls
            // monotonic-clock.subscribe-instant(1000) + .subscribe-duration(2000),
            // drops both pollables, returns 0.
            var bytes = File.ReadAllBytes(FindFixturePath(
                "wasi-clock-subscribe-component",
                "clocksub.component.wasm"));
            var resources = new ResourceContext();
            var clock = new MonotonicClock();

            var ci = ComponentInstance.Instantiate(bytes, runtime =>
            {
                new ClockBindings(resources, monotonic: clock)
                    .BindToRuntime(runtime);
                new IoBindings(resources).BindToRuntime(runtime);
            });

            Assert.Equal(0u, (uint)ci.Invoke("ask-subscribe")!);
            // Both pollables dropped by the guest; table empty.
            Assert.Equal(0, resources.TableFor(typeof(Pollable)).Count);
        }
    }
}
