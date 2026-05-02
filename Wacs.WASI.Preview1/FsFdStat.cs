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
using Wacs.HostBindings;
using Wacs.WASI.Preview1.Internal;
using Wacs.WASI.Preview1.Types;
using ptr = System.UInt32;
using fd = System.UInt32;
using filesize = System.UInt64;
using size = System.UInt32;
using timestamp = System.Int64;

namespace Wacs.WASI.Preview1
{
    public partial class FileSystem
    {
        private static readonly int FdStatSize = Marshal.SizeOf<FdStat>();
        private static readonly int FileStatSize = Marshal.SizeOf<FileStat>();
        private static readonly int PrestatSize = Marshal.SizeOf<Prestat>();

        // Interpreter wrappers — delegate to AOT-friendly statics below.

        public ErrNo FdFdstatGet(ExecContext ctx, fd fd, ptr bufPtr)
            => (ErrNo)FdFdstatGetCore(Clock.WacsHost(ctx), _state, (int)fd, (int)bufPtr);

        public ErrNo FdFdstatSetFlags(ExecContext ctx, fd fd, FdFlags flags)
            => (ErrNo)FdFdstatSetFlagsCore(_state, (int)fd, (int)flags);

        public ErrNo FdFdstatSetRights(ExecContext ctx, fd fd, Rights fs_rights_base, Rights fs_rights_inheriting)
            => (ErrNo)FdFdstatSetRightsCore(_state, (int)fd, (long)fs_rights_base, (long)fs_rights_inheriting);

        public ErrNo FdFilestatGet(ExecContext ctx, fd fd, ptr bufPtr)
            => (ErrNo)FdFilestatGetCore(Clock.WacsHost(ctx), _state, (int)fd, (int)bufPtr);

        public ErrNo FdFilestatSetSize(ExecContext ctx, fd fd, filesize stSize)
            => (ErrNo)FdFilestatSetSizeCore(_state, (int)fd, (long)stSize);

        public ErrNo FdFilestatSetTimes(ExecContext ctx, fd fd, timestamp atim, timestamp mtim, FstFlags flags)
            => (ErrNo)FdFilestatSetTimesCore(_state, (int)fd, atim, mtim, (int)flags);

        public ErrNo FdPrestatGet(ExecContext ctx, fd fd, ptr bufPtr)
            => (ErrNo)FdPrestatGetCore(Clock.WacsHost(ctx), _state, (int)fd, (int)bufPtr);

        public ErrNo FdPrestatDirName(ExecContext ctx, fd fd, ptr pathPtr, size pathLen)
            => (ErrNo)FdPrestatDirNameCore(Clock.WacsHost(ctx), _state, (int)fd, (int)pathPtr, (int)pathLen);

        public ErrNo PathFilestatGet(ExecContext ctx, fd fd, LookupFlags flags, ptr pathPtr, size pathLen, ptr buf)
            => (ErrNo)PathFilestatGetCore(Clock.WacsHost(ctx), _state, (int)fd, (int)flags, (int)pathPtr, (int)pathLen, (int)buf);

        public ErrNo PathFilestatSetTimes(ExecContext ctx, fd fd, LookupFlags flags,
            ptr pathPtr, size pathLen, timestamp stAtim, timestamp stMtim, FstFlags fstFlags)
            => (ErrNo)PathFilestatSetTimesCore(Clock.WacsHost(ctx), _state, (int)fd, (int)flags,
                (int)pathPtr, (int)pathLen, stAtim, stMtim, (int)fstFlags);

        // ============================================================
        // AOT-friendly static entry points — discovered by
        // WACS.HostBindings.SourceGen via [WacsImport].
        // ============================================================

        [WacsImport("wasi_snapshot_preview1", "fd_fdstat_get")]
        public static int FdFdstatGetCore(WacsHostMemory mem, State state, int fd, int bufPtr)
        {
            if (!mem.Contains(bufPtr, FdStatSize)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;

            var hostPath = state.PathMapper.MapToHostPath(fileDescriptor.Path);
            try { File.GetAttributes(hostPath); }
            catch (FileNotFoundException)      { return (int)ErrNo.NoEnt; }
            catch (DirectoryNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            catch (IOException)                { return (int)ErrNo.IO; }

            FdFlags flags = fileDescriptor.Flags;
            if (fileDescriptor.Type == Filetype.RegularFile &&
                fileDescriptor.Stream is FileStream fs && !fs.IsAsync)
                flags |= FdFlags.Sync;

            var fdStat = new FdStat
            {
                Filetype = fileDescriptor.Type,
                Flags = flags,
                RightsBase = fileDescriptor.Rights,
                RightsInheriting = fileDescriptor.InheritedRights,
            };
            mem.WriteStruct(bufPtr, fdStat);
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_fdstat_set_flags")]
        public static int FdFdstatSetFlagsCore(State state, int fd, int flags)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;

            const FdFlags known = FdFlags.Append | FdFlags.DSync |
                                  FdFlags.NonBlock | FdFlags.RSync | FdFlags.Sync;
            var f = (FdFlags)flags;
            if ((f & ~known) != 0) return (int)ErrNo.Inval;

            fileDescriptor.Flags = f;
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_fdstat_set_rights")]
        public static int FdFdstatSetRightsCore(State state, int fd,
            long fs_rights_base, long fs_rights_inheriting)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;

            var newBase = (Rights)fs_rights_base;
            var newInherit = (Rights)fs_rights_inheriting;
            // Spec: "can only be used to remove rights."
            if ((newBase & fileDescriptor.Rights) != newBase)
                return (int)ErrNo.NotCapable;
            if ((newInherit & fileDescriptor.InheritedRights) != newInherit)
                return (int)ErrNo.NotCapable;

            fileDescriptor.Rights = newBase;
            fileDescriptor.InheritedRights = newInherit;
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_filestat_get")]
        public static int FdFilestatGetCore(WacsHostMemory mem, State state, int fd, int bufPtr)
        {
            if (!mem.Contains(bufPtr, FileStatSize)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;

            // For stdio (fd < 3), provide dummy stats.
            if (fd < 3)
            {
                var dummy = new FileStat
                {
                    Device = 0, Ino = (ulong)fd, Mode = fileDescriptor.Type,
                    NLink = 1, Size = 0, ATim = 0, MTim = 0, CTim = 0,
                };
                mem.WriteStruct(bufPtr, dummy);
                return (int)ErrNo.Success;
            }

            var hostPath = state.PathMapper.MapToHostPath(fileDescriptor.Path);
            FileAttributes attr;
            try { attr = File.GetAttributes(hostPath); }
            catch (FileNotFoundException)      { return (int)ErrNo.NoEnt; }
            catch (DirectoryNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            catch (IOException)                { return (int)ErrNo.IO; }

            bool isDir = attr.HasFlag(FileAttributes.Directory);
            FileStat fileStat = isDir
                ? WasiFsHelpers.BuildFileStatForDirectory(new DirectoryInfo(hostPath))
                : WasiFsHelpers.BuildFileStatForFile(new FileInfo(hostPath));
            // FileInfo.Length reflects the on-disk size, which lags any
            // not-yet-flushed FileStream writes (rust/path_open_read_write
            // line 109 catches this — second fd_write extended the stream
            // but the stat showed the pre-flush size). Prefer the live
            // stream length when we have one.
            if (!isDir && fileDescriptor.Stream is FileStream fs && fs.CanSeek)
                fileStat.Size = (ulong)fs.Length;
            mem.WriteStruct(bufPtr, fileStat);
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_filestat_set_size")]
        public static int FdFilestatSetSizeCore(State state, int fd, long stSize)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if (fileDescriptor.Type == Filetype.Directory) return (int)ErrNo.IsDir;

            try { fileDescriptor.Stream.SetLength(stSize); }
            catch (NotSupportedException) { return (int)ErrNo.Inval; }
            catch (IOException) { return (int)ErrNo.IO; }
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_filestat_set_times")]
        public static int FdFilestatSetTimesCore(State state, int fd,
            long atim, long mtim, int flags)
        {
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;

            var hostPath = state.PathMapper.MapToHostPath(fileDescriptor.Path);
            FileAttributes attr;
            try { attr = File.GetAttributes(hostPath); }
            catch (FileNotFoundException)      { return (int)ErrNo.NoEnt; }
            catch (DirectoryNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            catch (IOException)                { return (int)ErrNo.IO; }

            bool isDir = attr.HasFlag(FileAttributes.Directory);
            var f = (FstFlags)flags;
            if ((f & (FstFlags.ATim | FstFlags.ATimNow)) == (FstFlags.ATim | FstFlags.ATimNow))
                return (int)ErrNo.Inval;
            if ((f & (FstFlags.MTim | FstFlags.MTimNow)) == (FstFlags.MTim | FstFlags.MTimNow))
                return (int)ErrNo.Inval;

            // Only touch a timestamp when the corresponding flag is set —
            // rust/fd_filestat_set verifies atim is preserved when only
            // FSTFLAGS_MTIM is passed.
            bool setA = (f & (FstFlags.ATim | FstFlags.ATimNow)) != 0;
            bool setM = (f & (FstFlags.MTim | FstFlags.MTimNow)) != 0;
            DateTime newAtime = (f & FstFlags.ATim) != 0
                ? Clock.ToDateTimeUtc(atim) : DateTime.UtcNow;
            DateTime newMtime = (f & FstFlags.MTim) != 0
                ? Clock.ToDateTimeUtc(mtim) : DateTime.UtcNow;

            try
            {
                if (isDir)
                {
                    if (setM) Directory.SetLastWriteTimeUtc(hostPath, newMtime);
                    if (setA) Directory.SetLastAccessTimeUtc(hostPath, newAtime);
                }
                else
                {
                    if (setM) File.SetLastWriteTimeUtc(hostPath, newMtime);
                    if (setA) File.SetLastAccessTimeUtc(hostPath, newAtime);
                }
            }
            catch (IOException)                { return (int)ErrNo.IO; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_prestat_get")]
        public static int FdPrestatGetCore(WacsHostMemory mem, State state, int fd, int bufPtr)
        {
            if (!mem.Contains(bufPtr, PrestatSize)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;

            var name = fileDescriptor.Path;
            if (name.Length > 1 && name[0] == '/') name = name.Substring(1);
            var utf8Name = Encoding.UTF8.GetBytes(name);

            Prestat prestat;
            if (fileDescriptor.Type == Filetype.Directory)
            {
                prestat = new Prestat
                {
                    Tag = PrestatTag.Dir,
                    Dir = new PrestatDir { NameLen = (uint)utf8Name.Length },
                };
            }
            else
            {
                prestat = new Prestat
                {
                    Tag = PrestatTag.NotDir,
                    Dir = new PrestatDir { NameLen = 0 },
                };
            }
            mem.WriteStruct(bufPtr, prestat);
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "fd_prestat_dir_name")]
        public static int FdPrestatDirNameCore(WacsHostMemory mem, State state,
            int fd, int pathPtr, int pathLen)
        {
            if (!mem.Contains(pathPtr, pathLen)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var fileDescriptor))
                return (int)ErrNo.Badf;
            if (fileDescriptor.Type != Filetype.Directory) return (int)ErrNo.NotDir;

            var name = fileDescriptor.Path;
            if (name.Length > 1 && name[0] == '/') name = name.Substring(1);
            var utf8Name = Encoding.UTF8.GetBytes(name);
            // wasi-libc and wasmtime return ENAMETOOLONG (or EINVAL) for
            // a too-small buffer here; the wasi-testsuite (rust/dir_fd_op
            // _failures) accepts either. E2BIG is the "argument list too
            // long" code, semantically wrong.
            if (utf8Name.Length > pathLen) return (int)ErrNo.NameTooLong;

            mem.WriteUtf8String(pathPtr, name, nullTerminate: false);
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "path_filestat_get")]
        public static int PathFilestatGetCore(WacsHostMemory mem, State state,
            int fd, int flags, int pathPtr, int pathLen, int buf)
        {
            if (!mem.Contains(buf, FileStatSize)) return (int)ErrNo.Inval;
            if (!mem.Contains(pathPtr, pathLen)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var dirFileDescriptor))
                return (int)ErrNo.Badf;
            if (dirFileDescriptor.Type != Filetype.Directory && fd >= 3)
                return (int)ErrNo.NotDir;

            var pathStr = mem.ReadString(pathPtr, pathLen);
            var guestPath = Path.Combine(dirFileDescriptor.Path, pathStr);

            bool followSymlink = ((LookupFlags)flags & LookupFlags.SymlinkFollow) != 0;
            string hostPath;
            if (followSymlink)
                hostPath = state.PathMapper.MapToHostPath(guestPath);
            else
                hostPath = Path.Combine(state.PathMapper.MapToHostPath(dirFileDescriptor.Path), pathStr);

#if NET6_0_OR_GREATER
            if (!followSymlink)
            {
                FileSystemInfo? linkInfo;
                try
                {
                    linkInfo = File.Exists(hostPath) || Directory.Exists(hostPath)
                        ? (FileSystemInfo)(File.Exists(hostPath)
                            ? new FileInfo(hostPath)
                            : new DirectoryInfo(hostPath))
                        : null;
                }
                catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
                catch (IOException)                 { return (int)ErrNo.IO; }

                // Dangling symlinks: parent enumeration can find them
                // even when File.Exists/Directory.Exists return false (the
                // target is missing but the link entry is on disk).
                bool linkInfoIsSymlink = linkInfo != null
                    && !string.IsNullOrEmpty(linkInfo.LinkTarget);
                string? danglingLinkTarget = null;
                if (linkInfo == null)
                {
                    var parent = Path.GetDirectoryName(hostPath);
                    var name = Path.GetFileName(hostPath);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        foreach (var e in new DirectoryInfo(parent).EnumerateFileSystemInfos(name))
                        {
                            if ((e.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                danglingLinkTarget = e.LinkTarget ?? "";
                                break;
                            }
                        }
                    }
                }
                if (linkInfo == null && danglingLinkTarget == null) return (int)ErrNo.NoEnt;

                if (linkInfoIsSymlink || danglingLinkTarget != null)
                {
                    // POSIX lstat on a symlink: Size is the length of the
                    // link's target-path bytes, not the size of the target.
                    // FileInfo.Length would throw for symlink-to-directory
                    // / dangling symlink because the file isn't there.
                    var target = (linkInfoIsSymlink ? linkInfo!.LinkTarget : danglingLinkTarget) ?? "";
                    // Pseudo-inode: stable per-host-path hash, doesn't need
                    // to read the (possibly missing) target.
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    var hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hostPath));
                    var inode = BitConverter.ToUInt64(hashBytes, 0);
                    var symStat = new FileStat
                    {
                        Device = 0,
                        Ino = inode,
                        Mode = Filetype.SymbolicLink,
                        NLink = 1,
                        Size = (ulong)System.Text.Encoding.UTF8.GetByteCount(target),
                        ATim = linkInfo != null ? Clock.ToTimestamp(linkInfo.LastAccessTimeUtc) : 0,
                        MTim = linkInfo != null ? Clock.ToTimestamp(linkInfo.LastWriteTimeUtc) : 0,
                        CTim = linkInfo != null ? Clock.ToTimestamp(linkInfo.CreationTimeUtc) : 0,
                    };
                    mem.WriteStruct(buf, symStat);
                    return (int)ErrNo.Success;
                }
            }
#endif

            FileAttributes attr;
            try { attr = File.GetAttributes(hostPath); }
            catch (FileNotFoundException)      { return (int)ErrNo.NoEnt; }
            catch (DirectoryNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            catch (IOException)                { return (int)ErrNo.IO; }

            bool isDir = attr.HasFlag(FileAttributes.Directory);
            FileStat fileStat = isDir
                ? WasiFsHelpers.BuildFileStatForDirectory(new DirectoryInfo(hostPath))
                : WasiFsHelpers.BuildFileStatForFile(new FileInfo(hostPath));
            mem.WriteStruct(buf, fileStat);
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "path_filestat_set_times")]
        public static int PathFilestatSetTimesCore(WacsHostMemory mem, State state,
            int fd, int flags, int pathPtr, int pathLen,
            long stAtim, long stMtim, int fstFlags)
        {
            if (!mem.Contains(pathPtr, pathLen)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var dirFileDescriptor))
                return (int)ErrNo.Badf;
            if (dirFileDescriptor.Type != Filetype.Directory && fd >= 3)
                return (int)ErrNo.NotDir;

            var pathStr = mem.ReadString(pathPtr, pathLen);
            var guestPath = Path.Combine(dirFileDescriptor.Path, pathStr);
            var hostPath = state.PathMapper.MapToHostPath(guestPath);

            FileAttributes attr;
            try { attr = File.GetAttributes(hostPath); }
            catch (FileNotFoundException)      { return (int)ErrNo.NoEnt; }
            catch (DirectoryNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            catch (IOException)                { return (int)ErrNo.IO; }

            bool isDir = attr.HasFlag(FileAttributes.Directory);

            var f = (FstFlags)fstFlags;
            if ((f & (FstFlags.ATim | FstFlags.ATimNow)) == (FstFlags.ATim | FstFlags.ATimNow))
                return (int)ErrNo.Inval;
            if ((f & (FstFlags.MTim | FstFlags.MTimNow)) == (FstFlags.MTim | FstFlags.MTimNow))
                return (int)ErrNo.Inval;

            DateTime newAtime = DateTime.UtcNow, newMtime = DateTime.UtcNow;
            if ((f & FstFlags.ATim) != 0)         newAtime = Clock.ToDateTimeUtc(stAtim);
            else if ((f & FstFlags.ATimNow) != 0) newAtime = DateTime.UtcNow;
            if ((f & FstFlags.MTim) != 0)         newMtime = Clock.ToDateTimeUtc(stMtim);
            else if ((f & FstFlags.MTimNow) != 0) newMtime = DateTime.UtcNow;

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
            catch (IOException)                { return (int)ErrNo.IO; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            return (int)ErrNo.Success;
        }
    }
}
