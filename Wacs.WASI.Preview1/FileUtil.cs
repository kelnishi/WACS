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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Wacs.WASI.Preview1.Types;

namespace Wacs.WASI.Preview1
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

            // On Unix, prefer the real st_ino so hardlinks share the same
            // inode (rust/path_link asserts this). The path-hash fallback
            // is fine on Windows where hardlinks are rare and st_ino isn't
            // a clean concept anyway.
            if (fileInfo.Exists && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (TryGetUnixInode(fileInfo.FullName, out var ino)) return ino;
            }

            using (var sha256 = SHA256.Create())
            {
                long length = fileInfo.Exists ? fileInfo.Length : 0L;
                string data = $"{fileInfo.FullName}|{fileInfo.CreationTimeUtc.Ticks}|{length}";
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                ulong inode = BitConverter.ToUInt64(hashBytes, 0);
                return inode;
            }
        }

        /// <summary>
        /// Probe the real Unix st_ino via stat(2). Returns true on success.
        /// We round-trip through `Mono.Unix.Native` would be ideal but isn't
        /// in our dep set; use libc::stat directly. Two structs because
        /// macOS and Linux disagree on layout.
        /// </summary>
        private static bool TryGetUnixInode(string path, out ulong inode)
        {
            inode = 0;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var s = default(StatMacOs);
                    if (StatMacOsX64(path, ref s) == 0) { inode = s.st_ino; return true; }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var s = default(StatLinux);
                    if (StatLinuxX64(path, ref s) == 0) { inode = s.st_ino; return true; }
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return false;
        }

        // macOS stat layout (with INODE64 default since 10.5).
        // Only st_ino is consumed — leave the rest as opaque padding.
        [StructLayout(LayoutKind.Sequential)]
        private struct StatMacOs
        {
            public uint   st_dev;
            public ushort st_mode;
            public ushort st_nlink;
            public ulong  st_ino;
            public uint   st_uid;
            public uint   st_gid;
            public uint   st_rdev;
            public long st_atime, st_atime_nsec;
            public long st_mtime, st_mtime_nsec;
            public long st_ctime, st_ctime_nsec;
            public long st_btime, st_btime_nsec;
            public long   st_size;
            public long   st_blocks;
            public int    st_blksize;
            public uint   st_flags;
            public uint   st_gen;
            public int    st_lspare;
            public long   st_qspare0, st_qspare1;
        }

        // Linux x86_64 stat layout (kernel struct stat as exposed via libc).
        [StructLayout(LayoutKind.Sequential)]
        private struct StatLinux
        {
            public ulong st_dev;
            public ulong st_ino;
            public ulong st_nlink;
            public uint  st_mode;
            public uint  st_uid;
            public uint  st_gid;
            public int   __pad0;
            public ulong st_rdev;
            public long  st_size;
            public long  st_blksize;
            public long  st_blocks;
            public long st_atime, st_atime_nsec;
            public long st_mtime, st_mtime_nsec;
            public long st_ctime, st_ctime_nsec;
            public long __unused0, __unused1, __unused2;
        }

        // libSystem.dylib on macOS exposes stat$INODE64 by default for
        // 64-bit binaries; the modern libc symbol is just `stat`.
        [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
        private static extern int StatMacOsX64(string path, ref StatMacOs buf);

        [DllImport("libc", EntryPoint = "__xstat", SetLastError = true)]
        private static extern int LinuxXstat(int ver, string path, ref StatLinux buf);

        // Modern glibc exports stat directly (vs the legacy __xstat shim);
        // try the direct symbol first via libc fallthrough.
        private static int StatLinuxX64(string path, ref StatLinux buf)
        {
            try { return StatLinuxDirect(path, ref buf); }
            catch (EntryPointNotFoundException) { return LinuxXstat(1, path, ref buf); }
        }

        [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
        private static extern int StatLinuxDirect(string path, ref StatLinux buf);

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
