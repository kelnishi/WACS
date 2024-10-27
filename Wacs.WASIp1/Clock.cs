using System;
using System.Diagnostics;
using Wacs.Core.Runtime;
using Wacs.Core.Utilities;
using Wacs.WASIp1.Types;

namespace Wacs.WASIp1
{
    public class Clock : IBindable
    {
        public Clock() {}

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext,ClockId,int,ErrNo>>((module, "clock_res_get"), ClockGetRes);
            runtime.BindHostFunction<Func<ExecContext,ClockId,ulong,int,ErrNo>>((module, "clock_time_get"), ClockTimeGet);
        }

        /// <summary>
        /// Return the resolution of a clock. Implementations are required to provide a non-zero value for supported
        /// clocks. For unsupported clocks, return errno::inval. Note: This is similar to clock_getres in POSIX.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="clockId">The clock for which to return the resolution.</param>
        /// <param name="resolutionPtr"></param>
        /// <returns></returns>
        public static ErrNo ClockGetRes(ExecContext ctx, ClockId clockId, int resolutionPtr)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains(resolutionPtr, 8))
                return ErrNo.Fault;
            
            Span<byte> resolutionMem = mem[resolutionPtr..(resolutionPtr + 8)];
            
            switch (clockId)
            {
                case ClockId.Realtime:
                    //Typical, 1ms in ns
                    resolutionMem.WriteInt64(1_000_000ul);
                    return ErrNo.Success;
                case ClockId.Monotonic:
                    // Calculate the duration of a single tick.
                    ulong resolution = (ulong)(1_000_000_000.0 / Stopwatch.Frequency);
                    resolutionMem.WriteInt64(resolution);
                    return ErrNo.Success;
                case ClockId.ProcessCputimeId:
                    //1us in ns
                    resolutionMem.WriteInt64(1_000_000ul);
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
        /// <param name="timestampPtr">Pointer where the retrieved timestamp will be stored.</param>
        /// <returns></returns>
        public static ErrNo ClockTimeGet(ExecContext ctx, ClockId clockId, ulong precision, int timestampPtr)
        {
            var mem = ctx.DefaultMemory;
            
            //HACK: We're ignoring precision.
            
            if (!mem.Contains(timestampPtr, 8))
                return ErrNo.Fault;

            Span<byte> timestampMem = mem[timestampPtr..(timestampPtr + 8)];
            ulong timestamp;

            switch (clockId)
            {
                case ClockId.Realtime:
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    timestamp = (ulong)now.ToUnixTimeMilliseconds() * 1_000_000; // Convert milliseconds to nanoseconds.
                    timestamp += (ulong)(now.UtcDateTime.Ticks % TimeSpan.TicksPerMillisecond) * 100; // Add sub-millisecond ticks.
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

            timestampMem.WriteInt64(timestamp);
            return ErrNo.Success;
        }
    }
}