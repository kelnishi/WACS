// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using Wacs.Core.Runtime;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.HostBinding.CanonicalAbi;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Clocks
{
    /// <summary>
    /// Orchestrator for the three <c>wasi:clocks/*</c>
    /// interfaces. Constructor takes nullable host impls and a
    /// shared <see cref="ResourceContext"/> (needed by
    /// <c>monotonic-clock.subscribe-*</c> which return
    /// <see cref="Pollable"/> resource handles).
    /// </summary>
    public sealed class ClockBindings : IBindable
    {
        private readonly IMonotonicClock? _monotonic;
        private readonly IWallClock? _wall;
        private readonly ITimezone? _timezone;
        private readonly ResourceContext _resources;

        public ClockBindings(
            ResourceContext resources,
            IMonotonicClock? monotonic = null,
            IWallClock? wall = null,
            ITimezone? timezone = null)
        {
            _resources = resources
                ?? throw new ArgumentNullException(nameof(resources));
            _monotonic = monotonic;
            _wall = wall;
            _timezone = timezone;
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            if (_monotonic != null)
                BindMonotonicClock(runtime, _resources, _monotonic);
            if (_wall != null)
                BindWallClock(runtime, _wall);
            if (_timezone != null)
                BindTimezone(runtime, _timezone);
        }

        // wasi:clocks/monotonic-clock@0.2.3
        //   now: func() -> instant            (u64)
        //   resolution: func() -> duration    (u64)
        //   subscribe-instant: func(when: instant) -> own<pollable>
        //   subscribe-duration: func(when: duration) -> own<pollable>
        private static void BindMonotonicClock(WasmRuntime runtime,
            ResourceContext resources, IMonotonicClock impl)
        {
            const string ns = "wasi:clocks/monotonic-clock@0.2.3";
            var pollables = resources.Table<Pollable>();

            runtime.BindHostFunction<Func<ExecContext, long>>(
                (ns, "now"),
                _ => (long)impl.Now());

            runtime.BindHostFunction<Func<ExecContext, long>>(
                (ns, "resolution"),
                _ => (long)impl.Resolution());

            runtime.BindHostFunction<Func<ExecContext, long, int>>(
                (ns, "subscribe-instant"),
                (_, when) =>
                    pollables.Allocate(impl.SubscribeInstant((ulong)when)));

            runtime.BindHostFunction<Func<ExecContext, long, int>>(
                (ns, "subscribe-duration"),
                (_, when) =>
                    pollables.Allocate(impl.SubscribeDuration((ulong)when)));
        }

        // wasi:clocks/wall-clock@0.2.3
        //   record datetime { seconds: u64, nanoseconds: u32 }
        //   now: func() -> datetime           (retArea 16B, align 8)
        //   resolution: func() -> datetime    (same)
        //
        // Layout: u64 at retArea+0, u32 at retArea+8, 4B tail
        // padding (record size is align'd to 8).
        private static void BindWallClock(WasmRuntime runtime,
            IWallClock impl)
        {
            const string ns = "wasi:clocks/wall-clock@0.2.3";

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "now"),
                (ctx, retArea) => WriteDatetime(ctx, retArea, impl.Now()));

            runtime.BindHostFunction<Action<ExecContext, int>>(
                (ns, "resolution"),
                (ctx, retArea) =>
                    WriteDatetime(ctx, retArea, impl.Resolution()));
        }

        // wasi:clocks/timezone@0.2.3
        //   utc-offset: func(when: datetime) -> s32
        //
        // datetime param flat-lowers to two slots (u64 + u32).
        // Wire: (param i64 i32) -> (result i32).
        private static void BindTimezone(WasmRuntime runtime,
            ITimezone impl)
        {
            const string ns = "wasi:clocks/timezone@0.2.3";

            runtime.BindHostFunction<Func<ExecContext, long, int, int>>(
                (ns, "utc-offset"),
                (_, seconds, nanoseconds) => impl.UtcOffset(new Datetime
                {
                    Seconds = (ulong)seconds,
                    Nanoseconds = (uint)nanoseconds,
                }));
        }

        private static void WriteDatetime(ExecContext ctx,
            int retArea, Datetime dt)
        {
            var mem = ctx.Memory();
            MemoryWriter.WriteU64LE(mem, retArea, dt.Seconds);
            MemoryWriter.WriteU32LE(mem, retArea + 8, dt.Nanoseconds);
            // Tail padding bytes (12..15) — leave zeroed; the
            // guest's cabi_realloc returns zeroed memory.
        }
    }
}
