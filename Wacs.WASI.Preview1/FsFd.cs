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
using Wacs.HostBindings;
using Wacs.WASI.Preview1.Extensions;
using Wacs.WASI.Preview1.Internal;
using Wacs.WASI.Preview1.Types;
using ptr = System.UInt32;
using fd = System.UInt32;
using filesize = System.UInt64;
using size = System.UInt32;
using dircookie = System.Int64;
using filedelta = System.Int64;

namespace Wacs.WASI.Preview1
{
    public partial class FileSystem
    {
        private static readonly long DirEntSize = Marshal.SizeOf<DirEnt>();

        // ============================================================
        // Interpreter wrappers — delegate to AOT-friendly statics below.
        // ============================================================

        public ErrNo FdAdvise(ExecContext ctx, fd fd, filesize offset, filesize len, Advice advice)
            => (ErrNo)FdAdviseCore(_state, (int)fd, (long)offset, (long)len, (int)advice);

        public ErrNo FdAllocate(ExecContext ctx, fd fd, filesize offset, filesize len)
            => (ErrNo)FdAllocateCore(_state, _config, (int)fd, (long)offset, (long)len);

        public ErrNo FdClose(ExecContext ctx, fd fd)
            => (ErrNo)FdCloseCore(_state, (int)fd);

        public ErrNo FdDatasync(ExecContext ctx, fd fd)
            => (ErrNo)FdDatasyncCore(_state, (int)fd);

        public ErrNo FdPread(ExecContext ctx, fd fd, ptr iovsPtr, size iovsLen, filesize offset, ptr nreadPtr)
            => (ErrNo)FdPreadCore(Clock.WacsHost(ctx), _state, (int)fd, (int)iovsPtr, (int)iovsLen, (long)offset, (int)nreadPtr);

        public ErrNo FdPwrite(ExecContext ctx, fd fd, ptr iovsPtr, size iovsLen, filesize offset, ptr nwrittenPtr)
            => (ErrNo)FdPwriteCore(Clock.WacsHost(ctx), _state, (int)fd, (int)iovsPtr, (int)iovsLen, (long)offset, (int)nwrittenPtr);

        public ErrNo FdRead(ExecContext ctx, fd fd, ptr iovsPtr, size iovsLen, ptr nreadPtr)
            => (ErrNo)FdReadCore(Clock.WacsHost(ctx), _state, (int)fd, (int)iovsPtr, (int)iovsLen, (int)nreadPtr);

        public ErrNo FdReaddir(ExecContext ctx, fd fd, ptr bufPtr, size bufLen, dircookie cookie, ptr bufUsedPtr)
            => (ErrNo)FdReaddirCore(Clock.WacsHost(ctx), _state, (int)fd, (int)bufPtr, (int)bufLen, cookie, (int)bufUsedPtr);

        public ErrNo FdRenumber(ExecContext ctx, fd from, fd to)
            => (ErrNo)FdRenumberCore(_state, (int)from, (int)to);

        public ErrNo FdSeek(ExecContext ctx, fd fd, filedelta offset, Whence whence, ptr newoffsetPtr)
            => (ErrNo)FdSeekCore(Clock.WacsHost(ctx), _state, (int)fd, offset, (int)whence, (int)newoffsetPtr);

        public ErrNo FdSync(ExecContext ctx, fd fd)
            => (ErrNo)FdSyncCore(_state, (int)fd);

        public ErrNo FdTell(ExecContext ctx, fd fd, ptr offsetPtr)
            => (ErrNo)FdTellCore(Clock.WacsHost(ctx), _state, (int)fd, (int)offsetPtr);

        public ErrNo FdWrite(ExecContext ctx, fd fd, ptr iovsPtr, size iovsLen, ptr nwrittenPtr)
            => (ErrNo)FdWriteCore(Clock.WacsHost(ctx), _state, (int)fd, (int)iovsPtr, (int)iovsLen, (int)nwrittenPtr);

        // ============================================================
        // AOT-friendly static entry points — discovered by
        // WACS.HostBindings.SourceGen via [WacsImport].
        // ============================================================

        [WacsImport("wasi_snapshot_preview1", "fd_advise")]
        public static int FdAdviseCore(State state, int fd, long offset, long len, int advice)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if ((ulong)offset + (ulong)len > (ulong)fileDescriptor.Stream.Length)
                return (int)ErrNo.Inval;
            // All advice values are no-ops in our backend; reject only unknown bits.
            switch ((Advice)advice)
            {
                case Advice.Normal:
                case Advice.Sequential:
                case Advice.Random:
                case Advice.WillNeed:
                case Advice.DontNeed:
                case Advice.NoReuse:
                    return (int)ErrNo.Success;
                default:
                    return (int)ErrNo.Inval;
            }
        }

        [WacsImport("wasi_snapshot_preview1", "fd_allocate")]
        public static int FdAllocateCore(State state, WasiConfiguration config,
            int fd, long offset, long len)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            // fd_allocate is a regular-file operation; directories return
            // ISDIR (rust/dir_fd_op_failures line 107).
            if (fileDescriptor.Type == Filetype.Directory) return (int)ErrNo.IsDir;

            long newLen = offset + len;
            if (newLen > config.MaxFileSize) return (int)ErrNo.Inval;

            fileDescriptor.Stream.SetLength(Math.Max(fileDescriptor.Stream.Length, newLen));
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_close")]
        public static int FdCloseCore(State state, int fd)
        {
            try { WasiFsHelpers.TryRemoveFd(state, (uint)fd); }
            catch (Exception) { return (int)ErrNo.IO; }
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_datasync")]
        public static int FdDatasyncCore(State state, int fd)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            fileDescriptor.Stream.Flush();
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_pread")]
        public static int FdPreadCore(WacsHostMemory mem, State state,
            int fd, int iovsPtr, int iovsLen, long offset, int nreadPtr)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if ((fileDescriptor.Rights & Rights.FD_READ) == 0) return (int)ErrNo.NotCapable;
            if (fileDescriptor.Type == Filetype.Directory) return (int)ErrNo.IsDir;
            if (!fileDescriptor.Stream.CanRead) return (int)ErrNo.IO;

            var origin = fileDescriptor.Stream.Position;
            fileDescriptor.Stream.Seek(offset, SeekOrigin.Begin);

            IoVec[] iovs = mem.ReadStructs<IoVec>(iovsPtr, iovsLen);
            int totalRead = 0;
            try
            {
                foreach (var iov in iovs)
                {
                    if (iov.bufLen == 0) continue;
                    var dest = mem.AsSpan((int)iov.bufPtr, (int)iov.bufLen);
#if NET8_0_OR_GREATER
                    int read = fileDescriptor.Stream.Read(dest);
#else
                    var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(dest.Length);
                    int read;
                    try
                    {
                        read = fileDescriptor.Stream.Read(buf, 0, dest.Length);
                        if (read > 0) buf.AsSpan(0, read).CopyTo(dest);
                    }
                    finally { System.Buffers.ArrayPool<byte>.Shared.Return(buf); }
#endif
                    totalRead += read;
                    if (read < iov.bufLen) break;
                }
                mem.WriteInt32(nreadPtr, totalRead);
                return (int)ErrNo.Success;
            }
            finally { fileDescriptor.Stream.Seek(origin, SeekOrigin.Begin); }
        }

        [WacsImport("wasi_snapshot_preview1", "fd_pwrite")]
        public static int FdPwriteCore(WacsHostMemory mem, State state,
            int fd, int iovsPtr, int iovsLen, long offset, int nwrittenPtr)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if ((fileDescriptor.Rights & Rights.FD_WRITE) == 0) return (int)ErrNo.NotCapable;
            if (fileDescriptor.Type == Filetype.Directory) return (int)ErrNo.IsDir;
            if (!fileDescriptor.Stream.CanWrite) return (int)ErrNo.IO;

            var origin = fileDescriptor.Stream.Position;
            try
            {
                fileDescriptor.Stream.Seek(offset, SeekOrigin.Begin);
                IoVec[] iovs = mem.ReadStructs<IoVec>(iovsPtr, iovsLen);
                int totalWritten = 0;
                foreach (var iov in iovs)
                {
                    if (iov.bufLen == 0) continue;
                    var src = mem.AsSpan((int)iov.bufPtr, (int)iov.bufLen);
#if NET8_0_OR_GREATER
                    fileDescriptor.Stream.Write(src);
#else
                    fileDescriptor.Stream.Write(src.ToArray(), 0, src.Length);
#endif
                    totalWritten += src.Length;
                }
                mem.WriteInt32(nwrittenPtr, totalWritten);
                return (int)ErrNo.Success;
            }
            finally { fileDescriptor.Stream.Seek(origin, SeekOrigin.Begin); }
        }

        [WacsImport("wasi_snapshot_preview1", "fd_read")]
        public static int FdReadCore(WacsHostMemory mem, State state,
            int fd, int iovsPtr, int iovsLen, int nreadPtr)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if ((fileDescriptor.Rights & Rights.FD_READ) == 0) return (int)ErrNo.NotCapable;
            if (fileDescriptor.Type == Filetype.Directory) return (int)ErrNo.IsDir;
            if (!fileDescriptor.Stream.CanRead) return (int)ErrNo.IO;

            IoVec[] iovs = mem.ReadStructs<IoVec>(iovsPtr, iovsLen);
            int totalRead = 0;
            foreach (var iov in iovs)
            {
                if (iov.bufLen == 0) continue;
                var dest = mem.AsSpan((int)iov.bufPtr, (int)iov.bufLen);
#if NET8_0_OR_GREATER
                int read = fileDescriptor.Stream.Read(dest);
#else
                var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(dest.Length);
                int read;
                try
                {
                    read = fileDescriptor.Stream.Read(buf, 0, dest.Length);
                    if (read > 0) buf.AsSpan(0, read).CopyTo(dest);
                }
                finally { System.Buffers.ArrayPool<byte>.Shared.Return(buf); }
#endif
                totalRead += read;
                if (read < dest.Length) break;
            }
            mem.WriteInt32(nreadPtr, totalRead);
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_readdir")]
        public static int FdReaddirCore(WacsHostMemory mem, State state,
            int fd, int bufPtr, int bufLen, long cookie, int bufUsedPtr)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if (fileDescriptor.Type != Filetype.Directory) return (int)ErrNo.NotDir;

            var hostPath = state.PathMapper.MapToHostPath(fileDescriptor.Path);
            var entries = DirectoryExtensions.EnumerateFileSystemEntriesSafely(hostPath);

            // Synthesize "." and ".." — Directory.EnumerateFileSystemEntries
            // omits them on .NET, but rust/fd_readdir asserts both are
            // present in any non-error readdir. Inode for "." matches the
            // dir itself; ".." reaches for the parent (falls back to self
            // for the root preopen with no parent).
            ulong selfInode = 0;
            try { selfInode = FileUtil.GenerateInode(new DirectoryInfo(hostPath)); } catch { }
            ulong parentInode = selfInode;
            try
            {
                var parent = Path.GetDirectoryName(hostPath);
                if (!string.IsNullOrEmpty(parent))
                    parentInode = FileUtil.GenerateInode(new DirectoryInfo(parent));
            }
            catch { }

            var array = new List<(long, DirEnt, byte[])>();
            long runningCookie = 0;
            void AddEntry(string name, Filetype fileType, ulong inode)
            {
                byte[] nameBytes = Encoding.UTF8.GetBytes(name);
                long nameLen = nameBytes.Length;
                long mycookie = runningCookie;
                runningCookie += DirEntSize + nameLen;
                array.Add((mycookie, new DirEnt
                {
                    DNext = runningCookie,
                    DIno = inode,
                    DNamlen = (uint)nameLen,
                    DType = fileType,
                }, nameBytes));
            }

            AddEntry(".",  Filetype.Directory, selfInode);
            AddEntry("..", Filetype.Directory, parentInode);

            foreach (var entry in entries)
            {
                if (entry == "." || entry == "..") continue;

                FileAttributes? attributes = null;
                try { attributes = File.GetAttributes(entry); } catch { continue; }

                var fileType = attributes.Value.HasFlag(FileAttributes.Directory) ? Filetype.Directory : Filetype.RegularFile;
                var inode = fileType is Filetype.Directory
                    ? FileUtil.GenerateInode(new DirectoryInfo(entry))
                    : FileUtil.GenerateInode(new FileInfo(entry));

                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name)) continue;
                AddEntry(name, fileType, inode);
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

            if (cookie >= allDirEnts.Length)
            {
                mem.WriteInt32(bufUsedPtr, 0);
                return (int)ErrNo.Success;
            }

            int startOffset = (int)cookie;
            int possibleBytes = allDirEnts.Length - startOffset;
            int bytesToCopy = (int)Math.Min(bufLen, possibleBytes);
            window.Slice(startOffset, bytesToCopy).CopyTo(mem.AsSpan(bufPtr, bytesToCopy));
            mem.WriteInt32(bufUsedPtr, bytesToCopy);
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_renumber")]
        public static int FdRenumberCore(State state, int from, int to)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)from, out _)) return (int)ErrNo.NoEnt;
            if (!WasiFsHelpers.TryGetFd(state, (uint)to,   out _)) return (int)ErrNo.NoEnt;
            try
            {
                WasiFsHelpers.TryRemoveFd(state, (uint)to);
                WasiFsHelpers.MoveFd(state, (uint)from, (uint)to);
            }
            catch (Exception) { return (int)ErrNo.IO; }
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_seek")]
        public static int FdSeekCore(WacsHostMemory mem, State state,
            int fd, long offset, int whence, int newoffsetPtr)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if (fileDescriptor.Type == Filetype.Directory) return (int)ErrNo.IsDir;

            long newPosition;
            switch ((Whence)whence)
            {
                case Whence.Set: newPosition = offset; break;
                case Whence.Cur: newPosition = fileDescriptor.Stream.Position + offset; break;
                case Whence.End: newPosition = fileDescriptor.Stream.Length + offset; break;
                default: return (int)ErrNo.Inval;
            }
            if (newPosition < 0) return (int)ErrNo.Inval;

            fileDescriptor.Stream.Seek(newPosition, SeekOrigin.Begin);
            mem.WriteInt64(newoffsetPtr, newPosition);
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_sync")]
        public static int FdSyncCore(State state, int fd)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if (fileDescriptor.Type == Filetype.Directory) return (int)ErrNo.IsDir;
            fileDescriptor.Stream.Flush();
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_tell")]
        public static int FdTellCore(WacsHostMemory mem, State state, int fd, int offsetPtr)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            try
            {
                if (fileDescriptor.Type == Filetype.Directory) return (int)ErrNo.IsDir;
                mem.WriteInt64(offsetPtr, fileDescriptor.Stream.Position);
            }
            catch (Exception) { return (int)ErrNo.IO; }
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_write")]
        public static int FdWriteCore(WacsHostMemory mem, State state,
            int fd, int iovsPtr, int iovsLen, int nwrittenPtr)
        {
            if (!state.FileDescriptors.TryGetValue((uint)fd, out var fileDescriptor))
                return (int)ErrNo.NoEnt;
            if ((fileDescriptor.Rights & Rights.FD_WRITE) == 0) return (int)ErrNo.NotCapable;
            if (fileDescriptor.Type == Filetype.Directory) return (int)ErrNo.IsDir;
            if (!fileDescriptor.Stream.CanWrite) return (int)ErrNo.IO;

            // O_APPEND / FD_APPEND semantics: seek to end before each
            // write so concurrent writers don't clobber each other.
            if ((fileDescriptor.Flags & FdFlags.Append) != 0 &&
                fileDescriptor.Stream.CanSeek)
                fileDescriptor.Stream.Seek(0, SeekOrigin.End);

            // Each iovec is 8 bytes: u32 bufPtr + u32 bufLen.
            int totalWritten = 0;
            for (int i = 0; i < iovsLen; i++)
            {
                int iovOff = iovsPtr + i * 8;
                if (!mem.Contains(iovOff, 8)) return (int)ErrNo.Fault;
                int bufPtr = mem.ReadInt32LE(iovOff);
                int bufLen = mem.ReadInt32LE(iovOff + 4);
                if (!mem.Contains(bufPtr, bufLen)) return (int)ErrNo.Fault;

                var span = mem.AsSpan(bufPtr, bufLen);
#if NET8_0_OR_GREATER
                fileDescriptor.Stream.Write(span);
#else
                fileDescriptor.Stream.Write(span.ToArray(), 0, bufLen);
#endif
                totalWritten += bufLen;
            }

            if (!mem.Contains(nwrittenPtr, 4)) return (int)ErrNo.Fault;
            mem.WriteInt32LE(nwrittenPtr, totalWritten);
            return (int)ErrNo.Success;
        }
    }
}
