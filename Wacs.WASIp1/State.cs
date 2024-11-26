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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using Wacs.WASIp1.Types;

namespace Wacs.WASIp1
{
    public class State
    {
        public readonly CancellationTokenSource Cts = new();

        private uint nextFd = 0;
        public int ExitCode { get; set; }
        public int LastSignal { get; set; }

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
        
        
        public Dictionary<int, Socket> socketTable = new Dictionary<int, Socket>();
        public int nextSocketDescriptor = 1;
    }
}