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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.WASIp1.Types;
using ptr = System.Int32;
using size = System.UInt32;
using timestamp = System.Int64;

namespace Wacs.WASIp1
{
    public class Poll : IBindable
    {
        private static readonly int SubSize = Marshal.SizeOf<Subscription>();
        private readonly State _state;

        public Poll(State state) => _state = state;

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext, ptr, ptr, size, ptr, ErrNo>>((module, "poll_oneoff"), PollOneoff);
        }

        /// <summary>
        /// Concurrently poll for the occurrence of a set of events.
        ///
        /// If nsubscriptions is 0, returns errno::inval.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="inPtr"></param>
        /// <param name="outPtr"></param>
        /// <param name="nsubscriptions"></param>
        /// <param name="neventsPtr"></param>
        /// <returns></returns>
        public ErrNo PollOneoff(ExecContext ctx, ptr inPtr, ptr outPtr, size nsubscriptions, ptr neventsPtr)
        {
            if (nsubscriptions == 0)
                return ErrNo.Inval; // Invalid argument.
            
            var mem = ctx.DefaultMemory;
            
            Subscription[] subs = new Subscription[nsubscriptions];
            for (int i = 0; i < nsubscriptions; ++i)
            {
                var inMem = mem[inPtr..(inPtr + SubSize)];
                subs[i] = MemoryMarshal.Read<Subscription>(inMem);
                inPtr += SubSize;
            }
            
            // Run the async polling operation synchronously
            try
            {
                var events = new List<Event>();
                ErrNo result = PollAsync(subs.ToList(), events).GetAwaiter().GetResult();

                if (result != ErrNo.Success)
                    return result;
                
                //Write the events back to memory
                foreach (var evt in events)
                {
                    Event vevt = evt;
                    int size = mem.WriteStruct(outPtr, ref vevt);
                    outPtr += size;
                }
                mem.WriteInt32(neventsPtr, events.Count);
            }
            catch (Exception)
            {
                return ErrNo.Inval;
            }
            
            return ErrNo.Success;
        }

        private async Task<ErrNo> PollAsync(List<Subscription> subscriptions, List<Event> events)
        {
            // Create tasks for all subscriptions
            var tasks = new List<Task<Event?>>();

            foreach (var sub in subscriptions)
            {
                switch (sub.Union.Tag)
                {
                    case EventType.Clock:
                        tasks.Add(CreateClockTask(sub));
                        break;

                    case EventType.FdRead:
                    case EventType.FdWrite:
                        tasks.Add(CreateFdTask(sub));
                        break;
                }
            }

            // Wait for any task to complete
            while (tasks.Count > 0 && events.Count < subscriptions.Count)
            {
                var completed = await Task.WhenAny(tasks);
                tasks.Remove(completed);

                var result = await completed;
                if (result.HasValue)
                {
                    events.Add(result.Value);
                }
            }

            return 0;
        }

        private async Task<Event?> CreateClockTask(Subscription sub)
        {
            // Spec: subscription_clock.timeout is in nanoseconds, relative
            // to (or absolute against) the subscription's clock_id. The
            // previous implementation mixed .NET's 100ns DateTime ticks
            // with the guest's nanoseconds, which broke absolute timeouts
            // outright and ignored clock_id entirely.
            var clockSub = sub.Union.Clock;
            long nowNs = NowNanos(clockSub.Id);
            long delayNs = (clockSub.Flags & SubclockFlags.SubscriptionClockAbstime) != 0
                ? Math.Max(0, clockSub.Timeout - nowNs)
                : Math.Max(0, clockSub.Timeout);

            // Task.Delay's resolution is ms; we round up so a sub-ms
            // request still waits long enough rather than spinning.
            int delayMs = (int)Math.Min(int.MaxValue, (delayNs + 999_999L) / 1_000_000L);

            try
            {
                if (delayMs > 0)
                    await Task.Delay(delayMs, _state.Cts.Token);

                return new Event
                {
                    UserData = sub.UserData,
                    Error = 0,
                    Type = EventType.Clock
                };
            }
            catch (TaskCanceledException)
            {
                return null;
            }
        }

        private static long NowNanos(ClockId id)
        {
            switch (id)
            {
                case ClockId.Realtime:
                    // Unix epoch nanoseconds. ToUnixTimeMilliseconds is
                    // millisecond-precision; tack on the sub-ms remainder
                    // from .NET ticks (100ns each).
                    var now = DateTimeOffset.UtcNow;
                    long ms = now.ToUnixTimeMilliseconds();
                    long subMsTicks = now.UtcDateTime.Ticks % TimeSpan.TicksPerMillisecond;
                    return ms * 1_000_000L + subMsTicks * 100L;
                case ClockId.Monotonic:
                    return Stopwatch.GetTimestamp() * (1_000_000_000L / Stopwatch.Frequency);
                case ClockId.ProcessCputimeId:
                case ClockId.ThreadCputimeId:
                    // Best-effort: process CPU time. Per-thread CPU time
                    // isn't trivially available cross-platform; fold it
                    // into the same source for now.
                    return Process.GetCurrentProcess().TotalProcessorTime.Ticks * 100L;
                default:
                    return 0;
            }
        }

        private async Task<Event?> CreateFdTask(Subscription sub)
        {
            var fdSub = sub.Union.FdReadWrite;
        
            
            if (!_state.FileDescriptors.TryGetValue(fdSub.Fd, out var fd))
            {
                return new Event
                {
                    UserData = sub.UserData,
                    Error = ErrNo.Badf,
                    Type = sub.Union.Tag
                };
            }

            try
            {
                if (sub.Union.Tag == EventType.FdRead)
                {
                    // // For read readiness, create a buffer and try to peek
                    // var buffer = new byte[1];
                    var bytesAvailable = PeekAvailableBytes(fd.Stream);

                    if (bytesAvailable > 0)
                    {
                        return new Event
                        {
                            UserData = sub.UserData,
                            Error = 0,
                            Type = EventType.FdRead,
                            FdReadWrite = new EventFdReadWrite { NBytes = (ulong)bytesAvailable },
                        };
                    }
                }
                else // FdWrite
                {
                    // For ordinary writable streams, "ready" means the
                    // stream accepts writes — there's no equivalent of a
                    // socket send-buffer high-water mark. The earlier
                    // Position<Length test was inverted: it returned ready
                    // only when the file was *shorter* than the position
                    // (i.e. effectively never).
                    if (fd.Stream.CanWrite)
                    {
                        return new Event
                        {
                            UserData = sub.UserData,
                            Error = 0,
                            Type = EventType.FdWrite,
                            FdReadWrite = new EventFdReadWrite { NBytes = ulong.MaxValue }
                        };
                    }
                }

                // If we get here, the fd isn't ready yet. We'll poll again after a short delay
                await Task.Delay(10);
                return null;
            }
            catch (Exception)
            {
                return new Event
                {
                    UserData = sub.UserData,
                    Error = ErrNo.IO,
                    Type = sub.Union.Tag
                };
            }
        }

        private static long PeekAvailableBytes(Stream stream)
        {
            if (!stream.CanRead)
                return 0;

            // For NetworkStream or similar where Length isn't supported
            if (stream is NetworkStream networkStream)
            {
                try
                {
                    return networkStream.DataAvailable ? 1 : 0;
                }
                catch
                {
                    return 0;
                }
            }

            // For regular streams where we can check position and length
            try
            {
                return stream.Length - stream.Position;
            }
            catch
            {
                return 0;
            }
        }
    }
}