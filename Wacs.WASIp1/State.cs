using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Wacs.WASIp1.Types;

namespace Wacs.WASIp1
{
    public class State
    {
        public readonly CancellationTokenSource Cts = new();
        public Signal ExitCode { get; set; }
        public Signal LastSignal { get; set; }
        public List<string> Arguments { get; set; } = new();
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
        public ConcurrentDictionary<uint, FileDescriptor> FileDescriptors { get; set; } = new();
    }
}