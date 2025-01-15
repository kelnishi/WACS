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
using System.IO;
using Wacs.Core.Attributes;
using Wacs.Core.Types.Defs;

namespace Wacs.WASIp1.Types
{
    [WasmType(nameof(ValType.I64))]
    [Flags]
    public enum Rights : ulong
    {
        None = 0,

        FD_DATASYNC = 1 << 0,
        FD_READ = 1 << 1,
        FD_SEEK = 1 << 2,
        FD_FDSTAT_SET_FLAGS = 1 << 3,
        FD_SYNC = 1 << 4,
        FD_TELL = 1 << 5,
        FD_WRITE = 1 << 6,
        FD_ADVISE = 1 << 7,
        FD_ALLOCATE = 1 << 8,
        PATH_CREATE_DIRECTORY = 1 << 9,
        PATH_CREATE_FILE = 1 << 10,
        PATH_LINK_SOURCE = 1 << 11,
        PATH_LINK_TARGET = 1 << 12,
        PATH_OPEN = 1 << 13,
        FD_READDIR = 1 << 14,
        PATH_READLINK = 1 << 15,
        PATH_RENAME_SOURCE = 1 << 16,
        PATH_RENAME_TARGET = 1 << 17,
        PATH_FILESTAT_GET = 1 << 18,
        PATH_FILESTAT_SET_SIZE = 1 << 19,
        PATH_FILESTAT_SET_TIMES = 1 << 20,
        FD_FILESTAT_GET = 1 << 21,
        FD_FILESTAT_SET_SIZE = 1 << 22,
        FD_FILESTAT_SET_TIMES = 1 << 23,
        PATH_SYMLINK = 1 << 24,
        PATH_REMOVE_DIRECTORY = 1 << 25,
        PATH_UNLINK_FILE = 1 << 26,
        POLL_FD_READWRITE = 1 << 27,
        SOCK_SHUTDOWN = 1 << 28,
        SOCK_ACCEPT = 1 << 29,

        All = UInt64.MaxValue,
    }

    public static class RightsExtension
    {
        /// <summary>
        /// Convert a set of WASI rights into .NET FileAccess flags.
        /// - FD_READ => FileAccess.Read
        /// - FD_WRITE => FileAccess.Write
        /// - If both => FileAccess.ReadWrite
        /// - FD_DATASYNC => here mapped to Read, purely for demonstration
        /// </summary>
        public static FileAccess ToFileAccess(this Rights rights)
        {
            bool canRead = (rights & Rights.FD_READ) != 0 || (rights & Rights.FD_DATASYNC) != 0;
            bool canWrite = (rights & Rights.FD_WRITE) != 0;

            if (canRead && canWrite) return FileAccess.ReadWrite;
            if (canRead) return FileAccess.Read;
            if (canWrite) return FileAccess.Write;
            return 0;
        }
    }

}