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
using dircookie = System.UInt64;
using filedelta = System.Int64;


namespace Wacs.WASIp1
{
    public partial class Filesystem
    {
        private static readonly ulong DirEntSize = (ulong)Marshal.SizeOf<DirEnt>();

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
                RemoveFD(fd);
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
        /// Write to a file descriptor, without using and updating the file descriptor's offset.
        /// This function is similar to `pwritev` in Linux and other Unix-like systems.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor to write to.</param>
        /// <param name="iovsPtr">Pointer to the scatter/gather array of buffers to write from.</param>
        /// <param name="iovsLen">The length of the scatter/gather array.</param>
        /// <param name="nwrittenPtr">Pointer to where the number of bytes written will be stored.</param>
        /// <returns>An error code indicating success or failure.</returns>
        public ErrNo FdPwrite(ExecContext ctx, fd fd, ptr iovsPtr, size iovsLen, filesize offset, ptr nwrittenPtr)
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

            var origin = fileDescriptor.Stream.Position;

            // Set the position in the file for writing
            fileDescriptor.Stream.Seek((long)offset, SeekOrigin.Begin);

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

            // Reset the offset
            fileDescriptor.Stream.Seek(origin, SeekOrigin.Begin);
            
            mem.WriteInt32(nwrittenPtr, totalWritten);
            
            return ErrNo.Success;
        }

        /// <summary>
        /// Read from a file descriptor.
        /// This function is similar to `readv` in POSIX.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor to read from.</param>
        /// <param name="iovsPtr">Pointer to the scatter/gather array where the data will be stored.</param>
        /// <param name="iovsLen">The length of the scatter/gather array.</param>
        /// <param name="nreadPtr">Pointer to where the number of bytes read will be stored.</param>
        /// <returns>An error code indicating success or failure.</returns>
        public ErrNo FdRead(ExecContext ctx, fd fd, ptr iovsPtr, size iovsLen, ptr nreadPtr)
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

            // Read data into the specified buffers
            IoVec[] iovs = mem.ReadStructs<IoVec>(iovsPtr, iovsLen);
            int totalRead = 0;

            foreach (var iov in iovs)
            {
                var dest = mem[(int)iov.bufPtr..(int)(iov.bufPtr + iov.bufLen)];
                int read = fileDescriptor.Stream.Read(dest.ToArray(), 0, (int)iov.bufLen);
                totalRead += read;

                // Check if we have read all requested bytes
                if (read < iov.bufLen)
                    break;
            }

            mem.WriteInt32(nreadPtr, totalRead);
            
            return ErrNo.Success;
        }

        /// <summary>
        /// Read directory entries from a directory.
        /// When successful, the contents of the output buffer consist of a sequence of directory entries.
        /// This function fills the output buffer as much as possible, potentially truncating the last directory entry.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The directory file descriptor to read from.</param>
        /// <param name="bufPtr">Pointer to the buffer where directory entries are stored.</param>
        /// <param name="bufLen">The length of the buffer.</param>
        /// <param name="cookie">The starting location within the directory to start reading.</param>
        /// <param name="bufUsedPtr">Pointer to where the number of bytes stored in the read buffer will be indicated.</param>
        /// <returns>An error code indicating success or failure.</returns>
        public ErrNo FdReaddir(ExecContext ctx, fd fd, ptr bufPtr, size bufLen, dircookie cookie, ptr bufUsedPtr)
        {
            if (!GetFd(fd, out var fileDescriptor))
            {
                return ErrNo.NoEnt; // The file descriptor does not exist.
            }

            if (fileDescriptor.Type != Filetype.Directory)
                return ErrNo.NotDir;

            var hostPath = _state.PathMapper.MapToHostPath(fileDescriptor.Path);
            var entries = Directory.EnumerateFileSystemEntries(hostPath);

            List<(dircookie, DirEnt, byte[])> array = new();
            dircookie runningCookie = 0;
            foreach (var entry in entries)
            {
                dircookie mycookie = runningCookie;
                
                var entryPath = Path.Combine(hostPath, entry);
                var entryInfo = new FileInfo(entryPath);
                byte[] name = Encoding.UTF8.GetBytes(entry);
                ulong nameLen = (ulong)name.Length;

                runningCookie += DirEntSize + nameLen; 
                
                DirEnt dirent = new DirEnt
                {
                    DNext = runningCookie,
                    DIno = FileUtil.GenerateInode(entryInfo),
                    DNamlen = (uint)nameLen,
                    DType = FileUtil.FiletypeFromInfo(entryInfo),
                };
                
                array.Add((mycookie, dirent, name));
            }

            byte[] buf = new byte[runningCookie];
            var window = buf.AsSpan();
            foreach (var (cook, struc, name) in array)
            {
                int start = (int)cook;
                int delim = (int)(cook + DirEntSize);
                int end = delim + name.Length;
                var entryTarget = window[start..delim];
                var nameTarget = window[delim..end];
                var dirEnt = struc;
                MemoryMarshal.Write(entryTarget, ref dirEnt);
                name.CopyTo(nameTarget);
            }

            var mem = ctx.DefaultMemory;

            //TODO: is this the correct behavior?
            //We're clamping to available buffer space
            var requested = window[(int)cookie..(int)(cookie + bufLen)];
            int available = requested.Length;
            var bufTarget = mem[(int)bufPtr..(int)(bufPtr + available)];
            if (available > bufTarget.Length)
            {
                available = bufTarget.Length;
                requested = window[(int)cookie..((int)cookie + available)];
            }
            
            requested.CopyTo(bufTarget);
            
            mem.WriteInt32(bufUsedPtr, available);

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
                RemoveFD(to);
                
                // Assign the "to" file descriptor to the same underlying resources of the "from" fd
                MoveFD(from, to);
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