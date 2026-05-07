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

using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using Wacs.WASI.Preview1.Types;

namespace Wacs.WASI.Preview1
{
    public class WasiConfiguration
    {
        public const long DefaultMaxFileSize = 10 * 1024 * 1024; //10MB
        public const int DefaultOperationSize = 1 * 1024 * 1024; //1MB
        public const int DefaultMaxOpenFiles = 128;

        public string HostRootDirectory { get; set; } = string.Empty;

        public List<PreopenedDirectory> PreopenedDirectories { get; set; } = new();

        public FileAccess DefaultPermissions { get; set; } = FileAccess.Read;

        public int MaxOpenFileDescriptors { get; set; } = DefaultMaxOpenFiles;
        public long MaxFileSize { get; set; } = DefaultMaxFileSize;
        public int MaxReadWriteOperationSize { get; set; } = DefaultOperationSize;

        public bool AllowSymbolicLinks { get; set; } = false;
        public bool AllowHardLinks { get; set; } = false;

        public bool AllowFileCreation { get; set; } = false;

        public bool AllowFileDeletion { get; set; } = false;

        public Stream StandardInput { get; set; } = Stream.Null;
        public Stream StandardOutput { get; set; } = Stream.Null;
        public Stream StandardError { get; set; } = Stream.Null;

        public List<string> Arguments { get; set; } = new();
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

        public bool AllowTimeAccess { get; set; } = true;

        // Whether to auto-bind HostRootDirectory as a preopen at fd 3.
        // Kept true by default for backwards compatibility with the
        // existing Wacs.Console flow (which always exposed cwd as fd 3),
        // but the wasi-testsuite expects fd 3 to be unbound when no
        // dirs are preopened — so the harness sets this false.
        public bool PreopenHostRootDirectory { get; set; } = true;

        // Network capability gate. Default off; flipping it on alone
        // does nothing — the embedder must also hand WACS pre-bound,
        // pre-listening sockets via PreopenedSockets. Two layers of
        // explicit consent before any guest can do network IO.
        public bool AllowNetworkSockets { get; set; } = false;

        // Listening sockets handed in by the embedder. Each one
        // becomes a preopened fd (alongside dirs); guests use
        // sock_accept on it to mint connection fds.
        public List<(Socket Socket, FdFlags Flags)> PreopenedSockets { get; set; }
            = new();
    }
}