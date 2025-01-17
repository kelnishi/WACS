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
using System.Security.Cryptography;
using System.Text;
using Wacs.WASIp1.Types;

namespace Wacs.WASIp1
{
    /// <summary>
    /// Utility class for working with files and directories in a WASI-like environment.
    /// </summary>
    public static class FileUtil
    {
        /// <summary>
        /// Generates a pseudo-inode for a directory by hashing its
        /// full path and creation time (in UTC). Directories do not
        /// have a meaningful length, so we omit that.
        /// </summary>
        /// <param name="dirInfo">The DirectoryInfo describing the directory.</param>
        /// <returns>A 64-bit hash that serves as a pseudo-inode.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="dirInfo"/> is null.</exception>
        public static ulong GenerateInode(DirectoryInfo dirInfo)
        {
            if (dirInfo == null)
                throw new ArgumentNullException(nameof(dirInfo));

            using (var sha256 = SHA256.Create())
            {
                // Combine directory attributes (FullName + CreationTimeUtc)
                string data = $"{dirInfo.FullName}|{dirInfo.CreationTimeUtc.Ticks}";
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                // Use the first 8 bytes of the hash as the pseudo-inode
                ulong inode = BitConverter.ToUInt64(hashBytes, 0);
                return inode;
            }
        }

        /// <summary>
        /// Generates a pseudo-inode for a file by hashing its
        /// full path, creation time (in UTC), and length (if it exists).
        /// </summary>
        /// <param name="fileInfo">The FileInfo describing the file.</param>
        /// <returns>A 64-bit hash that serves as a pseudo-inode.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="fileInfo"/> is null.</exception>
        public static ulong GenerateInode(FileInfo fileInfo)
        {
            if (fileInfo == null)
                throw new ArgumentNullException(nameof(fileInfo));

            using (var sha256 = SHA256.Create())
            {
                // Include file length only if it actually exists on disk
                long length = fileInfo.Exists ? fileInfo.Length : 0L;
                string data = $"{fileInfo.FullName}|{fileInfo.CreationTimeUtc.Ticks}|{length}";
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                // Use the first 8 bytes of the hash as the pseudo-inode
                ulong inode = BitConverter.ToUInt64(hashBytes, 0);
                return inode;
            }
        }

        /// <summary>
        /// Determines the WASI file type from a <see cref="FileInfo"/> object.
        /// This checks attributes such as Directory, ReparsePoint (symlinks), etc.
        /// </summary>
        /// <param name="info">The FileInfo object to examine.</param>
        /// <returns>A <see cref="Filetype"/> enum that best describes the item.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="info"/> is null.</exception>
        public static Filetype FiletypeFromInfo(FileInfo info)
        {
            if (info == null)
                throw new ArgumentNullException(nameof(info));

            // If it doesn't exist at all
            if (!info.Exists)
            {
                return Filetype.Unknown;
            }

            // If it's actually a directory
            if ((info.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                return Filetype.Directory;
            }

            // If it's a reparse point (commonly a symbolic link on Windows)
            if ((info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                return Filetype.SymbolicLink;
            }

            // If it's flagged as a device
            if ((info.Attributes & FileAttributes.Device) == FileAttributes.Device)
            {
                // Typically we’d need additional logic to differentiate Block vs. Character device
                return Filetype.CharacterDevice;
            }

            // If it has a ".sock" extension, we consider it a socket file
            if (info.Extension.Equals(".sock", StringComparison.OrdinalIgnoreCase))
            {
                return Filetype.SocketStream;
            }

            // Default: it's a regular file
            return Filetype.RegularFile;
        }
    }
}
