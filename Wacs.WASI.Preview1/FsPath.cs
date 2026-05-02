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
using System.Linq;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.HostBindings;
using Wacs.WASI.Preview1.Internal;
using Wacs.WASI.Preview1.Types;
using ptr = System.UInt32;
using fd = System.UInt32;
using size = System.UInt32;

namespace Wacs.WASI.Preview1
{
    public partial class FileSystem
    {
        public delegate void PathOpenDelegate(ExecContext ctx,
            fd dirFd,
            LookupFlags dirFlags,
            ptr pathPtr,
            size pathLen,
            OFlags oFlags,
            Rights fsRightsBase,
            Rights fsRightsInheriting,
            FdFlags fsFlags,
            ptr fdPtr,
            out ErrNo result);

        // ============================================================
        // Interpreter wrappers
        // ============================================================

        public ErrNo PathCreateDirectory(ExecContext ctx, fd fd, ptr pathPtr, size pathLen)
            => (ErrNo)PathCreateDirectoryCore(Clock.WacsHost(ctx), _state, _config, (int)fd, (int)pathPtr, (int)pathLen);

        public ErrNo PathLink(ExecContext ctx, fd oldFd, LookupFlags oldFlags, ptr oldPathPtr, size oldPathLen, fd newFd, ptr newPathPtr, size newPathLen)
            => (ErrNo)PathLinkCore(Clock.WacsHost(ctx), _state, _config, (int)oldFd, (int)oldFlags,
                (int)oldPathPtr, (int)oldPathLen, (int)newFd, (int)newPathPtr, (int)newPathLen);

        public ErrNo NakedPathOpen(ExecContext ctx, fd dirFd, LookupFlags dirFlags,
            ptr pathPtr, size pathLen, OFlags oFlags, Rights fsRightsBase,
            Rights fsRightsInheriting, FdFlags fsFlags, ptr fdPtr)
        {
            PathOpen(ctx, dirFd, dirFlags, pathPtr, pathLen, oFlags, fsRightsBase, fsRightsInheriting, fsFlags, fdPtr, out var result);
            return result;
        }

        public void PathOpen(ExecContext ctx, fd dirFd, LookupFlags dirFlags,
            ptr pathPtr, size pathLen, OFlags oFlags, Rights fsRightsBase,
            Rights fsRightsInheriting, FdFlags fsFlags, ptr fdPtr, out ErrNo result)
        {
            result = (ErrNo)PathOpenCore(Clock.WacsHost(ctx), _state, _config,
                (int)dirFd, (int)dirFlags, (int)pathPtr, (int)pathLen,
                (int)oFlags, (long)fsRightsBase, (long)fsRightsInheriting,
                (int)fsFlags, (int)fdPtr);
        }

        public ErrNo PathReadlink(ExecContext ctx, fd dirFd, ptr pathPtr, size pathLen,
            ptr bufPtr, size bufLen, ptr bufUsedPtr)
            => (ErrNo)PathReadlinkCore(Clock.WacsHost(ctx), _state, (int)dirFd,
                (int)pathPtr, (int)pathLen, (int)bufPtr, (int)bufLen, (int)bufUsedPtr);

        public ErrNo PathRemoveDirectory(ExecContext ctx, fd fd, ptr pathPtr, size pathLen)
            => (ErrNo)PathRemoveDirectoryCore(Clock.WacsHost(ctx), _state, (int)fd, (int)pathPtr, (int)pathLen);

        public ErrNo PathRename(ExecContext ctx, fd oldFd, ptr oldPathPtr, size oldPathLen,
            fd newFd, ptr newPathPtr, size newPathLen)
            => (ErrNo)PathRenameCore(Clock.WacsHost(ctx), _state, (int)oldFd,
                (int)oldPathPtr, (int)oldPathLen, (int)newFd, (int)newPathPtr, (int)newPathLen);

        public ErrNo PathSymlink(ExecContext ctx, ptr oldPathPtr, size oldPathLen, fd fd,
            ptr newPathPtr, size newPathLen)
            => (ErrNo)PathSymlinkCore(Clock.WacsHost(ctx), _state, _config,
                (int)oldPathPtr, (int)oldPathLen, (int)fd, (int)newPathPtr, (int)newPathLen);

        public ErrNo PathUnlinkFile(ExecContext ctx, fd fd, ptr pathPtr, size pathLen)
            => (ErrNo)PathUnlinkFileCore(Clock.WacsHost(ctx), _state, (int)fd, (int)pathPtr, (int)pathLen);

        // ============================================================
        // AOT-friendly static entry points
        // ============================================================

        [WacsImport("wasi_snapshot_preview1", "path_create_directory")]
        public static int PathCreateDirectoryCore(WacsHostMemory mem, State state, WasiConfiguration config,
            int fd, int pathPtr, int pathLen)
        {
            if (!mem.Contains(pathPtr, pathLen)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var dirFd)) return (int)ErrNo.NoEnt;
            if (!dirFd.Rights.HasFlag(Rights.PATH_CREATE_DIRECTORY) || (dirFd.Access & FileAccess.Write) == 0)
                return (int)ErrNo.Acces;

            try
            {
                var pathToCreate = mem.ReadString(pathPtr, pathLen);
                var guestDirPath = dirFd.Path;
                var hostDirPath = state.PathMapper.MapToHostPath(guestDirPath);
                var newGuestPath = Path.Combine(guestDirPath, pathToCreate);
                var newHostPath = Path.Combine(hostDirPath, pathToCreate);

                Directory.CreateDirectory(newHostPath);

                var rights = FileDescriptor.ComputeFileRights(
                    new DirectoryInfo(newHostPath),
                    Filetype.Directory,
                    dirFd.Rights.ToFileAccess(),
                    Stream.Null,
                    dirFd.Rights.HasFlag(Rights.PATH_CREATE_FILE),
                    dirFd.Rights.HasFlag(Rights.PATH_UNLINK_FILE)
                ) & dirFd.Rights;

                WasiFsHelpers.BindDir(state, config, newHostPath, newGuestPath,
                    dirFd.Access, isPreopened: true, rights, dirFd.Rights);
            }
            catch (DirectoryNotFoundException) { return (int)ErrNo.NoSys; }
            catch (IOException) { return (int)ErrNo.Exist; }
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "path_link")]
        public static int PathLinkCore(WacsHostMemory mem, State state, WasiConfiguration config,
            int oldFd, int oldFlags, int oldPathPtr, int oldPathLen,
            int newFd, int newPathPtr, int newPathLen)
        {
            if (!config.AllowHardLinks) return (int)ErrNo.NotSup;
            if (!mem.Contains(oldPathPtr, oldPathLen) || !mem.Contains(newPathPtr, newPathLen))
                return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)oldFd, out var oldDir)) return (int)ErrNo.NoEnt;
            if ((oldDir.Access & FileAccess.Read) == 0) return (int)ErrNo.Acces;
            if (!WasiFsHelpers.TryGetFd(state, (uint)newFd, out var newDir)) return (int)ErrNo.NoEnt;
            if ((newDir.Access & FileAccess.Write) == 0) return (int)ErrNo.Acces;

            try
            {
                var oldRel = mem.ReadString(oldPathPtr, oldPathLen);
                var newRel = mem.ReadString(newPathPtr, newPathLen);

                var oldGuest = Path.Combine(oldDir.Path, oldRel);
                var newGuest = Path.Combine(newDir.Path, newRel);
                var oldHost = state.PathMapper.MapToHostPath(oldGuest);
                var newHost = state.PathMapper.MapToHostPath(newGuest);

                if (System.Runtime.InteropServices.RuntimeInformation
                        .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    if (!CreateHardLinkW(newHost, oldHost, IntPtr.Zero))
                    {
                        int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                        return (int)(err == 183 ? ErrNo.Exist : ErrNo.IO);
                    }
                }
                else
                {
                    int rc = link(oldHost, newHost);
                    if (rc != 0)
                    {
                        int errno = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                        return (int)(errno switch
                        {
                            1  => ErrNo.Perm,   // EPERM — directory hard-link, etc.
                            2  => ErrNo.NoEnt,  // ENOENT
                            13 => ErrNo.Acces,  // EACCES
                            17 => ErrNo.Exist,  // EEXIST
                            _  => ErrNo.IO,
                        });
                    }
                }
                return (int)ErrNo.Success;
            }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            catch (IOException) { return (int)ErrNo.IO; }
        }

        // .NET has no portable HardLink helper, so reach for the OS calls.
        // Both APIs use last-error semantics; swap on platform.
        [System.Runtime.InteropServices.DllImport("kernel32.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode,
            SetLastError = true, EntryPoint = "CreateHardLinkW")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName,
            IntPtr lpSecurityAttributes);

        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
        private static extern int link(string oldpath, string newpath);

        [WacsImport("wasi_snapshot_preview1", "path_open")]
        public static int PathOpenCore(WacsHostMemory mem, State state, WasiConfiguration config,
            int dirFd, int dirFlags, int pathPtr, int pathLen,
            int oFlags, long fsRightsBase, long fsRightsInheriting,
            int fsFlags, int fdPtr)
        {
            if (!mem.Contains(pathPtr, pathLen)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)dirFd, out var dirFileDescriptor))
                return (int)ErrNo.Badf;
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0) return (int)ErrNo.Acces;

            try
            {
                var pathToOpen = mem.ReadString(pathPtr, pathLen);
                var guestDirPath = dirFileDescriptor.Path;
                var hostDirPath = state.PathMapper.MapToHostPath(guestDirPath);
                var guestPath = Path.Combine(guestDirPath, pathToOpen);
                var hostPath = Path.Combine(hostDirPath, pathToOpen);

                var oFlagsTyped = (OFlags)oFlags;
                var fsFlagsTyped = (FdFlags)fsFlags;
                var fsRightsInheritingTyped = (Rights)fsRightsInheriting;

                // @Spec wasi: path_open with LOOKUPFLAGS_SYMLINK_FOLLOW
                // unset must not chase the final-component symlink. Per the
                // wasi-testsuite (rust/dangling_symlink, nofollow_errors,
                // symlink_loop), the rejection is ERRNO_LOOP — even when the
                // symlink dangles or self-loops. The check happens before
                // GetAttributes so a dangling target doesn't surface as
                // FileNotFoundException → NoEnt. "." / ".." are directory
                // traversals, never symlinks themselves; skip them so a
                // scratch dir under a symlinked tree (e.g. /tmp on macOS)
                // doesn't trip the check.
                var dirFlagsTyped = (LookupFlags)dirFlags;
                if ((dirFlagsTyped & LookupFlags.SymlinkFollow) == 0
                    && pathToOpen != "." && pathToOpen != ".."
                    && VirtualPathMapper.IsSymlink(hostPath))
                {
                    return (int)ErrNo.Loop;
                }

                FileAttributes attr;
                try { attr = File.GetAttributes(hostPath); }
                catch (FileNotFoundException)
                {
                    if (oFlagsTyped.HasFlag(OFlags.Creat))
                    {
                        try
                        {
                            var fileStream = new FileStream(hostPath, FileMode.CreateNew,
                                dirFileDescriptor.Access, FileShare.Read);
                            uint newFd = WasiFsHelpers.BindFile(state, guestPath, fileStream,
                                dirFileDescriptor.Access, dirFileDescriptor.Rights, fsRightsInheritingTyped);
                            if (state.FileDescriptors.TryGetValue(newFd, out var openedFd))
                                openedFd.Flags = fsFlagsTyped;
                            mem.WriteInt32(fdPtr, (int)newFd);
                            if (oFlagsTyped.HasFlag(OFlags.Trunc) && (dirFileDescriptor.Access & FileAccess.Write) != 0)
                                fileStream.SetLength(0);
                            return (int)ErrNo.Success;
                        }
                        catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
                        catch (IOException)                 { return (int)ErrNo.IO; }
                    }
                    return (int)ErrNo.NoEnt;
                }
                catch (DirectoryNotFoundException) { return (int)ErrNo.NoEnt; }
                catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
                catch (IOException)                { return (int)ErrNo.IO; }

                bool isDirectory = attr.HasFlag(FileAttributes.Directory);
                bool isReadOnly = attr.HasFlag(FileAttributes.ReadOnly);

                // OFLAGS_DIRECTORY against a non-directory target ⇒ ENOTDIR
                // (per spec; rust/nofollow_errors verifies this after replacing
                // the symlink target with a file).
                if (oFlagsTyped.HasFlag(OFlags.Directory) && !isDirectory)
                    return (int)ErrNo.NotDir;

                if (isDirectory)
                {
                    if (oFlagsTyped.HasFlag(OFlags.Creat) && oFlagsTyped.HasFlag(OFlags.Excl))
                        return (int)ErrNo.Exist;
                    var existing = state.FileDescriptors.Values
                        .FirstOrDefault(d => d.Path == guestPath);
                    if (existing != null)
                    {
                        mem.WriteInt32(fdPtr, (int)existing.Fd);
                        return (int)ErrNo.Success;
                    }
                    uint newFd = WasiFsHelpers.BindDir(state, config, hostPath, guestPath,
                        dirFileDescriptor.Access, false,
                        dirFileDescriptor.Rights, fsRightsInheritingTyped);
                    mem.WriteInt32(fdPtr, (int)newFd);
                }
                else
                {
                    if (oFlagsTyped.HasFlag(OFlags.Creat) && oFlagsTyped.HasFlag(OFlags.Excl))
                        return (int)ErrNo.Exist;
                    var existing = state.FileDescriptors.Values
                        .FirstOrDefault(d => d.Path == guestPath);
                    if (existing != null)
                    {
                        mem.WriteInt32(fdPtr, (int)existing.Fd);
                        return (int)ErrNo.Success;
                    }

                    var fileAccess = dirFileDescriptor.Access;
                    if (isReadOnly) fileAccess &= ~FileAccess.Write;

                    try
                    {
                        var fileMode = oFlagsTyped.HasFlag(OFlags.Creat)
                            ? FileMode.OpenOrCreate
                            : FileMode.Open;
                        var fileStream = new FileStream(hostPath, fileMode, fileAccess, FileShare.Read);
                        uint newFd = WasiFsHelpers.BindFile(state, guestPath, fileStream,
                            fileAccess, dirFileDescriptor.Rights, fsRightsInheritingTyped);
                        if (state.FileDescriptors.TryGetValue(newFd, out var openedFd2))
                            openedFd2.Flags = fsFlagsTyped;
                        mem.WriteInt32(fdPtr, (int)newFd);
                        if (oFlagsTyped.HasFlag(OFlags.Trunc) && (fileAccess & FileAccess.Write) != 0)
                            fileStream.SetLength(0);
                    }
                    catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
                    catch (IOException)                 { return (int)ErrNo.IO; }
                }
                return (int)ErrNo.Success;
            }
            catch (Exception) { return (int)ErrNo.NoSys; }
        }

        [WacsImport("wasi_snapshot_preview1", "path_readlink")]
        public static int PathReadlinkCore(WacsHostMemory mem, State state,
            int dirFd, int pathPtr, int pathLen, int bufPtr, int bufLen, int bufUsedPtr)
        {
            if (!mem.Contains(pathPtr, pathLen) || !mem.Contains(bufPtr, bufLen))
                return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)dirFd, out var dirFileDescriptor))
                return (int)ErrNo.NoEnt;
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0) return (int)ErrNo.Acces;

            try
            {
                var pathToRead = mem.ReadString(pathPtr, pathLen);
                var guestDirPath = dirFileDescriptor.Path;
                var hostDirPath = state.PathMapper.MapToHostPath(guestDirPath);
                var guestPath = Path.Combine(guestDirPath, pathToRead);
                var hostPath = Path.Combine(hostDirPath, pathToRead);

#if NET6_0_OR_GREATER
                var fileInfo = new FileInfo(hostPath);
                if (fileInfo.LinkTarget == null) return (int)ErrNo.Inval;
                var linkTarget = fileInfo.LinkTarget;
                if (string.IsNullOrEmpty(linkTarget)) return (int)ErrNo.Inval;

                try
                {
                    var dirPart = Path.GetDirectoryName(hostPath) ?? throw new IOException("Invalid path");
                    var newPath = Path.Combine(dirPart, linkTarget);
                    VirtualPathMapper.ResolveSymbolicLinks(newPath, hostDirPath);
                    // path_readlink: bufused is the byte length of the link
                    // target itself, no NUL terminator (per spec § path_readlink
                    // and rust/readlink test). Cap by bufLen — caller may pass
                    // a buffer smaller than the target, in which case we
                    // truncate and return the truncated length (caller can
                    // detect by comparing returned length to bufLen).
                    int byteCount = System.Text.Encoding.UTF8.GetByteCount(linkTarget);
                    int writeLen = System.Math.Min(byteCount, bufLen);
                    var truncated = byteCount > bufLen
                        ? System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(linkTarget), 0, writeLen)
                        : linkTarget;
                    int strLen = mem.WriteUtf8String(bufPtr, truncated, false);
                    mem.WriteInt32(bufUsedPtr, strLen);
                }
                catch (SandboxError sandboxError) { return (int)sandboxError.ErrorNumber; }
#else
                return (int)ErrNo.NoSys;
#endif
            }
            catch (FileNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            catch (IOException) { return (int)ErrNo.NoSys; }
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "path_remove_directory")]
        public static int PathRemoveDirectoryCore(WacsHostMemory mem, State state,
            int fd, int pathPtr, int pathLen)
        {
            if (!mem.Contains(pathPtr, pathLen)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var dirFileDescriptor))
                return (int)ErrNo.NoEnt;
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0 ||
                (dirFileDescriptor.Access & FileAccess.Write) == 0)
                return (int)ErrNo.Acces;

            try
            {
                var pathToRemove = mem.ReadString(pathPtr, pathLen);
                var guestDirPath = dirFileDescriptor.Path;
                var hostDirPath = state.PathMapper.MapToHostPath(guestDirPath);
                var guestPath = Path.Combine(guestDirPath, pathToRemove);
                var hostPath = Path.Combine(hostDirPath, pathToRemove);

                Directory.Delete(hostPath, false);
                WasiFsHelpers.UnbindDir(state, guestPath);
            }
            catch (DirectoryNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (IOException) { return (int)ErrNo.NotEmpty; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "path_rename")]
        public static int PathRenameCore(WacsHostMemory mem, State state,
            int oldFd, int oldPathPtr, int oldPathLen,
            int newFd, int newPathPtr, int newPathLen)
        {
            if (!mem.Contains(oldPathPtr, oldPathLen) || !mem.Contains(newPathPtr, newPathLen))
                return (int)ErrNo.Inval;

            try
            {
                if (!WasiFsHelpers.TryGetFd(state, (uint)oldFd, out var oldDirFileDescriptor))
                    return (int)ErrNo.NoEnt;
                if (!WasiFsHelpers.TryGetFd(state, (uint)newFd, out var newDirFileDescriptor))
                    return (int)ErrNo.NoEnt;

                var oldPathToRename = mem.ReadString(oldPathPtr, oldPathLen);
                var newPathForRename = mem.ReadString(newPathPtr, newPathLen);

                var oldGuestPath = Path.Combine(oldDirFileDescriptor.Path, oldPathToRename);
                var newGuestPath = Path.Combine(newDirFileDescriptor.Path, newPathForRename);
                var oldHostDirPath = state.PathMapper.MapToHostPath(oldDirFileDescriptor.Path);
                var newHostDirPath = state.PathMapper.MapToHostPath(newDirFileDescriptor.Path);
                var oldHostPath = Path.Combine(oldHostDirPath, oldPathToRename);
                var newHostPath = Path.Combine(newHostDirPath, newPathForRename);

                if (File.Exists(oldHostPath))
                {
                    if (!oldDirFileDescriptor.Rights.HasFlag(Rights.PATH_UNLINK_FILE) ||
                        (oldDirFileDescriptor.Access & FileAccess.Read) == 0) return (int)ErrNo.Acces;
                    if (!newDirFileDescriptor.Rights.HasFlag(Rights.PATH_CREATE_FILE) ||
                        (newDirFileDescriptor.Access & FileAccess.Write) == 0) return (int)ErrNo.Acces;
                    File.Move(oldHostPath, newHostPath);
                }
                else if (Directory.Exists(oldHostPath))
                {
                    if (!oldDirFileDescriptor.Rights.HasFlag(Rights.PATH_REMOVE_DIRECTORY) ||
                        (oldDirFileDescriptor.Access & FileAccess.Read) == 0) return (int)ErrNo.Acces;
                    if (!newDirFileDescriptor.Rights.HasFlag(Rights.PATH_CREATE_DIRECTORY) ||
                        (newDirFileDescriptor.Access & FileAccess.Write) == 0) return (int)ErrNo.Acces;
                    Directory.Move(oldHostPath, newHostPath);
                }
                else
                {
                    return (int)ErrNo.NoEnt;
                }

                state.PathMapper.MoveHostPath(oldHostPath, newHostPath);
            }
            catch (FileNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (IOException) { return (int)ErrNo.NoSys; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            return (int)ErrNo.Success;
        }

        [WacsImport("wasi_snapshot_preview1", "path_symlink")]
        public static int PathSymlinkCore(WacsHostMemory mem, State state, WasiConfiguration config,
            int oldPathPtr, int oldPathLen, int fd, int newPathPtr, int newPathLen)
        {
            if (!config.AllowSymbolicLinks) return (int)ErrNo.NotSup;
#if NET6_0_OR_GREATER
            if (!mem.Contains(oldPathPtr, oldPathLen) || !mem.Contains(newPathPtr, newPathLen))
                return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var dirFileDescriptor))
                return (int)ErrNo.NoEnt;
            if ((dirFileDescriptor.Access & FileAccess.Write) == 0) return (int)ErrNo.Acces;
            if (dirFileDescriptor.Type != Filetype.Directory) return (int)ErrNo.NotDir;

            try
            {
                var oldPath = mem.ReadString(oldPathPtr, oldPathLen);
                var newPath = mem.ReadString(newPathPtr, newPathLen);
                // WASI sandboxes reject absolute symlink targets — they
                // would let the guest escape the sandbox by linking to
                // arbitrary host paths. Mirror wasmtime/wasi-libc which
                // return EPERM for an absolute target. (rust/symlink_create
                // create_symlink_to_root pins this.)
                if (oldPath.Length > 0 && (oldPath[0] == '/' || oldPath[0] == '\\'))
                    return (int)ErrNo.Perm;
                var guestPath = Path.Combine(dirFileDescriptor.Path, newPath);
                var newHostPath = state.PathMapper.MapToHostPath(guestPath);
                File.CreateSymbolicLink(newHostPath, oldPath);
                return (int)ErrNo.Success;
            }
            catch (DirectoryNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (FileNotFoundException)      { return (int)ErrNo.NoEnt; }
            catch (IOException ioe) when (ioe.HResult == unchecked((int)0x80070050)) { return (int)ErrNo.Exist; }
            catch (IOException)                { return (int)ErrNo.IO; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
#else
            return (int)ErrNo.NotSup;
#endif
        }

        [WacsImport("wasi_snapshot_preview1", "path_unlink_file")]
        public static int PathUnlinkFileCore(WacsHostMemory mem, State state,
            int fd, int pathPtr, int pathLen)
        {
            if (!mem.Contains(pathPtr, pathLen)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var dirFileDescriptor))
                return (int)ErrNo.NoEnt;
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0 ||
                (dirFileDescriptor.Access & FileAccess.Write) == 0)
                return (int)ErrNo.Acces;

            try
            {
                var pathToUnlink = mem.ReadString(pathPtr, pathLen);
                var guestDirPath = dirFileDescriptor.Path;
                var hostDirPath = state.PathMapper.MapToHostPath(guestDirPath);
                var guestPath = Path.Combine(guestDirPath, pathToUnlink);
                var hostPath = Path.Combine(hostDirPath, pathToUnlink);

                // path_unlink_file removes the entry, never the target.
                // If hostPath is a symlink — even one pointing at a
                // directory or a dangling target — File.Delete unlinks
                // the link itself (matching POSIX unlink(2)). The IsDir
                // check has to skip the symlink case explicitly because
                // Directory.Exists follows links.
                bool isSymlink = VirtualPathMapper.IsSymlink(hostPath);
                if (!isSymlink && Directory.Exists(hostPath)) return (int)ErrNo.IsDir;
                File.Delete(hostPath);
                WasiFsHelpers.UnbindFile(state, guestPath);
            }
            catch (FileNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            catch (IOException) { return (int)ErrNo.NoSys; }
            return (int)ErrNo.Success;
        }
    }
}
