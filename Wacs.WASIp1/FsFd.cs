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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.WASIp1.Types;
using ptr = System.UInt32;
using fd = System.UInt32;
using filesize = System.UInt64;
using size = System.UInt32;
using dircookie = System.Int64;
using filedelta = System.Int64;
using Wacs.WASIp1.Extensions;


namespace Wacs.WASIp1
{
    public partial class FileSystem
    {
        private static readonly long DirEntSize = Marshal.SizeOf<DirEnt>();

        /// <summary>
        /// Provides advice on file usage for the specified file descriptor.
        /// This function interacts with the file system to provide advisory information
        /// to optimize file access patterns based on the provided advice parameter.
        ///
        /// <para>Note: This function is akin to POSIX's posix_fadvise.</para>
        /// </summary>
        /// <param name="ctx">Execution context of the calling program.</param>
        /// <param name="fd">File descriptor referencing an open file.</param>
        /// <param name="offset">The offset within the file to which the advisory applies.</param>
        /// <param name="len">The length of the region to which the advisory applies.</param>
        /// <param name="advice">The advisory information indicating how to handle the file.</param>
        /// <returns>Returns an ErrNo indicating success or specific error code.</returns>
        public ErrNo FdAdvise(ExecContext ctx, fd fd, filesize offset, filesize len, Advice advice)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt; // The file descriptor does not exist.
            }

            if (offset + len > (filesize)fileDescriptor.Stream.Length)
            {
                return ErrNo.Inval; // Invalid offset or length.
            }

            switch (advice)
            {
                case Advice.Normal:
                    // No special handling needed; we can ignore this.
                    break;
                case Advice.Sequential:
                    // Optionally implement strategies for sequential access optimization.
                    // For example, prefetching data in anticipation.
                    break;
                case Advice.Random:
                    // Handle potential optimizations for random access patterns, though
                    // most implementations may ignore this advice.
                    break;
                case Advice.WillNeed:
                    // This indicates the application will access this data; we might
                    // mark it for caching if there were caching mechanisms.
                    break;
                case Advice.DontNeed:
                    // Indicate to evict or ignore this data from caches if applicable.
                    break;
                case Advice.NoReuse:
                    // Handle advice for data that will not be reused.
                    break;
                default:
                    return ErrNo.Inval; // Unrecognized advice type.
            }

            //HACK We're not that fancy...
            return ErrNo.Success;
        }


        /// <summary>
        /// Forces the allocation of space in the file for the specified file descriptor.
        /// This function ensures that a specified range of bytes is allocated in the file,
        /// potentially extending it if necessary.
        ///
        /// <para>Note: This function is similar to POSIX's posix_fallocate.</para>
        /// </summary>
        /// <param name="ctx">Execution context of the calling program.</param>
        /// <param name="fd">File descriptor referencing an open file.</param>
        /// <param name="offset">The offset at which to start the allocation.</param>
        /// <param name="len">The length of the area to allocate.</param>
        /// <returns>Returns an ErrNo indicating success or specific error code.</returns>
        public ErrNo FdAllocate(ExecContext ctx, fd fd, filesize offset, filesize len)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt; // The file descriptor does not exist.
            }

            long newLen = (long)(offset + len);

            if (newLen > _config.MaxFileSize)
            {
                return ErrNo.Inval; // Invalid offset or length.
            }

            // Ensure the file is extended if needed.
            fileDescriptor.Stream.SetLength(Math.Max(fileDescriptor.Stream.Length, newLen));

            return ErrNo.Success;
        }

        /// <summary>
        /// Closes the specified file descriptor, releasing any resources associated with it.
        /// Once closed, the file descriptor cannot be used until it is re-opened.
        ///
        /// <para>Note: This function is analogous to POSIX's close.</para>
        /// </summary>
        /// <param name="ctx">Execution context of the calling program.</param>
        /// <param name="fd">File descriptor to close.</param>
        /// <returns>Returns an ErrNo indicating success or specific error code.</returns>
        public ErrNo FdClose(ExecContext ctx, fd fd)
        {
            try
            {
                RemoveFd(fd);
            }
            catch (Exception)
            {
                return ErrNo.IO; // Return a generic I/O error indicating the closure failed.
            }

            return ErrNo.Success; // Successfully closed the file descriptor.
        }


        /// <summary>
        /// Synchronizes the data of a file represented by a file descriptor to disk.
        /// This ensures that all modifications made to the file are persisted.
        ///
        /// <para>Note: This function is akin to POSIX's fdatasync.</para>
        /// </summary>
        /// <param name="ctx">Execution context of the calling program.</param>
        /// <param name="fd">File descriptor referencing an open file.</param>
        /// <returns>Returns an ErrNo indicating success or specific error code.</returns>
        public ErrNo FdDatasync(ExecContext ctx, fd fd)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt; // The file descriptor does not exist.
            }

            // Ensure data is written to disk
            fileDescriptor.Stream.Flush();

            return ErrNo.Success;
        }

        /// <summary>
        /// Reads data from a file descriptor into a series of buffers specified by scatter/gather vectors.
        /// The read operation does not update the file descriptor's offset.
        /// 
        /// <para>Note: This function resembles POSIX's preadv.</para>
        /// </summary>
        /// <param name="ctx">Execution context of the calling program.</param>
        /// <param name="fd">File descriptor to read from.</param>
        /// <param name="iovsPtr">Pointer to the buffer(s) where read data will be stored.</param>
        /// <param name="iovsLen">Length of the iovec array.</param>
        /// <param name="offset">The offset within the file where the read should begin.</param>
        /// <param name="nreadPtr">Pointer to a location where the number of read bytes will be stored.</param>
        /// <returns>Returns an ErrNo indicating success or specific error code.</returns>
        public ErrNo FdPread(ExecContext ctx, fd fd, ptr iovsPtr, size iovsLen, filesize offset, ptr nreadPtr)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt; // The file descriptor does not exist.
            }

            if (fileDescriptor.Type == Filetype.Directory)
                return ErrNo.IsDir;

            if (!fileDescriptor.Stream.CanRead)
                return ErrNo.IO;

            var mem = ctx.DefaultMemory;

            var origin = fileDescriptor.Stream.Position;

            // Set the position in the file for reading
            fileDescriptor.Stream.Seek((long)offset, SeekOrigin.Begin);

            IoVec[] iovs = mem.ReadStructs<IoVec>(iovsPtr, iovsLen);
            var largest = iovs.Max(iov => iov.bufLen);
            byte[] buf = new byte[largest];

            int totalRead = 0;
            foreach (var iov in iovs)
            {
                var dest = mem[(int)iov.bufPtr..(int)(iov.bufPtr + iov.bufLen)];
                int read = fileDescriptor.Stream.Read(buf, 0, (int)iov.bufLen);
                totalRead += read;
                var data = buf.AsSpan(0, read);
                data.CopyTo(dest);

                //No more data
                if (read < iov.bufLen)
                    break;
            }

            //Reset the offset
            fileDescriptor.Stream.Seek(origin, SeekOrigin.Begin);

            mem.WriteInt32(nreadPtr, totalRead);

            return ErrNo.Success;
        }

        /// <summary>
        /// Writes to a file descriptor at a specified offset without updating the file descriptor's position.
        /// </summary>
        /// <remarks>
        /// This function implements functionality similar to the POSIX pwritev system call,
        /// allowing writing from multiple buffers at a specified offset in a single operation.
        /// The original file position is preserved after the write operation.
        /// </remarks>
        /// <param name="ctx">The execution context for the operation.</param>
        /// <param name="fd">The file descriptor to write to.</param>
        /// <param name="iovsPtr">A pointer to the scatter/gather array of buffers to write from.</param>
        /// <param name="iovsLen">The length of the scatter/gather array.</param>
        /// <param name="offset">The offset in the file where writing should begin.</param>
        /// <param name="nwrittenPtr">A pointer to store the total number of bytes written.</param>
        /// <returns>An error code indicating the operation's success or failure.</returns>
        public ErrNo FdPwrite(ExecContext ctx, fd fd, ptr iovsPtr, size iovsLen, filesize offset, ptr nwrittenPtr)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt;
            }

            if (fileDescriptor.Type == Filetype.Directory)
            {
                return ErrNo.IsDir;
            }

            if (!fileDescriptor.Stream.CanWrite)
            {
                return ErrNo.IO;
            }

            var mem = ctx.DefaultMemory;
            var origin = fileDescriptor.Stream.Position;

            try
            {
                fileDescriptor.Stream.Seek((long)offset, SeekOrigin.Begin);
                IoVec[] iovs = mem.ReadStructs<IoVec>(iovsPtr, iovsLen);
                int totalWritten = 0;

                foreach (var iov in iovs)
                {
                    var src = mem[(int)iov.bufPtr..(int)(iov.bufPtr + iov.bufLen)];
                    if (src.Length == 0)
                    {
                        continue;
                    }

                    var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(src.Length);
                    try
                    {
                        src.CopyTo(buf);
                        fileDescriptor.Stream.Write(buf, 0, src.Length);
                        totalWritten += src.Length;
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(buf);
                    }

                    if (src.Length < iov.bufLen)
                    {
                        break;
                    }
                }

                mem.WriteInt32(nwrittenPtr, totalWritten);
                return ErrNo.Success;
            }
            finally
            {
                fileDescriptor.Stream.Seek(origin, SeekOrigin.Begin);
            }
        }

        /// <summary>
        /// Reads data from a file descriptor into a scatter/gather array.
        /// </summary>
        /// <remarks>
        /// This function implements functionality similar to the POSIX readv system call,
        /// allowing reading into multiple buffers in a single operation.
        /// </remarks>
        /// <param name="ctx">The execution context for the operation.</param>
        /// <param name="fd">The file descriptor to read from.</param>
        /// <param name="iovsPtr">A pointer to the scatter/gather array where data will be stored.</param>
        /// <param name="iovsLen">The length of the scatter/gather array.</param>
        /// <param name="nreadPtr">A pointer to store the total number of bytes read.</param>
        /// <returns>An error code indicating the operation's success or failure.</returns>
        public ErrNo FdRead(ExecContext ctx, fd fd, ptr iovsPtr, size iovsLen, ptr nreadPtr)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt;
            }

            if (fileDescriptor.Type == Filetype.Directory)
            {
                return ErrNo.IsDir;
            }

            if (!fileDescriptor.Stream.CanRead)
            {
                return ErrNo.IO;
            }

            var mem = ctx.DefaultMemory;
            IoVec[] iovs = mem.ReadStructs<IoVec>(iovsPtr, iovsLen);
            int totalRead = 0;

            foreach (var iov in iovs)
            {
                var dest = mem[(int)iov.bufPtr..(int)(iov.bufPtr + iov.bufLen)];
                if (dest.Length == 0)
                {
                    continue;
                }

#if NET8_0_OR_GREATER
                int read = fileDescriptor.Stream.Read(dest);
#else
        var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(dest.Length);
        int read;
        
        try
        {
            read = fileDescriptor.Stream.Read(buf, 0, dest.Length);
            if (read > 0)
            {
                buf.AsSpan(0, read).CopyTo(dest);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buf);
        }
#endif
                totalRead += read;

                if (read < dest.Length)
                {
                    break;
                }
            }

            mem.WriteInt32(nreadPtr, totalRead);
            return ErrNo.Success;
        }

        /// <summary>
        /// Reads directory entries from a specified directory file descriptor.
        /// </summary>
        /// <remarks>
        /// When successful, the contents of the output buffer consist of a sequence of directory entries.
        /// The function fills the output buffer as much as possible, potentially truncating the last directory entry.
        /// </remarks>
        /// <param name="ctx">The execution context for the operation.</param>
        /// <param name="fd">The directory file descriptor to read from.</param>
        /// <param name="bufPtr">A pointer to the buffer where directory entries will be stored.</param>
        /// <param name="bufLen">The length of the buffer in bytes.</param>
        /// <param name="cookie">The starting position within the directory from which to begin reading.</param>
        /// <param name="bufUsedPtr">A pointer to store the number of bytes written to the buffer.</param>
        /// <returns>An error code indicating the operation's success or failure.</returns>
        public ErrNo FdReaddir(ExecContext ctx, fd fd, ptr bufPtr, size bufLen, dircookie cookie, ptr bufUsedPtr)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt;
            }

            if (fileDescriptor.Type != Filetype.Directory)
            {
                return ErrNo.NotDir;
            }

            var hostPath = _state.PathMapper.MapToHostPath(fileDescriptor.Path);
            var entries = DirectoryExtensions.EnumerateFileSystemEntriesSafely(hostPath);

            List<(dircookie, DirEnt, byte[])> array = new();
            dircookie runningCookie = 0;

            foreach (var entry in entries)
            {
                if (entry == "." || entry == "..")
                {
                    continue;
                }

                dircookie mycookie = runningCookie;

                FileAttributes? attributes = null;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch
                {
                    continue;
                }

                var fileType = attributes.Value.HasFlag(FileAttributes.Directory) ? Filetype.Directory : Filetype.RegularFile;
                var inode = fileType is Filetype.Directory ?
                    FileUtil.GenerateInode(new DirectoryInfo(entry)) :
                    FileUtil.GenerateInode(new FileInfo(entry));

                var name = Path.GetFileName(entry);

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                byte[] nameBytes = Encoding.UTF8.GetBytes(name);
                long nameLen = nameBytes.Length;

                runningCookie += DirEntSize + nameLen;

                DirEnt dirent = new DirEnt
                {
                    DNext = runningCookie,
                    DIno = inode,
                    DNamlen = (uint)nameLen,
                    DType = fileType,
                };

                array.Add((mycookie, dirent, nameBytes));
            }

            byte[] allDirEnts = new byte[runningCookie];
            var window = allDirEnts.AsSpan();

            foreach (var (startCookie, struc, nameBytes) in array)
            {
                var dirEnt = struc;
                int start = (int)startCookie;
                int delim = (int)(start + DirEntSize);
                int end = delim + nameBytes.Length;

#if NET8_0_OR_GREATER
                MemoryMarshal.Write(window[start..delim], in dirEnt);
#else
        MemoryMarshal.Write(window[start..delim], ref dirEnt);
#endif
                nameBytes.CopyTo(window[delim..end]);
            }

            var mem = ctx.DefaultMemory;

            if (cookie >= allDirEnts.Length)
            {
                mem.WriteInt32(bufUsedPtr, 0);
                return ErrNo.Success;
            }

            int startOffset = (int)cookie;
            int possibleBytes = allDirEnts.Length - startOffset;
            int bytesToCopy = (int)Math.Min(bufLen, possibleBytes);

            var sourceSlice = window.Slice(startOffset, bytesToCopy);
            var targetSlice = mem[(int)bufPtr..((int)bufPtr + bytesToCopy)];
            sourceSlice.CopyTo(targetSlice);

            mem.WriteInt32(bufUsedPtr, bytesToCopy);

            return ErrNo.Success;
        }

        /// <summary>
        /// Atomically replace a file descriptor by renumbering another file descriptor.
        /// This function provides a way to perform this operation in a thread-safe manner.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="from">The file descriptor to move.</param>
        /// <param name="to">The file descriptor to be overwrite.</param>
        /// <returns>An error code indicating success or failure.</returns>
        public ErrNo FdRenumber(ExecContext ctx, fd from, fd to)
        {
            //Check if both exist
            if (!GetFd(from, out _))
                return ErrNo.NoEnt;
            if (!GetFd(to, out _))
                return ErrNo.NoEnt;
            try
            {
                // Close the "to" file descriptor if it is open
                RemoveFd(to);

                // Assign the "to" file descriptor to the same underlying resources of the "from" fd
                MoveFd(from, to);
            }
            catch (Exception)
            {
                return ErrNo.IO; // Return a generic I/O error indicating the operation failed.
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// Move the offset of a file descriptor.
        /// This function is similar to `lseek` in POSIX.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor to seek on.</param>
        /// <param name="offset">The number of bytes to move.</param>
        /// <param name="whence">The base from which the offset is relative.</param>
        /// <param name="newoffsetPtr">A pointer to where the new offset will be reported</param>
        /// <returns>An error code indicating success or failure.</returns>
        public ErrNo FdSeek(ExecContext ctx, fd fd, filedelta offset, Whence whence, ptr newoffsetPtr)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt; // The file descriptor does not exist.
            }

            if (fileDescriptor.Type == Filetype.Directory)
                return ErrNo.IsDir;

            long newPosition;
            switch (whence)
            {
                case Whence.Set:
                    newPosition = offset;
                    break;
                case Whence.Cur:
                    newPosition = fileDescriptor.Stream.Position + offset;
                    break;
                case Whence.End:
                    newPosition = fileDescriptor.Stream.Length + offset;
                    break;
                default:
                    return ErrNo.Inval;
            }

            if (newPosition < 0)
            {
                return ErrNo.Inval; // Negative offset.
            }

            fileDescriptor.Stream.Seek(newPosition, SeekOrigin.Begin);

            var mem = ctx.DefaultMemory;
            mem.WriteInt32(newoffsetPtr, (int)newPosition);

            return ErrNo.Success;
        }

        /// <summary>
        /// Synchronize the data and metadata of a file to disk.
        /// This function is similar to `fsync` in POSIX.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor to synchronize.</param>
        /// <returns>An error code indicating success or failure.</returns>
        public ErrNo FdSync(ExecContext ctx, fd fd)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt; // The file descriptor does not exist.
            }

            if (fileDescriptor.Type == Filetype.Directory)
                return ErrNo.IsDir;

            fileDescriptor.Stream.Flush();

            return ErrNo.Success;
        }

        /// <summary>
        /// Return the current offset of a file descriptor.
        /// This function is similar to `lseek(fd, 0, SEEK_CUR)` in POSIX.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor to query.</param>
        /// <param name="offsetPtr">Pointer to where the current offset will be stored.</param>
        /// <returns>An error code indicating success or failure.</returns>
        public ErrNo FdTell(ExecContext ctx, fd fd, ptr offsetPtr)
        {
            var mem = ctx.DefaultMemory;
            if (!GetFd(fd, out var fileDescriptor))
                return ErrNo.NoEnt;

            try
            {
                if (fileDescriptor.Type == Filetype.Directory)
                    return ErrNo.IsDir;

                filesize currentPosition = (filesize)fileDescriptor.Stream.Position;
                mem.WriteInt64(offsetPtr, currentPosition);
            }
            catch (Exception)
            {
                return ErrNo.IO;
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// Write to a file descriptor.
        /// This function is similar to `writev` in POSIX.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor to write to.</param>
        /// <param name="iovsPtr">Pointer to the scatter/gather array of buffers to write from.</param>
        /// <param name="iovsLen">The length of the scatter/gather array.</param>
        /// <param name="nwrittenPtr">Pointer to where the number of bytes written will be stored.</param>
        /// <returns>An error code indicating success or failure.</returns>
        public ErrNo FdWrite(ExecContext ctx, fd fd, ptr iovsPtr, size iovsLen, ptr nwrittenPtr)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt; // The file descriptor does not exist.
            }

            if (fileDescriptor.Type == Filetype.Directory)
                return ErrNo.IsDir;

            if (!fileDescriptor.Stream.CanWrite)
                return ErrNo.IO;

            var mem = ctx.DefaultMemory;

            IoVec[] iovs = mem.ReadStructs<IoVec>(iovsPtr, iovsLen);
            int totalWritten = 0;

            foreach (var iov in iovs)
            {
                var src = mem[(int)iov.bufPtr..(int)(iov.bufPtr + iov.bufLen)];
                fileDescriptor.Stream.Write(src.ToArray(), 0, src.Length);
                totalWritten += src.Length;

                // Check for full write
                if (src.Length < iov.bufLen)
                    break;
            }

            mem.WriteInt32(nwrittenPtr, totalWritten);

            return ErrNo.Success;
        }
    }
}