// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.Core.Types;
using Wacs.Core.Types.Defs;
using Wacs.WASI.Preview3.Clocks;
using Xunit;

namespace Wacs.WASI.Preview3.Test
{
    /// <summary>
    /// Phase 5 Slice A coverage:
    /// <c>wasi:clocks/monotonic-clock@0.3.0-rc-2026-03-15</c> and
    /// <c>wasi:clocks/system-clock@0.3.0-rc-2026-03-15</c>.
    /// </summary>
    public class ClocksTests
    {
        private static MemoryInstance MakeMemory()
        {
            var memType = new MemoryType(new Limits(AddrType.I32, 1, 1));
            return new MemoryInstance(memType);
        }

        // ---- MonotonicClock ------------------------------------------

        [Fact]
        public void MonotonicClock_now_is_non_decreasing()
        {
            var clock = new MonotonicClock();
            var n1 = clock.Now();
            var n2 = clock.Now();
            Assert.True(n2 >= n1,
                $"Now() must be non-decreasing; n1={n1}, n2={n2}.");
        }

        [Fact]
        public void MonotonicClock_resolution_is_positive()
        {
            // Resolution is `1_000_000_000 / Stopwatch.Frequency`,
            // which is 1ns on Stopwatch.IsHighResolution platforms
            // and at most ~100ns on low-res ones. Must be >0.
            var clock = new MonotonicClock();
            Assert.True(clock.GetResolution() > 0);
        }

        [Fact]
        public async Task MonotonicClock_wait_for_completes_after_requested_duration()
        {
            // wait-for(N ns) must take AT LEAST N ns wall-clock to
            // complete, per the spec's "at least" semantics. We
            // use 30ms to give the timer resolution comfortable
            // headroom on slow CI.
            var clock = new MonotonicClock();
            var sw = Stopwatch.StartNew();
            await clock.WaitForAsync(30_000_000UL); // 30ms
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds >= 25,
                $"WaitForAsync(30ms) returned in {sw.ElapsedMilliseconds}ms.");
        }

        [Fact]
        public async Task MonotonicClock_wait_for_zero_completes_immediately()
        {
            var clock = new MonotonicClock();
            await clock.WaitForAsync(0);
            // No assertion — just confirming it returns without
            // hanging or throwing.
        }

        [Fact]
        public async Task MonotonicClock_wait_until_past_mark_completes_immediately()
        {
            var clock = new MonotonicClock();
            // A mark in the past relative to the clock's epoch =
            // mark=0 (clock started at process start, so 0 is well
            // in the past).
            await clock.WaitUntilAsync(0);
        }

        [Fact]
        public async Task MonotonicClock_wait_for_honors_cancellation()
        {
            var clock = new MonotonicClock();
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(20);
            // Request a long wait; cancellation should fire first.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => clock.WaitForAsync(5_000_000_000UL, cts.Token));
        }

        // ---- SystemClock ---------------------------------------------

        [Fact]
        public void SystemClock_now_is_near_current_time()
        {
            var clock = new SystemClock();
            var instant = clock.Now();
            // The Unix epoch is 1970; expect ~57 years (1.8e9 s)
            // since then. Sanity: between 2026-01-01 and
            // 2030-01-01.
            const long y2026_jan1 = 1_767_225_600L;
            const long y2030_jan1 = 1_893_456_000L;
            Assert.InRange(instant.Seconds, y2026_jan1, y2030_jan1);
            Assert.True(instant.Nanoseconds < 1_000_000_000U);
        }

        [Fact]
        public void SystemClock_resolution_is_one_hundred_nanoseconds()
        {
            var clock = new SystemClock();
            // .NET DateTime tick = 100 ns.
            Assert.Equal(100UL, clock.GetResolution());
        }

        // ---- Wire-level binding via WasiPreview3Host -----------------

        [Fact]
        public void BindToRuntime_registers_clocks_host_functions()
        {
            var host = new WasiPreview3Host();
            var runtime = new WasmRuntime();
            host.BindToRuntime(runtime);

            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.MonotonicClockModuleName, "now"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.MonotonicClockModuleName, "get-resolution"),
                out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.MonotonicClockModuleName, "wait-until"),
                out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.MonotonicClockModuleName, "wait-for"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.SystemClockModuleName, "now"), out _));
            Assert.True(runtime.TryGetExportedFunction(
                (WasiPreview3Host.SystemClockModuleName, "get-resolution"),
                out _));
        }

        [Fact]
        public void InvokeSystemClockNow_writes_instant_record_to_memory()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };

            // Use a stub system clock that returns a known instant.
            var stub = new StubSystemClock(
                new Instant(seconds: 1_700_000_000L, nanoseconds: 123_456_789U));

            const int retptr = 64;
            host.InvokeSystemClockNow(stub, retptr);

            var bytes = dispatcher.Memory.AsSpan(retptr, 16);
            long sec = BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(0));
            uint ns = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8));
            Assert.Equal(1_700_000_000L, sec);
            Assert.Equal(123_456_789U, ns);
        }

        [Fact]
        public void InvokeSystemClockNow_rejects_misaligned_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            dispatcher.Memory = MakeMemory();
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var stub = new StubSystemClock(new Instant(0, 0));

            // retptr 8-alignment required; 12 is 4-aligned but
            // not 8-aligned.
            var thrown = Assert.Throws<InvalidOperationException>(
                () => host.InvokeSystemClockNow(stub, 12));
            Assert.Contains("misaligned", thrown.Message);
        }

        [Fact]
        public void InvokeSystemClockNow_rejects_out_of_range_retptr()
        {
            var dispatcher = new AsyncDispatcher();
            var memory = MakeMemory();
            dispatcher.Memory = memory;
            var host = new WasiPreview3Host { Dispatcher = dispatcher };
            var stub = new StubSystemClock(new Instant(0, 0));

            int outOfRange = memory.Data.Length - 8;
            var thrown = Assert.Throws<InvalidOperationException>(
                () => host.InvokeSystemClockNow(stub, outOfRange));
            Assert.Contains("out of range", thrown.Message);
        }

        [Fact]
        public void InvokeSystemClockNow_throws_when_dispatcher_unset()
        {
            var host = new WasiPreview3Host();
            var stub = new StubSystemClock(new Instant(0, 0));
            var thrown = Assert.Throws<InvalidOperationException>(
                () => host.InvokeSystemClockNow(stub, 0));
            Assert.Contains("Dispatcher", thrown.Message);
        }

        [Fact]
        public void Host_default_construct_provides_default_clocks()
        {
            var host = new WasiPreview3Host();
            Assert.NotNull(host.MonotonicClock);
            Assert.NotNull(host.SystemClock);
        }

        [Fact]
        public void Host_custom_clock_overrides_threaded_via_builder()
        {
            var stubMono = new StubMonotonicClock();
            var stubSys = new StubSystemClock(new Instant(1, 2));
            var host = new WasiPreview3Host(new WasiPreview3HostBuilder
            {
                MonotonicClock = stubMono,
                SystemClock = stubSys,
            });
            Assert.Same(stubMono, host.MonotonicClock);
            Assert.Same(stubSys, host.SystemClock);
        }

        // ---- Test stubs ----------------------------------------------

        private sealed class StubSystemClock : ISystemClock
        {
            private readonly Instant _now;
            public StubSystemClock(Instant now) { _now = now; }
            public Instant Now() => _now;
            public ulong GetResolution() => 1;
        }

        private sealed class StubMonotonicClock : IMonotonicClock
        {
            public ulong Now() => 0;
            public ulong GetResolution() => 1;
            public Task WaitUntilAsync(
                ulong when, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
            public Task WaitForAsync(
                ulong howLong, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}
