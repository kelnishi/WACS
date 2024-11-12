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
using Wacs.WASIp1.Types;
using ptr = System.Int32;

using timestamp = System.UInt64;

namespace Wacs.WASIp1
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

        /// <summary>
        /// Return the resolution of a clock. Implementations are required to provide a non-zero value for supported
        /// clocks. For unsupported clocks, return errno::inval. Note: This is similar to clock_getres in POSIX.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="clockId">The clock for which to return the resolution.</param>
        /// <param name="resolutionPtr"></param>
        /// <returns></returns>
        public ErrNo ClockGetRes(ExecContext ctx, ClockId clockId, ptr resolutionPtr)
        {
            if (!_config.AllowTimeAccess)
                return ErrNo.NoSys; // Function not supported
            
            var mem = ctx.DefaultMemory;
            if (!mem.Contains(resolutionPtr, 8))
                return ErrNo.Fault;
            
            switch (clockId)
            {
                case ClockId.Realtime:
                    //Typical, 1ms in ns
                    mem.WriteInt64(resolutionPtr,1_000_000ul);
                    return ErrNo.Success;
                case ClockId.Monotonic:
                    // Calculate the duration of a single tick.
                    ulong resolution = (ulong)(1_000_000_000.0 / Stopwatch.Frequency);
                    mem.WriteInt64(resolutionPtr,resolution);
                    return ErrNo.Success;
                case ClockId.ProcessCputimeId:
                    //1us in ns
                    mem.WriteInt64(resolutionPtr,1_000_000ul);
                    return ErrNo.Success;
                case ClockId.ThreadCputimeId:
                    //1us in ns
                    return ErrNo.NoSys;
                default:
                    return ErrNo.Inval;
            }
        }

        /// <summary>
        /// Retrieves the current time of the specified clock. Implementations should return the time in nanoseconds since the epoch or for specific clocks as defined in the WASI specification.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="clockId">The clock for which to return the time.</param>
        /// <param name="precision">The maximum lag (exclusive) that the returned time value may have, compared to its actual value.</param>
        /// <param name="timestampPtrPtr">Pointer where the retrieved timestamp will be stored.</param>
        /// <returns></returns>
        public ErrNo ClockTimeGet(ExecContext ctx, ClockId clockId, timestamp precision, ptr timestampPtr)
        {
            if (!_config.AllowTimeAccess)
                return ErrNo.NoSys; // Function not supported
            
            var mem = ctx.DefaultMemory;
            
            //HACK: We're ignoring precision.
            
            if (!mem.Contains(timestampPtr, 8))
                return ErrNo.Fault;
            
            timestamp timestamp;

            switch (clockId)
            {
                case ClockId.Realtime:
                    timestamp = ToTimestamp(DateTimeOffset.UtcNow);
                    break;
                case ClockId.Monotonic:
                    long ticks = Stopwatch.GetTimestamp();
                    double nanosecondsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;
                    timestamp = (ulong)(ticks * nanosecondsPerTick);
                    break;
                case ClockId.ProcessCputimeId:
                    TimeSpan cpuTime = Process.GetCurrentProcess().TotalProcessorTime;
                    timestamp = (ulong)cpuTime.TotalMilliseconds * 1_000_000; // Convert milliseconds to nanoseconds.
                    break;
                case ClockId.ThreadCputimeId:
                    return ErrNo.NoSys;
                default:
                    return ErrNo.Inval;
            }

            mem.WriteInt64(timestampPtr,timestamp);
            return ErrNo.Success;
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
            long milliseconds = (long)(atim / 1_000_000); // Convert nanoseconds to milliseconds.
            return new DateTime(milliseconds, DateTimeKind.Utc);
        }
    }
}