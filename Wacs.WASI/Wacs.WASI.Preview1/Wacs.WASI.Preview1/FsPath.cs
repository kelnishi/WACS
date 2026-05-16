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

                // Carry the parent's INHERITED rights (not its own base
                // rights) into the new dir so child files opened through
                // it can reach FD_WRITE / FD_SEEK / etc. (Phase 4.7
                // own-vs-inherited split).
                WasiFsHelpers.BindDir(state, config, newHostPath, newGuestPath,
                    dirFd.Access, isPreopened: true,
                    dirFd.InheritedRights, dirFd.InheritedRights);
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

                // Trailing slash on dest (newRel) is malformed for a hard
                // link target — POSIX rule: the entry must not exist, but
                // a trailing slash implies it must be a directory, which
                // a fresh entry can't be. rust/path_link verifies NOENT.
                bool destTrailingSlash = newRel.Length > 0
                    && (newRel[newRel.Length - 1] == '/'
                        || newRel[newRel.Length - 1] == '\\');
                if (destTrailingSlash) return (int)ErrNo.NoEnt;

                // Resolve the parent dirs (which may legitimately be
                // symlinks) but append the final component raw — POSIX
                // link(2) operates on the link itself (rust/path_link
                // covers this with a dangling-symlink and a self-loop
                // source). MapToHostPath would chase the source link,
                // hand link(2) the resolved (possibly missing) target,
                // and break the test.
                var oldHostDir = state.PathMapper.MapToHostPath(oldDir.Path);
                var newHostDir = state.PathMapper.MapToHostPath(newDir.Path);
                var oldHost = Path.Combine(oldHostDir, oldRel);
                var newHost = Path.Combine(newHostDir, newRel);

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
                    // wasi-libc / wasmtime reject path_link with
                    // LOOKUPFLAGS_SYMLINK_FOLLOW set — hardlinks operate
                    // on inodes, the follow bit is meaningless and the
                    // testsuite (rust/path_link line 185) requires INVAL.
                    if (((LookupFlags)oldFlags & LookupFlags.SymlinkFollow) != 0)
                        return (int)ErrNo.Inval;
                    bool follow = false;
                    int atFdCwd = System.Runtime.InteropServices.RuntimeInformation
                        .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                        ? -2 : -100;
                    int atFlags = follow ? 0x400 /* AT_SYMLINK_FOLLOW */ : 0;
                    int rc;
                    try { rc = linkat(atFdCwd, oldHost, atFdCwd, newHost, atFlags); }
                    catch (EntryPointNotFoundException)
                    {
                        // Older libc — fall back to plain link(). Loses
                        // the no-follow guarantee but covers the common case.
                        rc = link(oldHost, newHost);
                    }
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

        // POSIX linkat with AT_SYMLINK_NOFOLLOW (= 0x100 on Linux/macOS)
        // creates a hard link to the symlink itself rather than its target.
        // wasi path_link with LOOKUPFLAGS_SYMLINK_FOLLOW unset requires
        // this behavior — bare link(2) on Linux follows the source link
        // by default.
        // AT_FDCWD is -100 on Linux, -2 on macOS; we always pass full
        // host paths so the dirfd is irrelevant — pick AT_FDCWD per
        // platform to keep things explicit.
        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "linkat", SetLastError = true)]
        private static extern int linkat(int olddirfd, string oldpath,
            int newdirfd, string newpath, int flags);

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
            // path_open requires dirFd to actually be a directory — opening
            // through a regular file fd is rejected with NOTDIR (per
            // rust/path_open_dirfd_not_dir).
            if (dirFileDescriptor.Type != Filetype.Directory) return (int)ErrNo.NotDir;

            try
            {
                var pathToOpen = mem.ReadString(pathPtr, pathLen);
                // Absolute paths in path_open would let the guest escape
                // the sandbox by reaching outside the dirfd. wasi-libc /
                // wasmtime treat this as PERM (rust/interesting_paths
                // line 16 verifies this with "/dir/nested/file").
                if (pathToOpen.Length > 0
                    && (pathToOpen[0] == '/' || pathToOpen[0] == '\\'))
                    return (int)ErrNo.Perm;
                // Embedded NUL byte ⇒ INVAL (rust/interesting_paths line
                // 41). Real filesystems reject the syscall before any
                // resolution, since their string layer is NUL-terminated.
                if (pathToOpen.IndexOf('\0') >= 0)
                    return (int)ErrNo.Inval;
                var guestDirPath = dirFileDescriptor.Path;
                var hostDirPath = state.PathMapper.MapToHostPath(guestDirPath);
                var guestPath = Path.Combine(guestDirPath, pathToOpen);
                var hostPath = Path.Combine(hostDirPath, pathToOpen);

                var oFlagsTyped = (OFlags)oFlags;
                var fsFlagsTyped = (FdFlags)fsFlags;
                var fsRightsBaseTyped = (Rights)fsRightsBase;
                var fsRightsInheritingTyped = (Rights)fsRightsInheriting;
                // Per WASI spec: result fd's base rights = fsRightsBase ∩
                // parent's inheriting rights. Strict intersection; the
                // wasi-testsuite (rust/truncation_rights) requires that
                // passing fsRightsBase=0 yields zero rights so subsequent
                // OFLAGS_TRUNC fails with NOTCAPABLE — not "fall back to
                // the parent's full inheritable cap".
                var requestedBase = fsRightsBaseTyped & dirFileDescriptor.InheritedRights;

                // OFLAGS_TRUNC requires PATH_FILESTAT_SET_SIZE on the
                // parent directory (rust/truncation_rights line 67). The
                // base-rights intersection above caps requestedBase, but
                // dropping the right from the parent should reject the
                // open before any file is opened.
                if (oFlagsTyped.HasFlag(OFlags.Trunc)
                    && (dirFileDescriptor.Rights & Rights.PATH_FILESTAT_SET_SIZE) == 0)
                    return (int)ErrNo.Perm;

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
                                dirFileDescriptor.Access,
                                requestedBase,
                                fsRightsInheritingTyped);
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
                // the symlink target with a file). Same rule for trailing-slash
                // path targets (rust/interesting_paths line 49) — the slash
                // implicitly demands a directory.
                bool pathHasTrailingSlash = pathToOpen.Length > 0
                    && (pathToOpen[pathToOpen.Length - 1] == '/'
                        || pathToOpen[pathToOpen.Length - 1] == '\\');
                if ((oFlagsTyped.HasFlag(OFlags.Directory) || pathHasTrailingSlash)
                    && !isDirectory)
                    return (int)ErrNo.NotDir;

                // OFLAGS_DIRECTORY + explicit FD_WRITE base right ⇒ ISDIR
                // (rust/path_open_preopen line 106). Now that preopen Rights
                // omit FD_WRITE (Phase 4.7 own/inherited split), the
                // open_scratch_directory pattern doesn't trigger this — only
                // an explicit RIGHTS_FD_WRITE in fsRightsBase does.
                if (oFlagsTyped.HasFlag(OFlags.Directory) && isDirectory
                    && (fsRightsBaseTyped & Rights.FD_WRITE) != 0)
                    return (int)ErrNo.IsDir;


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
                        requestedBase, fsRightsInheritingTyped);
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
                            fileAccess,
                            requestedBase,
                            fsRightsInheritingTyped);
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
                return (int)ErrNo.Badf;
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
                return (int)ErrNo.Success;
#else
                return (int)ErrNo.NoSys;
#endif
            }
            catch (FileNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            catch (IOException) { return (int)ErrNo.NoSys; }
        }

        [WacsImport("wasi_snapshot_preview1", "path_remove_directory")]
        public static int PathRemoveDirectoryCore(WacsHostMemory mem, State state,
            int fd, int pathPtr, int pathLen)
        {
            if (!mem.Contains(pathPtr, pathLen)) return (int)ErrNo.Inval;
            if (!WasiFsHelpers.TryGetFd(state, (uint)fd, out var dirFileDescriptor))
                return (int)ErrNo.Badf;
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

                // Trailing-slash semantics (rust/remove_directory_trailing_slashes):
                // remove_directory on "file" or "file/" must trap a non-
                // directory target with ENOTDIR before Directory.Delete
                // surfaces a more generic IO error.
                if (!Directory.Exists(hostPath))
                    return File.Exists(hostPath) ? (int)ErrNo.NotDir : (int)ErrNo.NoEnt;
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
                    return (int)ErrNo.Badf;
                if (!WasiFsHelpers.TryGetFd(state, (uint)newFd, out var newDirFileDescriptor))
                    return (int)ErrNo.Badf;

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
                return (int)ErrNo.Badf;
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
                // Trailing slash on the link destination (newPath) follows
                // the rust/path_symlink_trailing_slashes matrix:
                //   - target doesn't exist ⇒ ENOENT (slash implies dir but
                //     dir doesn't exist there)
                //   - target is a file     ⇒ ENOTDIR
                //   - target is a directory ⇒ EEXIST (handled by the
                //     existing IOException 0x80070050 branch below).
                bool destTrailingSlash = newPath.Length > 0
                    && (newPath[newPath.Length - 1] == '/'
                        || newPath[newPath.Length - 1] == '\\');
                var guestPath = Path.Combine(dirFileDescriptor.Path, newPath);
                var newHostPath = state.PathMapper.MapToHostPath(guestPath);
                if (destTrailingSlash)
                {
                    if (Directory.Exists(newHostPath)) return (int)ErrNo.Exist;
                    if (File.Exists(newHostPath))     return (int)ErrNo.NotDir;
                    return (int)ErrNo.NoEnt;
                }
                // Pre-check existence: File.CreateSymbolicLink throws an
                // IOException whose HResult is platform-specific (0x80070050
                // on Windows, EEXIST on Unix). Surfacing EEXIST uniformly
                // here is simpler than chasing the HResult per-platform.
                if (Directory.Exists(newHostPath) || File.Exists(newHostPath)
                    || VirtualPathMapper.IsSymlink(newHostPath))
                    return (int)ErrNo.Exist;
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
                return (int)ErrNo.Badf;
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
                bool hasTrailingSlash = pathToUnlink.Length > 0
                    && (pathToUnlink[pathToUnlink.Length - 1] == '/'
                        || pathToUnlink[pathToUnlink.Length - 1] == '\\');
                // POSIX trailing-slash semantics: "file/" implies file
                // must be a directory. unlink_file on a non-directory
                // with trailing slash ⇒ ENOTDIR. (rust/unlink_file_
                // trailing_slashes verifies this; macOS in particular
                // wouldn't surface it without an explicit check.)
                if (hasTrailingSlash && !isSymlink && !Directory.Exists(hostPath))
                    return File.Exists(hostPath) ? (int)ErrNo.NotDir : (int)ErrNo.NoEnt;
                if (!isSymlink && Directory.Exists(hostPath)) return (int)ErrNo.IsDir;
                File.Delete(hostPath);
                // POSIX: existing open fds against an unlinked file
                // remain valid (the inode persists until last close).
                // Don't UnbindFile here — let fd_close finalize the
                // descriptor (rust/path_link line 102 closes a link_fd
                // after path_unlink_file).
            }
            catch (FileNotFoundException) { return (int)ErrNo.NoEnt; }
            catch (UnauthorizedAccessException) { return (int)ErrNo.Acces; }
            catch (IOException) { return (int)ErrNo.NoSys; }
            return (int)ErrNo.Success;
        }
    }
}
