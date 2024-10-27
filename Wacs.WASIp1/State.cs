using System.Collections.Concurrent;
using System.Threading;
using Wacs.WASIp1.Types;

namespace Wacs.WASIp1
{
    public class State
    {
        public readonly CancellationTokenSource Cts = new();

        private uint nextFd = 0;
        public Signal ExitCode { get; set; }
        public Signal LastSignal { get; set; }

        public uint GetNextFd {
            get
            {
                uint fd = nextFd;
                nextFd += 1;
                return fd;
            }
        }

        public ConcurrentDictionary<uint, FileDescriptor> FileDescriptors { get; set; } = new();


        public VirtualPathMapper PathMapper { get; set; } = new();
    }
}