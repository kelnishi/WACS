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

namespace Wacs.WASIp1.Types
{
    public class FileDescriptor : IDisposable
    {
        public static readonly FileDescriptor BadFd = new() { Fd = uint.MaxValue };

        public uint Fd { get; set; }
        public Stream Stream { get; set; } = Stream.Null;

        //Guest Path
        public string Path { get; set; } = String.Empty;

        public Filetype Type { get; set; }

        public FileAccess Access { get; set; }
        public bool IsPreopened { get; set; }

        public Rights Rights { get; set; }

        public Rights InheritedRights { get; set; }

        /// <summary>
        /// Computes the WASI <see cref="Rights"/> for a directory.
        /// </summary>
        public static Rights ComputeFileRights(
            DirectoryInfo dirInfo,
            Filetype type,
            FileAccess access,
            Stream stream,
            bool allowFileCreation,
            bool allowFileDeletion)
        {
            // Start by setting rights based on requested FileAccess and Filetype
            Rights rights = ComputeCommonRights(type, access, stream, allowFileCreation, allowFileDeletion);

            // If directory is marked ReadOnly (especially on Windows),
            // remove FD_WRITE because we can't write or delete in that directory.
            if (dirInfo.Attributes.HasFlag(FileAttributes.ReadOnly) &&
                (access & FileAccess.Write) != 0)
            {
                rights &= ~Rights.FD_WRITE;
                // Also remove creation/deletion rights if the directory is read-only
                rights &= ~Rights.PATH_CREATE_DIRECTORY;
                rights &= ~Rights.PATH_CREATE_FILE;
                rights &= ~Rights.PATH_REMOVE_DIRECTORY;
                rights &= ~Rights.PATH_UNLINK_FILE;
            }

            return rights;
        }

        /// <summary>
        /// Computes the WASI <see cref="Rights"/> for a regular file.
        /// </summary>
        public static Rights ComputeFileRights(
            FileInfo fileInfo,
            Filetype type,
            FileAccess access,
            Stream stream,
            bool allowFileCreation,
            bool allowFileDeletion)
        {
            // Start by setting rights based on requested FileAccess and Filetype
            Rights rights = ComputeCommonRights(type, access, stream, allowFileCreation, allowFileDeletion);

            // If file is read-only (and the user is requesting write access), strip out FD_WRITE
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly) &&
                (access & FileAccess.Write) != 0)
            {
                rights &= ~Rights.FD_WRITE;
            }

            return rights;
        }



        /// <summary>
        /// Shared logic that sets basic rights flags depending on Filetype,
        /// FileAccess, and other parameters like allowFileCreation/Deletion.
        /// Used by both file and directory overloads.
        /// </summary>
        private static Rights ComputeCommonRights(
            Filetype type,
            FileAccess access,
            Stream stream,
            bool allowFileCreation,
            bool allowFileDeletion)
        {
            Rights rights = new();

            // From requested FileAccess
            if ((access & FileAccess.Read) != 0)
            {
                rights |= Rights.FD_READ;
                rights |= Rights.PATH_READLINK;       // e.g., reading symlinks
                rights |= Rights.FD_FILESTAT_GET;
                if (type == Filetype.Directory)
                    rights |= Rights.FD_READDIR;
            }
            if ((access & FileAccess.Write) != 0)
            {
                rights |= Rights.FD_WRITE;
                rights |= Rights.FD_FILESTAT_SET_SIZE;
            }
            // If explicitly ReadWrite, set both read + write bits
            // Note: (access & FileAccess.ReadWrite) means bitwise combination is present.
            if ((access & FileAccess.ReadWrite) == FileAccess.ReadWrite)
            {
                rights |= Rights.FD_READ;
                rights |= Rights.FD_WRITE;
                rights |= Rights.FD_SEEK;
                rights |= Rights.FD_TELL;
            }

            // If the underlying stream can read or write or seek, set those rights
            if (stream is FileStream fs)
            {
                if (fs.CanRead) rights |= Rights.FD_READ;
                if (fs.CanWrite) rights |= Rights.FD_WRITE;
                if (fs.CanSeek)
                {
                    rights |= Rights.FD_SEEK;
                    rights |= Rights.FD_TELL;
                }
            }

            // Directory-specific rights
            if (type == Filetype.Directory)
            {
                // If the user config says they can create or delete
                if (allowFileCreation)
                {
                    rights |= Rights.PATH_CREATE_DIRECTORY | Rights.PATH_CREATE_FILE;
                }
                if (allowFileDeletion)
                {
                    rights |= Rights.PATH_REMOVE_DIRECTORY | Rights.PATH_UNLINK_FILE;
                }

                rights |= Rights.PATH_OPEN;     // Opening files within the directory
                rights |= Rights.FD_READDIR;    // Reading directory entries
            }

            // Optionally always allow data syncing
            rights |= Rights.FD_SYNC;
            rights |= Rights.FD_DATASYNC;

            return rights;
        }

        public void Dispose()
        {
            Stream.Dispose();

        }
    }
}
