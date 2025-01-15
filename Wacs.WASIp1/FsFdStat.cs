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
using System.Text;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.WASIp1.Types;
using ptr = System.UInt32;
using fd = System.UInt32;
using filesize = System.UInt64;
using size = System.UInt32;
using timestamp = System.Int64;

namespace Wacs.WASIp1
{
    public partial class FileSystem
    {
        private static readonly int FdStatSize = Marshal.SizeOf<FdStat>();
        private static readonly int FileStatSize = Marshal.SizeOf<FileStat>();

        /// <summary>
        /// Get the attributes of a file descriptor.
        /// This is similar to fcntl(fd, F_GETFL) in POSIX, as well as additional fields.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor to obtain attributes for.</param>
        /// <param name="bufPtr">Pointer to the buffer where the file descriptor's attributes will be stored.</param>
        /// <returns>Returns ErrNo.Success if successful, otherwise an error code.</returns>
        public ErrNo FdFdstatGet(ExecContext ctx, fd fd, ptr bufPtr)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)bufPtr, FdStatSize))
                return ErrNo.Inval;

            if (!GetFd(fd, out var fileDescriptor))
                return ErrNo.NoEnt;

            FdFlags flags = FdFlags.None;

            var hostPath = _state.PathMapper.MapToHostPath(fileDescriptor.Path);

            FileAttributes attr;
            try
            {
                attr = File.GetAttributes(hostPath);
            }
            catch (FileNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (DirectoryNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }
            catch (IOException)
            {
                return ErrNo.IO;
            }

            // If it's a regular file with a FileStream, we can attempt to deduce some flags
            if (fileDescriptor.Type == Filetype.RegularFile &&
                fileDescriptor.Stream is FileStream fs)
            {
                // For this minimal approach, interpret "can't timeout" as NonBlock
                if (!fs.CanTimeout)
                    flags |= FdFlags.NonBlock;

                // If "Archive" attribute is set, interpret that as 'Append'
                if ((attr & FileAttributes.Archive) == FileAttributes.Archive)
                    flags |= FdFlags.Append;

                // If the file is not async, interpret as "sync"
                if (!fs.IsAsync)
                    flags |= FdFlags.Sync;
            }

            var fdStat = new FdStat
            {
                Filetype = fileDescriptor.Type,
                Flags = flags,
                RightsBase = fileDescriptor.Rights,
                RightsInheriting = fileDescriptor.InheritedRights,
            };

            mem.WriteStruct(bufPtr, ref fdStat);
            return ErrNo.Success;
        }

        /// <summary>
        /// Adjust the flags associated with a file descriptor.
        /// This is similar to fcntl(fd, F_SETFL, flags) in POSIX.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor whose flags are to be adjusted.</param>
        /// <param name="flags">The desired values of the file descriptor flags.</param>
        /// <returns>Returns ErrNo.Success if successful, otherwise an error code.</returns>
        public ErrNo FdFdstatSetFlags(ExecContext ctx, fd fd, FdFlags flags)
        {
            return ErrNo.NotSup;
        }

        /// <summary>
        /// Adjust the rights associated with a file descriptor.
        /// This can only be used to remove rights.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor to adjust rights for.</param>
        /// <param name="fs_rights_base">The desired rights of the file descriptor.</param>
        /// <param name="fs_rights_inheriting">The maximum set of rights that may be installed on new file descriptors created through this file descriptor.</param>
        /// <returns>Returns ErrNo.Success if successful, otherwise an error code.</returns>
        public ErrNo FdFdstatSetRights(ExecContext ctx, fd fd, Rights fs_rights_base, Rights fs_rights_inheriting)
        {
            return ErrNo.NotSup;
        }

        /// <summary>
        /// Return the attributes of an open file.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor of the file to inspect.</param>
        /// <param name="bufPtr">Pointer to the buffer where the file's attributes will be stored.</param>
        /// <returns>Returns ErrNo.Success if successful, otherwise an error code.</returns>
        public ErrNo FdFilestatGet(ExecContext ctx, fd fd, ptr bufPtr)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)bufPtr, FileStatSize))
                return ErrNo.Inval;

            if (!GetFd(fd, out var fileDescriptor))
                return ErrNo.NoEnt;

            // For stdio (fd < 3), we usually provide dummy stats
            if (fd < 3)
            {
                var dummyStat = new FileStat
                {
                    Device = 0,
                    Ino = fd, // treat FD number as inode
                    Mode = fileDescriptor.Type,
                    NLink = 1,
                    Size = 0,
                    ATim = 0,
                    MTim = 0,
                    CTim = 0,
                };

                mem.WriteStruct(bufPtr, ref dummyStat);
                return ErrNo.Success;
            }

            // Otherwise, check if it's a file or directory
            var hostPath = _state.PathMapper.MapToHostPath(fileDescriptor.Path);

            FileAttributes attr;
            try
            {
                attr = File.GetAttributes(hostPath);
            }
            catch (FileNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (DirectoryNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }
            catch (IOException)
            {
                return ErrNo.IO;
            }

            bool isDir = attr.HasFlag(FileAttributes.Directory);
            // If it's not a directory, we assume it's a file. In Windows, FileAttributes.Normal can indicate a standard file.
            bool isFile = !isDir;

            if (!isDir && !isFile)
                return ErrNo.NoEnt;

            if (isDir)
            {
                var dirInfo = new DirectoryInfo(hostPath);
                var fileStat = BuildFileStatForDirectory(fileDescriptor, dirInfo);
                mem.WriteStruct(bufPtr, ref fileStat);
            }
            else
            {
                var fileInfo = new FileInfo(hostPath);
                var fileStat = BuildFileStatForFile(fileDescriptor, fileInfo);
                mem.WriteStruct(bufPtr, ref fileStat);
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// Adjust the size of an open file. If this increases the file's size, the extra bytes are filled with zeros.
        /// This is similar to ftruncate in POSIX.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor of the file to resize.</param>
        /// <param name="stSize">The desired file size.</param>
        /// <returns>Returns ErrNo.Success if successful, otherwise an error code.</returns>
        public ErrNo FdFilestatSetSize(ExecContext ctx, fd fd, filesize stSize)
        {
            if (!GetFd(fd, out var fileDescriptor))
                return ErrNo.NoEnt;

            // If directory => EISDIR
            if (fileDescriptor.Type == Filetype.Directory)
                return ErrNo.IsDir;

            try
            {
                fileDescriptor.Stream.SetLength((long)stSize);
            }
            catch (NotSupportedException)
            {
                return ErrNo.Inval;
            }
            catch (IOException)
            {
                return ErrNo.IO;
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// Adjust the timestamps of an open file or directory.
        /// This is similar to futimens in POSIX.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor of the file or directory.</param>
        /// <param name="atim">The desired value of the data access timestamp.</param>
        /// <param name="mtim">The desired value of the data modification timestamp.</param>
        /// <param name="flags">A bitmask indicating which timestamps to adjust.</param>
        /// <returns>Returns ErrNo.Success if successful, otherwise an error code.</returns>
        public ErrNo FdFilestatSetTimes(ExecContext ctx, fd fd, timestamp atim, timestamp mtim, FstFlags flags)
        {
            if (!GetFd(fd, out var fileDescriptor))
                return ErrNo.NoEnt;

            var hostPath = _state.PathMapper.MapToHostPath(fileDescriptor.Path);

            FileAttributes attr;
            try
            {
                attr = File.GetAttributes(hostPath);
            }
            catch (FileNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (DirectoryNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }
            catch (IOException)
            {
                return ErrNo.IO;
            }

            bool isDir = attr.HasFlag(FileAttributes.Directory);
            bool isFile = !isDir;

            if (!isDir && !isFile)
                return ErrNo.NoEnt;

            DateTime newAtime = DateTime.UtcNow;
            DateTime newMtime = DateTime.UtcNow;

            // If user provided explicit atime
            if ((flags & FstFlags.ATim) != 0)
                newAtime = Clock.ToDateTimeUtc(atim);

            // If user said "ATimNow", override with "now"
            if ((flags & FstFlags.ATimNow) != 0)
                newAtime = DateTime.UtcNow;

            // If user provided explicit mtime
            if ((flags & FstFlags.MTim) != 0)
                newMtime = Clock.ToDateTimeUtc(mtim);

            // If user said "MTimNow", override with "now"
            if ((flags & FstFlags.MTimNow) != 0)
                newMtime = DateTime.UtcNow;

            try
            {
                if (isDir)
                {
                    Directory.SetLastWriteTimeUtc(hostPath, newMtime);
                    Directory.SetLastAccessTimeUtc(hostPath, newAtime);
                }
                else
                {
                    File.SetLastWriteTimeUtc(hostPath, newMtime);
                    File.SetLastAccessTimeUtc(hostPath, newAtime);
                }
            }
            catch (IOException)
            {
                return ErrNo.IO;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// Return a description of the given preopened file descriptor.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor of the preopened file.</param>
        /// <param name="bufPtr">Pointer to the buffer where the description will be stored.</param>
        /// <returns>Returns ErrNo.Success if successful, otherwise an error code.</returns>
        public ErrNo FdPrestatGet(ExecContext ctx, fd fd, ptr bufPtr)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)bufPtr, Marshal.SizeOf<Prestat>()))
                return ErrNo.Inval;

            if (!GetFd(fd, out var fileDescriptor))
                return ErrNo.Badf;

            var name = fileDescriptor.Path;
            var utf8Name = Encoding.UTF8.GetBytes(name);

            // Typically, for WASI, only directories are truly "preopened"
            Prestat prestat;
            if (fileDescriptor.Type == Filetype.Directory)
            {
                prestat = new Prestat
                {
                    Tag = PrestatTag.Dir,
                    Dir = new PrestatDir
                    {
                        NameLen = (uint)(utf8Name.Length + 1)
                    }
                };
            }
            else
            {
                prestat = new Prestat
                {
                    Tag = PrestatTag.NotDir,
                    Dir = new PrestatDir
                    {
                        NameLen = 0
                    }
                };
            }

            mem.WriteStruct(bufPtr, ref prestat);
            return ErrNo.Success;
        }

        /// <summary>
        /// Return the name of the directory associated with the preopened file descriptor.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor of the preopened directory.</param>
        /// <param name="pathPtr">Pointer to the buffer to write the directory name.</param>
        /// <param name="pathLen">The length of the directory name buffer.</param>
        /// <returns>Returns ErrNo.Success if successful, otherwise an error code.</returns>
        public ErrNo FdPrestatDirName(ExecContext ctx, fd fd, ptr pathPtr, size pathLen)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)pathPtr, (int)pathLen))
                return ErrNo.Inval;

            if (!GetFd(fd, out var fileDescriptor))
                return ErrNo.NoEnt;

            if (fileDescriptor.Type != Filetype.Directory)
                return ErrNo.NotDir;

            var name = fileDescriptor.Path;
            var utf8Name = Encoding.UTF8.GetBytes(name);
            if (utf8Name.Length + 1 > pathLen)
                return ErrNo.TooBig;

            mem.WriteUtf8String(pathPtr, name, true);
            return ErrNo.Success;
        }

        /// <summary>
        /// Return the attributes of a file or directory.
        /// This is similar to stat in POSIX.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor to use for resolving the path.</param>
        /// <param name="flags">Flags determining how the path is resolved.</param>
        /// <param name="pathPtr">Pointer to the path of the file or directory.</param>
        /// <param name="pathLen">The length of the path.</param>
        /// <param name="buf">Pointer to the buffer where the attributes will be stored.</param>
        /// <returns>Returns ErrNo.Success if successful, otherwise an error code.</returns>
        public ErrNo PathFilestatGet(ExecContext ctx, fd fd, LookupFlags flags, ptr pathPtr, size pathLen, ptr buf)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)buf, FileStatSize))
                return ErrNo.Inval;

            if (!mem.Contains((int)pathPtr, (int)pathLen))
                return ErrNo.Inval;

            if (!GetFd(fd, out var dirFileDescriptor))
                return ErrNo.NoEnt;

            // If dirFileDescriptor is not a directory (and not FD < 3?), we can't interpret paths from it
            if (dirFileDescriptor.Type != Filetype.Directory && fd >= 3)
                return ErrNo.NotDir;

            var pathStr = mem.ReadString(pathPtr, pathLen);
            var guestPath = Path.Combine(dirFileDescriptor.Path, pathStr);
            var hostPath = _state.PathMapper.MapToHostPath(guestPath);

            FileAttributes attr;
            try
            {
                attr = File.GetAttributes(hostPath);
            }
            catch (FileNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (DirectoryNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }
            catch (IOException)
            {
                return ErrNo.IO;
            }

            bool isDir = attr.HasFlag(FileAttributes.Directory);
            bool isFile = !isDir;

            if (!isDir && !isFile)
                return ErrNo.NoEnt;

            FileStat fileStat;
            if (isDir)
            {
                var dirInfo = new DirectoryInfo(hostPath);
                fileStat = BuildFileStatForDirectory(
                    new FileDescriptor { Type = Filetype.Directory },
                    dirInfo
                );
            }
            else
            {
                var fileInfo = new FileInfo(hostPath);
                fileStat = BuildFileStatForFile(
                    new FileDescriptor { Type = Filetype.RegularFile },
                    fileInfo
                );
            }

            mem.WriteStruct(buf, ref fileStat);
            return ErrNo.Success;
        }

        /// <summary>
        /// Adjust the timestamps of a file or directory.
        /// This is similar to utimensat in POSIX.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor to use for resolving the path.</param>
        /// <param name="flags">Flags determining how the path is resolved.</param>
        /// <param name="pathPtr">Pointer to the path of the file or directory.</param>
        /// <param name="pathLen">The length of the path.</param>
        /// <param name="stAtim">The desired value of the data access timestamp.</param>
        /// <param name="stMtim">The desired value of the data modification timestamp.</param>
        /// <param name="fstFlags">A bitmask indicating which timestamps to adjust.</param>
        /// <returns>Returns ErrNo.Success if successful, otherwise an error code.</returns>
        public ErrNo PathFilestatSetTimes(
            ExecContext ctx,
            fd fd,
            LookupFlags flags,
            ptr pathPtr,
            size pathLen,
            timestamp stAtim,
            timestamp stMtim,
            FstFlags fstFlags
        )
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)pathPtr, (int)pathLen))
                return ErrNo.Inval;

            if (!GetFd(fd, out var dirFileDescriptor))
                return ErrNo.NoEnt;

            // If not a directory and fd>=3, can't interpret the path
            if (dirFileDescriptor.Type != Filetype.Directory && fd >= 3)
                return ErrNo.NotDir;

            var pathStr = mem.ReadString(pathPtr, pathLen);
            var guestPath = Path.Combine(dirFileDescriptor.Path, pathStr);
            var hostPath = _state.PathMapper.MapToHostPath(guestPath);

            FileAttributes attr;
            try
            {
                attr = File.GetAttributes(hostPath);
            }
            catch (FileNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (DirectoryNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }
            catch (IOException)
            {
                return ErrNo.IO;
            }

            bool isDir = attr.HasFlag(FileAttributes.Directory);
            bool isFile = !isDir;
            if (!isDir && !isFile)
                return ErrNo.NoEnt;

            DateTime newAtime = DateTime.UtcNow;
            DateTime newMtime = DateTime.UtcNow;

            if ((fstFlags & FstFlags.ATim) != 0)
                newAtime = Clock.ToDateTimeUtc(stAtim);
            if ((fstFlags & FstFlags.ATimNow) != 0)
                newAtime = DateTime.UtcNow;

            if ((fstFlags & FstFlags.MTim) != 0)
                newMtime = Clock.ToDateTimeUtc(stMtim);
            if ((fstFlags & FstFlags.MTimNow) != 0)
                newMtime = DateTime.UtcNow;

            try
            {
                if (isDir)
                {
                    Directory.SetLastWriteTimeUtc(hostPath, newMtime);
                    Directory.SetLastAccessTimeUtc(hostPath, newAtime);
                }
                else
                {
                    File.SetLastWriteTimeUtc(hostPath, newMtime);
                    File.SetLastAccessTimeUtc(hostPath, newAtime);
                }
            }
            catch (IOException)
            {
                return ErrNo.IO;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }

            return ErrNo.Success;
        }

        #region Helper Methods to Build FileStat

        /// <summary>
        /// Builds a <see cref="FileStat"/> for a file using its FileInfo.
        /// </summary>
        private FileStat BuildFileStatForFile(FileDescriptor fd, FileInfo fileInfo)
        {
            var inode = FileUtil.GenerateInode(fileInfo);
            return new FileStat
            {
                Device = 0,
                Ino = inode,
                Mode = Filetype.RegularFile,
                NLink = 1,
                Size = (filesize)fileInfo.Length,
                ATim = Clock.ToTimestamp(fileInfo.LastAccessTimeUtc),
                MTim = Clock.ToTimestamp(fileInfo.LastWriteTimeUtc),
                CTim = Clock.ToTimestamp(fileInfo.CreationTimeUtc)
            };
        }

        /// <summary>
        /// Builds a <see cref="FileStat"/> for a directory using its DirectoryInfo.
        /// </summary>
        private FileStat BuildFileStatForDirectory(FileDescriptor fd, DirectoryInfo dirInfo)
        {
            var inode = FileUtil.GenerateInode(dirInfo);
            return new FileStat
            {
                Device = 0,
                Ino = inode,
                Mode = Filetype.Directory,
                NLink = 1,
                Size = 0, // Directories typically show 0 size in WASI
                ATim = Clock.ToTimestamp(dirInfo.LastAccessTimeUtc),
                MTim = Clock.ToTimestamp(dirInfo.LastWriteTimeUtc),
                CTim = Clock.ToTimestamp(dirInfo.CreationTimeUtc)
            };
        }

        #endregion
    }
}