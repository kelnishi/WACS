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
using timestamp = System.UInt64;

namespace Wacs.WASIp1
{
    public partial class Filesystem
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
            
            FileDescriptor fileDescriptor;
            try
            {
                fileDescriptor = GetFD(fd);
            }
            catch (ArgumentException)
            {
                return ErrNo.NoEnt;
            }

            //TODO: I really don't know if these flags are correct...
            FdFlags flags = FdFlags.None;
            
            var hostPath = _state.PathMapper.MapToHostPath(fileDescriptor.Path);
            var fileInfo = new FileInfo(hostPath);
            
            if (!fileDescriptor.Stream.CanTimeout)
            {
                flags |= FdFlags.NonBlock;
            }
            
            if (fileDescriptor.Stream is FileStream fileStream)
            {
                if ((fileInfo.Attributes & FileAttributes.Archive) == FileAttributes.Archive)
                    flags |= FdFlags.Append; 
                
                if (!fileStream.IsAsync)
                {
                    flags |= FdFlags.Sync;
                }
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
            if (!mem.Contains((int)bufPtr, FdStatSize))
                return ErrNo.Inval;
            
            FileDescriptor fileDescriptor;
            try
            {
                fileDescriptor = GetFD(fd);
            }
            catch (ArgumentException)
            {
                return ErrNo.NoEnt;
            }

            var hostPath = _state.PathMapper.MapToHostPath(fileDescriptor.Path);
            var fileInfo = new FileInfo(hostPath);
            
            var fileStat = new FileStat
            {
                Device = 0, // Device ID - can be set later if available
                Ino = FileUtil.GenerateInode(fileInfo),
                Mode = fileDescriptor.Type,
                NLink = 1, // Number of hard links - can be adjusted as needed
                Size = (filesize)fileInfo.Length,
                ATim = Clock.ToTimestamp(fileInfo.LastAccessTimeUtc),
                MTim = Clock.ToTimestamp(fileInfo.LastWriteTimeUtc),
                CTim = Clock.ToTimestamp(fileInfo.CreationTimeUtc)
            };

            mem.WriteStruct(bufPtr, ref fileStat);
            
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
            try
            {
                var fileDescriptor = GetFD(fd);
                fileDescriptor.Stream.SetLength((long)stSize);
            }
            catch (ArgumentException)
            {
                return ErrNo.NoEnt;
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
            try
            {
                var fileDescriptor = GetFD(fd);
                var hostPath = _state.PathMapper.MapToHostPath(fileDescriptor.Path);
                var fileInfo = new FileInfo(hostPath);

                if ((flags & FstFlags.ATim) != 0)
                {
                    fileInfo.LastAccessTimeUtc = Clock.ToDateTimeUtc(atim);
                }
                if ((flags & FstFlags.MTim) != 0)
                {
                    fileInfo.LastWriteTimeUtc = Clock.ToDateTimeUtc(mtim);
                }
                if ((flags & FstFlags.ATimNow) != 0)
                {
                    fileInfo.LastAccessTimeUtc = DateTime.Now.ToUniversalTime();
                }
                if ((flags & FstFlags.MTimNow) != 0)
                {
                    fileInfo.LastWriteTimeUtc = DateTime.Now.ToUniversalTime();
                }

                // FileInfo does not directly support updating timestamps, so we need to use the File.SetLastWriteTimeUtc
                File.SetLastWriteTimeUtc(hostPath, fileInfo.LastWriteTimeUtc);
                File.SetLastAccessTimeUtc(hostPath, fileInfo.LastAccessTimeUtc);
            }
            catch (ArgumentException)
            {
                return ErrNo.NoEnt;
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
            if (!mem.Contains((int)bufPtr, sizeof(ptr)))
                return ErrNo.Inval;

            FileDescriptor fileDescriptor;
            try
            {
                fileDescriptor = GetFD(fd);
            }
            catch (ArgumentException)
            {
                //Signal that there are no more FDs
                return ErrNo.Badf;
            }
            
            var name = fileDescriptor.Path;
            var utf8Name = Encoding.UTF8.GetBytes(name);
            
            Prestat prestat = new Prestat
            {
                Tag = fileDescriptor.Type == Filetype.Directory
                    ? PrestatTag.Dir
                    : PrestatTag.NotDir,
                Dir = new PrestatDir
                {
                    NameLen = (uint)utf8Name.Length+1
                }
            };

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

            FileDescriptor fileDescriptor;
            try
            {
                fileDescriptor = GetFD(fd);
            }
            catch (ArgumentException)
            {
                return ErrNo.NoEnt;
            }

            if (fileDescriptor.Type != Filetype.Directory)
                return ErrNo.NotDir;

            var name = fileDescriptor.Path;
            var utf8Name = Encoding.UTF8.GetBytes(name);
            if (utf8Name.Length+1 > pathLen)
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

            FileDescriptor fileDescriptor;
            try
            {
                fileDescriptor = GetFD(fd);
            }
            catch (ArgumentException)
            {
                return ErrNo.NoEnt;
            }

            var hostPath = _state.PathMapper.MapToHostPath(fileDescriptor.Path);
            var fileInfo = new FileInfo(hostPath);

            var fileStat = new FileStat
            {
                Device = 0, // Device ID - can be set later if available
                Ino = FileUtil.GenerateInode(fileInfo),
                Mode = fileDescriptor.Type,
                NLink = 1, // Number of hard links - can be adjusted as needed
                Size = (filesize)fileInfo.Length,
                ATim = Clock.ToTimestamp(fileInfo.LastAccessTimeUtc),
                MTim = Clock.ToTimestamp(fileInfo.LastWriteTimeUtc),
                CTim = Clock.ToTimestamp(fileInfo.CreationTimeUtc)
            };

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
        public ErrNo PathFilestatSetTimes(ExecContext ctx, fd fd, LookupFlags flags, ptr pathPtr, size pathLen, timestamp stAtim, timestamp stMtim,
            FstFlags fstFlags)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)pathPtr, (int)pathLen))
                return ErrNo.Inval;

            FileDescriptor fileDescriptor;
            try
            {
                fileDescriptor = GetFD(fd);
            }
            catch (ArgumentException)
            {
                return ErrNo.NoEnt;
            }
            
            var hostPath = _state.PathMapper.MapToHostPath(fileDescriptor.Path);
            var fileInfo = new FileInfo(hostPath);
            

            if ((fstFlags & FstFlags.ATim) != 0)
            {
                fileInfo.LastAccessTimeUtc = Clock.ToDateTimeUtc(stAtim);
            }
            if ((fstFlags & FstFlags.MTim) != 0)
            {
                fileInfo.LastWriteTimeUtc = Clock.ToDateTimeUtc(stMtim);
            }
            if ((fstFlags & FstFlags.ATimNow) != 0)
            {
                fileInfo.LastAccessTimeUtc = DateTime.UtcNow;
            }
            if ((fstFlags & FstFlags.MTimNow) != 0)
            {
                fileInfo.LastWriteTimeUtc = DateTime.UtcNow;
            }

            File.SetLastWriteTimeUtc(hostPath, fileInfo.LastWriteTimeUtc);
            File.SetLastAccessTimeUtc(hostPath, fileInfo.LastAccessTimeUtc);
            
            return ErrNo.Success;
        }
    }
}