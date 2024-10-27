using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Wacs.Core.Runtime;
using Wacs.Core.Utilities;
using Wacs.WASIp1.Types;
using size = System.UInt32;

namespace Wacs.WASIp1
{
    public class Poll : IBindable
    {
        private static readonly int SubSize = Marshal.SizeOf<Subscription>();
        private static readonly int EvtSize = Marshal.SizeOf<Event>();
        private readonly State _state;

        public Poll(State state) => _state = state;

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext, int, int, size, int, ErrNo>>((module, "poll_oneoff"), PollOneoff);
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
        public ErrNo PollOneoff(ExecContext ctx, int inPtr, int outPtr, size nsubscriptions, int neventsPtr)
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
                    var outMem = mem[outPtr..(outPtr + EvtSize)];
                    Event vevt = evt;
                    MemoryMarshal.Write(outMem, ref vevt);
                    outPtr += EvtSize;
                }
                var neventsMem = mem[neventsPtr..(neventsPtr + 4)];
                neventsMem.WriteInt32(events.Count);
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
            var now = (ulong)DateTime.UtcNow.Ticks;

            foreach (var sub in subscriptions)
            {
                switch (sub.Type)
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

        private async Task<Event?> CreateClockTask(Subscription sub, ulong now)
        {
            var clockSub = sub.U.Clock;
            ulong targetTime;

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
            var fdSub = sub.U.FdReadWrite;
        
            
            if (!_state.FileDescriptors.TryGetValue(fdSub.Fd, out var fd))
            {
                return new Event
                {
                    UserData = sub.UserData,
                    Error = ErrNo.Badf,
                    Type = sub.Type
                };
            }

            try
            {
                if (sub.Type == EventType.FdRead)
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
                    Type = sub.Type
                };
            }
        }

        private int PeekAvailableBytes(Stream stream)
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
                return (int)(stream.Length - stream.Position);
            }
            catch
            {
                return 0;
            }
        }
    }
}