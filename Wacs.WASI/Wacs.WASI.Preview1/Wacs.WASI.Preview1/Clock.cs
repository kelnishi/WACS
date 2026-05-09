// /*
//  * Copyright 2024 Kelvin Nishikawa
//  *
//  * Licensed under the Apache License, Version 2.0 (the "License");
//  * you may not use this file except in compliance with the License.
//  * You may obtain a copy of the License at
//  *
//  *     http://www.apache.org/licenses/LICENSE-2.0
//  *
//  * Unless required by applicable law or agreed to in writing, software
//  * distributed under the License is distributed on an "AS IS" BASIS,
//  * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  * See the License for the specific language governing permissions and
//  * limitations under the License.
//  */

using System;
using System.Diagnostics;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.HostBindings;
using Wacs.WASI.Preview1.Types;
using ptr = System.Int32;

using timestamp = System.Int64;

namespace Wacs.WASI.Preview1
{
    public class Clock : IBindable
    {
        private readonly WasiConfiguration _config;
        public Clock(WasiConfiguration config) => _config = config;

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext,ClockId,ptr,ErrNo>>((module, "clock_res_get"), ClockGetRes);
            runtime.BindHostFunction<Func<ExecContext,ClockId,timestamp,ptr,ErrNo>>((module, "clock_time_get"), ClockTimeGet);
        }

        // The interpreter binds these instance methods through IBindable; they
        // pull memory off ExecContext and forward to the AOT-friendly statics
        // below. Source-gen for the AOT path discovers the statics directly via
        // [WacsImport] and never sees ExecContext.

        /// <summary>
        /// Return the resolution of a clock. Implementations are required to provide a non-zero value for supported
        /// clocks. For unsupported clocks, return errno::inval. Note: This is similar to clock_getres in POSIX.
        /// </summary>
        public ErrNo ClockGetRes(ExecContext ctx, ClockId clockId, ptr resolutionPtr)
            => ClockResGetCore(WacsHost(ctx), _config, clockId, resolutionPtr);

        /// <summary>
        /// Retrieves the current time of the specified clock. Implementations
        /// should return the time in nanoseconds since the epoch or for
        /// specific clocks as defined in the WASI specification.
        /// </summary>
        public ErrNo ClockTimeGet(ExecContext ctx, ClockId clockId, timestamp precision, ptr timestampPtr)
            => ClockTimeGetCore(WacsHost(ctx), _config, clockId, precision, timestampPtr);

        // ============================================================
        // AOT-friendly static entry points. Discovered by
        // WACS.HostBindings.SourceGen via [WacsImport] and called
        // directly from the generated GeneratedHostImports adapter.
        // ============================================================

        [WacsImport("wasi_snapshot_preview1", "clock_res_get")]
        public static ErrNo ClockResGetCore(WacsHostMemory mem, WasiConfiguration config,
                                             ClockId clockId, ptr resolutionPtr)
        {
            if (!config.AllowTimeAccess)
                return ErrNo.NoSys;
            if (!mem.Contains(resolutionPtr, 8))
                return ErrNo.Fault;

            switch (clockId)
            {
                case ClockId.Realtime:
                    // Typical, 1ms in ns
                    mem.WriteInt64LE(resolutionPtr, 1_000_000L);
                    return ErrNo.Success;
                case ClockId.Monotonic:
                    long resolution = 1_000_000_000L / Stopwatch.Frequency;
                    mem.WriteInt64LE(resolutionPtr, resolution);
                    return ErrNo.Success;
                case ClockId.ProcessCputimeId:
                    // 100ns
                    mem.WriteInt64LE(resolutionPtr, 1L);
                    return ErrNo.Success;
                case ClockId.ThreadCputimeId:
                    return ErrNo.NoSys;
                default:
                    return ErrNo.Inval;
            }
        }

        [WacsImport("wasi_snapshot_preview1", "clock_time_get")]
        public static ErrNo ClockTimeGetCore(WacsHostMemory mem, WasiConfiguration config,
                                              ClockId clockId, timestamp precision, ptr timestampPtr)
        {
            if (!config.AllowTimeAccess)
                return ErrNo.NoSys;

            // HACK: precision is ignored.

            if (!mem.Contains(timestampPtr, 8))
                return ErrNo.Fault;

            timestamp timestamp;
            switch (clockId)
            {
                case ClockId.Realtime:
                    timestamp = ToTimestamp(DateTimeOffset.UtcNow);
                    break;
                case ClockId.Monotonic:
                {
                    long ticks = Stopwatch.GetTimestamp();
                    long nanosecondsPerTick = 1_000_000_000L / Stopwatch.Frequency;
                    timestamp = (ticks * nanosecondsPerTick);
                } break;
                case ClockId.ProcessCputimeId:
                {
                    TimeSpan cpuTime = Process.GetCurrentProcess().TotalProcessorTime;
                    timestamp = cpuTime.Ticks * 1_000_000L / TimeSpan.TicksPerMillisecond; // ns
                } break;
                case ClockId.ThreadCputimeId:
                    return ErrNo.NoSys;
                default:
                    return ErrNo.Inval;
            }

            mem.WriteInt64LE(timestampPtr, timestamp);
            return ErrNo.Success;
        }

        // ============================================================
        // Helpers
        // ============================================================

        // Wrap a runtime ExecContext's default memory in WacsHostMemory.
        // The ExecContext path is interpreter-side only; AOT-side
        // callers construct WacsHostMemory directly from their host's
        // byte-buffer view.
        // Gap 12 phase C.4: dispatch by storage mode — NativePointer
        // memories use the IntPtr ctor so the binding sees native
        // bytes; ManagedArray uses the historical byte[] ctor.
        internal static unsafe WacsHostMemory WacsHost(ExecContext ctx)
        {
            var m = ctx.DefaultMemory;
            if (m.StorageMode == Wacs.Core.Runtime.MemoryStorageMode.NativePointer)
            {
                int len = (int)System.Math.Min(
                    (ulong)m.NativeSize, (ulong)int.MaxValue);
                return new WacsHostMemory((System.IntPtr)m.NativeBase, len);
            }
            return new WacsHostMemory(m.Data, m.Data.Length);
        }

        public static timestamp ToTimestamp(DateTime time) => ToTimestamp(new DateTimeOffset(time));

        public static timestamp ToTimestamp(DateTimeOffset time)
        {
            timestamp timestamp = (timestamp)time.ToUnixTimeMilliseconds() * 1_000_000; // Convert milliseconds to nanoseconds.
            timestamp += (timestamp)(time.UtcDateTime.Ticks % TimeSpan.TicksPerMillisecond) * 100; // Add sub-millisecond ticks.
            return timestamp;
        }

        public static DateTime ToDateTimeUtc(timestamp atim)
        {
            // WASI timestamps are nanoseconds since the Unix epoch.
            // DateTime tick is 100ns, so atim/100 = ticks-from-epoch
            // (added onto the Unix epoch to land in the right century).
            // Previous code mistakenly used `atim/1_000_000` as DateTime
            // *ticks* — produced a year-0001 timestamp that
            // File.SetLastWriteTimeUtc rounded to whatever the underlying
            // filesystem allowed, breaking fd_filestat_set's mtim check.
            long ticks = (long)(atim / 100);
            return DateTime.UnixEpoch.AddTicks(ticks);
        }
    }
}