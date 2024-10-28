using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Wacs.Core.Runtime;
using Wacs.WASIp1.Types;
using ptr = System.Int32;
using size = System.UInt32;

using timestamp = System.UInt64;

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
            var now = (timestamp)DateTime.UtcNow.Ticks;

            foreach (var sub in subscriptions)
            {
                switch (sub.Union.Tag)
                {
                    case EventType.Clock:
                        tasks.Add(CreateClockTask(sub, now));
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

        private async Task<Event?> CreateClockTask(Subscription sub, timestamp now)
        {
            var clockSub = sub.Union.Clock;
            timestamp targetTime;

            if ((clockSub.Flags & SubclockFlags.SubscriptionClockAbstime) != 0)
            {
                targetTime = clockSub.Timeout;
            }
            else
            {
                targetTime = now + clockSub.Timeout;
            }

            var delayTicks = Math.Max(0, targetTime - now);
            var delayMs = delayTicks / TimeSpan.TicksPerMillisecond;

            try
            {
                await Task.Delay((int)delayMs, _state.Cts.Token);
            
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
                    // For write readiness, check if we're at the end of the stream
                    if (fd.Stream.CanWrite && fd.Stream.Position < fd.Stream.Length)
                    {
                        return new Event
                        {
                            UserData = sub.UserData,
                            Error = 0,
                            Type = EventType.FdWrite,
                            FdReadWrite = new EventFdReadWrite { NBytes = (ulong)(fd.Stream.Length - fd.Stream.Position) }
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