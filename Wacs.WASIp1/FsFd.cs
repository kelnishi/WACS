using Wacs.Core.Runtime;
using Wacs.WASIp1.Types;
using ptr = System.UInt32;
using fd = System.UInt32;
using filesize = System.UInt64;
using size = System.UInt32;
using advice = System.Byte;
using dircookie = System.UInt64;
using filedelta = System.Int64;
using whence = System.Byte;

namespace Wacs.WASIp1
{
    public partial class Filesystem
    {
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
        public ErrNo FdAdvise(ExecContext ctx, fd fd, filesize offset, filesize len, advice advice)
        {
            // TODO: Implement this function
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
            // TODO: Implement this function
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
            // TODO: Implement this function
            return ErrNo.Success;
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
            // TODO: Implement this function
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
            // TODO: Implement this function
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
            // TODO: Implement this function
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
            // TODO: Implement this function
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
            // TODO: Implement this function
            return ErrNo.Success;
        }

        /// <summary>
        /// Atomically replace a file descriptor by renumbering another file descriptor.
        /// This function provides a way to perform this operation in a thread-safe manner.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="from">The file descriptor to overwrite.</param>
        /// <param name="to">The file descriptor to be replaced.</param>
        /// <returns>An error code indicating success or failure.</returns>
        public ErrNo FdRenumber(ExecContext ctx, fd from, fd to)
        {
            // TODO: Implement this function
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
        /// <returns>An error code indicating success or failure.</returns>
        public ErrNo FdSeek(ExecContext ctx, fd fd, filedelta offset, whence whence)
        {
            // TODO: Implement this function
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
            // TODO: Implement this function
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
            // TODO: Implement this function
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
            // TODO: Implement this function
            return ErrNo.Success;
        }
    }
}