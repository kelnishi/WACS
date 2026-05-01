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
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
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

        /// <summary>
        /// Create a directory.
        /// This method is analogous to the POSIX <c>mkdirat</c> function.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">The file descriptor of the directory in which to create the new directory.</param>
        /// <param name="pathPtr">Pointer to the path of the new directory to create.</param>
        /// <param name="pathLen">Length of the path.</param>
        /// <returns>An error code indicating the result of the operation.</returns>
        public ErrNo PathCreateDirectory(ExecContext ctx, fd fd, ptr pathPtr, size pathLen)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)pathPtr, (int)pathLen))
                return ErrNo.Inval;

            // Get the parent directory's FD
            if (!GetFd(fd, out var dirFd))
                return ErrNo.NoEnt;

            // Must have PATH_CREATE_DIRECTORY + write permission
            if (!dirFd.Rights.HasFlag(Rights.PATH_CREATE_DIRECTORY) ||
                (dirFd.Access & FileAccess.Write) == 0)
            {
                return ErrNo.Acces;
            }

            try
            {
                var pathToCreate = mem.ReadString(pathPtr, pathLen);

                // Combine the (guest) path with the FD's guest path
                var guestDirPath = dirFd.Path;
                var hostDirPath = _state.PathMapper.MapToHostPath(guestDirPath);

                var newGuestPath = Path.Combine(guestDirPath, pathToCreate);
                var newHostPath = Path.Combine(hostDirPath, pathToCreate);

                Directory.CreateDirectory(newHostPath);

                // Compute rights for newly created directory
                // We treat it as a directory, so we might use DirectoryInfo
                var rights = FileDescriptor.ComputeFileRights(
                    new DirectoryInfo(newHostPath),
                    Filetype.Directory,
                    dirFd.Rights.ToFileAccess(), // Convert from existing rights
                    Stream.Null,
                    dirFd.Rights.HasFlag(Rights.PATH_CREATE_FILE),
                    dirFd.Rights.HasFlag(Rights.PATH_UNLINK_FILE)
                ) & dirFd.Rights;

                // Bind this new directory into our FD table.
                fd newFd = BindDir(
                    newHostPath,      // hostDir
                    newGuestPath,     // guestDir
                    dirFd.Access,
                    true,             // isPreopened?
                    rights,
                    dirFd.Rights
                );
            }
            catch (IOException ex) when (ex is DirectoryNotFoundException)
            {
                // Could not find the parent directory
                return ErrNo.NoSys;
            }
            catch (IOException)
            {
                // In typical POSIX semantics, if the directory already exists, we'd do EEXIST.
                // But here you may return a more general code or do a file-exists check first.
                return ErrNo.Exist;
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// Create a hard link.
        /// This method is analogous to the POSIX <c>linkat</c> function.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="oldFd">File descriptor of the existing file.</param>
        /// <param name="oldFlags">Flags that determine how the path is resolved.</param>
        /// <param name="oldPathPtr">Pointer to the source path from which to link.</param>
        /// <param name="oldPathLen">Length of the source path.</param>
        /// <param name="newFd">File descriptor of the directory in which to create the new link.</param>
        /// <param name="newPathPtr">Pointer to the destination path at which to create the hard link.</param>
        /// <param name="newPathLen">Length of the destination path.</param>
        /// <returns>An error code indicating the result of the operation.</returns>
        public ErrNo PathLink(ExecContext ctx, fd oldFd, LookupFlags oldFlags, ptr oldPathPtr, size oldPathLen, fd newFd, ptr newPathPtr, size newPathLen)
        {
            if (!_config.AllowHardLinks)
                return ErrNo.NotSup;

            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)oldPathPtr, (int)oldPathLen) ||
                !mem.Contains((int)newPathPtr, (int)newPathLen))
                return ErrNo.Inval;

            if (!GetFd(oldFd, out var oldDir)) return ErrNo.NoEnt;
            if ((oldDir.Access & FileAccess.Read) == 0) return ErrNo.Acces;
            if (!GetFd(newFd, out var newDir)) return ErrNo.NoEnt;
            if ((newDir.Access & FileAccess.Write) == 0) return ErrNo.Acces;

            try
            {
                var oldRel = mem.ReadString(oldPathPtr, oldPathLen);
                var newRel = mem.ReadString(newPathPtr, newPathLen);

                var oldGuest = Path.Combine(oldDir.Path, oldRel);
                var newGuest = Path.Combine(newDir.Path, newRel);
                var oldHost = _state.PathMapper.MapToHostPath(oldGuest);
                var newHost = _state.PathMapper.MapToHostPath(newGuest);

                if (System.Runtime.InteropServices.RuntimeInformation
                        .IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    if (!CreateHardLinkW(newHost, oldHost, IntPtr.Zero))
                    {
                        int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                        // ERROR_ALREADY_EXISTS = 183
                        return err == 183 ? ErrNo.Exist : ErrNo.IO;
                    }
                }
                else
                {
                    int rc = link(oldHost, newHost);
                    if (rc != 0)
                    {
                        int errno = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                        return errno switch
                        {
                            17 => ErrNo.Exist,   // EEXIST
                            2  => ErrNo.NoEnt,   // ENOENT
                            13 => ErrNo.Acces,   // EACCES
                            _  => ErrNo.IO,
                        };
                    }
                }
                return ErrNo.Success;
            }
            catch (UnauthorizedAccessException) { return ErrNo.Acces; }
            catch (IOException) { return ErrNo.IO; }
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

        // This function signiature may SEGFAULT dotnet runtimes, but is required for some where reflection isn't 100% available.
        // * provided for situations like Unity
        public ErrNo NakedPathOpen(ExecContext ctx,
            fd dirFd,
            LookupFlags dirFlags,
            ptr pathPtr,
            size pathLen,
            OFlags oFlags,
            Rights fsRightsBase,
            Rights fsRightsInheriting,
            FdFlags fsFlags,
            ptr fdPtr)
        {
            PathOpen(ctx, dirFd, dirFlags, pathPtr, pathLen, oFlags, fsRightsBase, fsRightsInheriting, fsFlags, fdPtr, out ErrNo result);
            return result;
        }

        /// <summary>
        /// Open a file or directory (similar to POSIX openat).
        /// </summary>
        /// <param name="ctx">Execution context, gives us memory and other environment.</param>
        /// <param name="dirFd">File descriptor representing the directory in which we should look for <paramref name="pathPtr"/>.</param>
        /// <param name="dirFlags">Lookup flags for path resolution (e.g. symlink policy).</param>
        /// <param name="pathPtr">Pointer to the UTF-8 path string in guest memory.</param>
        /// <param name="pathLen">Length of the path string.</param>
        /// <param name="oFlags">WASI open flags (e.g. O_CREAT, O_DIRECTORY, etc.).</param>
        /// <param name="fsRightsBase">Rights for the newly created file descriptor.</param>
        /// <param name="fsRightsInheriting">Rights inherited by descriptors created from this one.</param>
        /// <param name="fsFlags">File-descriptor flags (e.g. non-blocking, sync, etc.).</param>
        /// <param name="fdPtr">Pointer where we store the newly allocated file descriptor.</param>
        /// <param name="result">Output: <see cref="ErrNo"/> code for success/failure.</param>
        public void PathOpen(
            ExecContext ctx,
            fd dirFd,
            LookupFlags dirFlags,
            ptr pathPtr,
            size pathLen,
            OFlags oFlags,
            Rights fsRightsBase,
            Rights fsRightsInheriting,
            FdFlags fsFlags,
            ptr fdPtr,
            out ErrNo result)
        {
            var mem = ctx.DefaultMemory;
            result = ErrNo.Success;

            // Validate memory bounds
            if (!mem.Contains((int)pathPtr, (int)pathLen))
            {
                result = ErrNo.Inval;
                return;
            }

            // Get directory file descriptor
            if (!GetFd(dirFd, out var dirFileDescriptor))
            {
                result = ErrNo.Badf;
                return;
            }

            // Directory must have read permission
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0)
            {
                result = ErrNo.Acces;
                return;
            }

            try
            {
                var pathToOpen = mem.ReadString(pathPtr, pathLen);
                var guestDirPath = dirFileDescriptor.Path;
                var hostDirPath = _state.PathMapper.MapToHostPath(guestDirPath);
                var guestPath = Path.Combine(guestDirPath, pathToOpen);
                var hostPath = Path.Combine(hostDirPath, pathToOpen);

                // Check file attributes to determine if path exists and its type
                FileAttributes attr;
                try
                {
                    attr = File.GetAttributes(hostPath);
                }
                catch (FileNotFoundException)
                {
                    // Handle non-existent path with creation flag
                    if (oFlags.HasFlag(OFlags.Creat))
                    {
                        try
                        {
                            // Inherit access from parent directory for new files
                            var fileStream = new FileStream(
                                hostPath,
                                FileMode.CreateNew,
                                dirFileDescriptor.Access,
                                FileShare.Read);

                            fd newFd = BindFile(
                                guestPath,
                                fileStream,
                                dirFileDescriptor.Access,
                                dirFileDescriptor.Rights,
                                fsRightsInheriting);

                            // Carry over the open-time fdflags so
                            // FdWrite honors O_APPEND etc.
                            if (_state.FileDescriptors.TryGetValue(newFd, out var openedFd))
                                openedFd.Flags = fsFlags;

                            mem.WriteInt32(fdPtr, (int)newFd);

                            // Handle truncation if requested
                            if (oFlags.HasFlag(OFlags.Trunc) && (dirFileDescriptor.Access & FileAccess.Write) != 0)
                            {
                                fileStream.SetLength(0);
                            }
                            return;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            result = ErrNo.Acces;
                            return;
                        }
                        catch (IOException)
                        {
                            result = ErrNo.IO;
                            return;
                        }
                    }
                    result = ErrNo.NoEnt;
                    return;
                }
                catch (DirectoryNotFoundException)
                {
                    result = ErrNo.NoEnt;
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    result = ErrNo.Acces;
                    return;
                }
                catch (IOException)
                {
                    result = ErrNo.IO;
                    return;
                }

                bool isDirectory = attr.HasFlag(FileAttributes.Directory);
                bool isReadOnly = attr.HasFlag(FileAttributes.ReadOnly);

                // Handle existing path based on whether it's a file or directory
                if (isDirectory)
                {
                    // Fail if trying to create exclusively
                    if (oFlags.HasFlag(OFlags.Creat) && oFlags.HasFlag(OFlags.Excl))
                    {
                        result = ErrNo.Exist;
                        return;
                    }

                    // Check for existing FD
                    if (GetFd(guestPath, out var existingFd))
                    {
                        mem.WriteInt32(fdPtr, (int)existingFd.Fd);
                        return;
                    }

                    fd newFd = BindDir(
                        hostPath,
                        guestPath,
                        dirFileDescriptor.Access,
                        false,
                        dirFileDescriptor.Rights,
                        fsRightsInheriting);

                    mem.WriteInt32(fdPtr, (int)newFd);
                }
                else // Regular file
                {
                    // Fail if trying to create exclusively
                    if (oFlags.HasFlag(OFlags.Creat) && oFlags.HasFlag(OFlags.Excl))
                    {
                        result = ErrNo.Exist;
                        return;
                    }

                    // Check for existing FD
                    if (GetFd(guestPath, out var existingFd))
                    {
                        mem.WriteInt32(fdPtr, (int)existingFd.Fd);
                        return;
                    }

                    // Inherit access from parent, but remove write access if file is read-only
                    var fileAccess = dirFileDescriptor.Access;
                    if (isReadOnly)
                    {
                        fileAccess &= ~FileAccess.Write;
                    }

                    try
                    {
                        var fileMode = oFlags.HasFlag(OFlags.Creat)
                            ? FileMode.OpenOrCreate
                            : FileMode.Open;

                        var fileStream = new FileStream(
                            hostPath,
                            fileMode,
                            fileAccess,
                            FileShare.Read);

                        fd newFd = BindFile(
                            guestPath,
                            fileStream,
                            fileAccess,
                            dirFileDescriptor.Rights,
                            fsRightsInheriting);

                        // Carry over the open-time fdflags so FdWrite
                        // honors O_APPEND etc.
                        if (_state.FileDescriptors.TryGetValue(newFd, out var openedFd2))
                            openedFd2.Flags = fsFlags;

                        mem.WriteInt32(fdPtr, (int)newFd);

                        // Handle truncation if requested and we have write access
                        if (oFlags.HasFlag(OFlags.Trunc) && (fileAccess & FileAccess.Write) != 0)
                        {
                            fileStream.SetLength(0);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        result = ErrNo.Acces;
                    }
                    catch (IOException)
                    {
                        result = ErrNo.IO;
                    }
                }
            }
            catch (Exception)
            {
                result = ErrNo.NoSys;
            }
        }

        /// <summary>
        /// Read the contents of a symbolic link.
        /// This method is analogous to the POSIX <c>readlinkat</c> function.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="dirFd">File descriptor of the directory containing the symbolic link.</param>
        /// <param name="pathPtr">Pointer to the path of the symbolic link to read.</param>
        /// <param name="pathLen">Length of the path.</param>
        /// <param name="bufPtr">Pointer to the buffer to store the contents of the symbolic link.</param>
        /// <param name="bufLen">Length of the buffer.</param>
        /// <param name="bufUsedPtr">Pointer to store the number of bytes used in the buffer.</param>
        /// <returns>An error code indicating the result of the operation.</returns>
        public ErrNo PathReadlink(
            ExecContext ctx,
            fd dirFd,
            ptr pathPtr,
            size pathLen,
            ptr bufPtr,
            size bufLen,
            ptr bufUsedPtr)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)pathPtr, (int)pathLen) ||
                !mem.Contains((int)bufPtr, (int)bufLen))
            {
                return ErrNo.Inval;
            }

            if (!GetFd(dirFd, out var dirFileDescriptor))
                return ErrNo.NoEnt;

            if ((dirFileDescriptor.Access & FileAccess.Read) == 0)
                return ErrNo.Acces;

            try
            {
                var pathToRead = mem.ReadString(pathPtr, pathLen);
                var guestDirPath = dirFileDescriptor.Path;
                var hostDirPath = _state.PathMapper.MapToHostPath(guestDirPath);
                var guestPath = Path.Combine(guestDirPath, pathToRead);
                var hostPath = Path.Combine(hostDirPath, pathToRead);

#if NET6_0_OR_GREATER
                var fileInfo = new FileInfo(hostPath);
                if (fileInfo.LinkTarget == null)
                {
                    return ErrNo.Inval; // Not a symlink
                }

                var linkTarget = fileInfo.LinkTarget;
                if (string.IsNullOrEmpty(linkTarget))
                {
                    return ErrNo.Inval;
                }

                // Check if the target would be within sandbox
                try
                {
                    var dirPart = Path.GetDirectoryName(hostPath) ?? throw new IOException("Invalid path");
                    var newPath = Path.Combine(dirPart, linkTarget);
                    var resolvedPath = VirtualPathMapper.ResolveSymbolicLinks(newPath, hostDirPath);

                    // If we get here, the path is valid and within sandbox
                    if (linkTarget.Length > bufLen)
                        return ErrNo.MsgSize;

                    int strLen = mem.WriteUtf8String(bufPtr, linkTarget, true);
                    mem.WriteInt32(bufUsedPtr, strLen);
                }
                catch (SandboxError sandboxError)
                {
                    return sandboxError.ErrorNumber;
                }
#else
        // Pre-.NET 8 - return unsupported operation
        return ErrNo.NoSys;
#endif
            }
            catch (FileNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }
            catch (IOException)
            {
                return ErrNo.NoSys;
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// Remove a directory.
        /// This method is analogous to the POSIX <c>unlinkat</c> function with <c>AT_REMOVEDIR</c> flag.
        /// Returns <c>errno::notempty</c> if the directory is not empty.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="fd">File descriptor of the directory to remove.</param>
        /// <param name="pathPtr">Pointer to the path of the directory to remove.</param>
        /// <param name="pathLen">Length of the path.</param>
        /// <returns>An error code indicating the result of the operation.</returns>
        public ErrNo PathRemoveDirectory(ExecContext ctx, fd fd, ptr pathPtr, size pathLen)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)pathPtr, (int)pathLen))
                return ErrNo.Inval;

            if (!GetFd(fd, out var dirFileDescriptor))
                return ErrNo.NoEnt;

            // Typically need FD read+write to remove
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0 ||
                (dirFileDescriptor.Access & FileAccess.Write) == 0)
            {
                return ErrNo.Acces;
            }

            try
            {
                var pathToRemove = mem.ReadString(pathPtr, pathLen);
                var guestDirPath = dirFileDescriptor.Path;
                var hostDirPath = _state.PathMapper.MapToHostPath(guestDirPath);

                var guestPath = Path.Combine(guestDirPath, pathToRemove);
                var hostPath = Path.Combine(hostDirPath, pathToRemove);


                Directory.Delete(hostPath, false);
                UnbindDir(guestPath);
            }
            catch (DirectoryNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (IOException)
            {
                return ErrNo.NotEmpty; // Directory is not empty
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// /// Renames a file or directory.
        /// </summary>
        /// <param name="ctx">Execution context of the calling process.</param>
        /// <param name="oldFd">File descriptor of the source directory.</param>
        /// <param name="oldPathPtr">Pointer to the source path of the file or directory to rename.</param>
        /// <param name="oldPathLen">Length of the source path.</param>
        /// <param name="newFd">File descriptor of the target directory.</param>
        /// <param name="newPathPtr">Pointer to the destination path for the renamed file or directory.</param>
        /// <param name="newPathLen">Length of the destination path.</param>
        /// <returns>Returns an error code. Successful rename returns <c>ErrNo.Success</c>.</returns>
        public ErrNo PathRename(
            ExecContext ctx,
            fd oldFd,
            ptr oldPathPtr,
            size oldPathLen,
            fd newFd,
            ptr newPathPtr,
            size newPathLen
        )
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)oldPathPtr, (int)oldPathLen) ||
                !mem.Contains((int)newPathPtr, (int)newPathLen))
            {
                return ErrNo.Inval;
            }

            try
            {
                if (!GetFd(oldFd, out var oldDirFileDescriptor))
                    return ErrNo.NoEnt;
                if (!GetFd(newFd, out var newDirFileDescriptor))
                    return ErrNo.NoEnt;

                var oldPathToRename = mem.ReadString(oldPathPtr, oldPathLen);
                var newPathForRename = mem.ReadString(newPathPtr, newPathLen);

                var oldGuestPath = Path.Combine(oldDirFileDescriptor.Path, oldPathToRename);
                var newGuestPath = Path.Combine(newDirFileDescriptor.Path, newPathForRename);

                var oldHostDirPath = _state.PathMapper.MapToHostPath(oldDirFileDescriptor.Path);
                var newHostDirPath = _state.PathMapper.MapToHostPath(newDirFileDescriptor.Path);

                var oldHostPath = Path.Combine(oldHostDirPath, oldPathToRename);
                var newHostPath = Path.Combine(newHostDirPath, newPathForRename);

                // Check if it's a file
                if (File.Exists(oldHostPath))
                {
                    // Need PATH_UNLINK_FILE + read in old
                    if (!oldDirFileDescriptor.Rights.HasFlag(Rights.PATH_UNLINK_FILE) ||
                        (oldDirFileDescriptor.Access & FileAccess.Read) == 0)
                        return ErrNo.Acces;
                    // Need PATH_CREATE_FILE + write in new
                    if (!newDirFileDescriptor.Rights.HasFlag(Rights.PATH_CREATE_FILE) ||
                        (newDirFileDescriptor.Access & FileAccess.Write) == 0)
                        return ErrNo.Acces;

                    // Rename the file
                    File.Move(oldHostPath, newHostPath);
                }
                else if (Directory.Exists(oldHostPath))
                {
                    // Need PATH_REMOVE_DIRECTORY + read in old
                    if (!oldDirFileDescriptor.Rights.HasFlag(Rights.PATH_REMOVE_DIRECTORY) ||
                        (oldDirFileDescriptor.Access & FileAccess.Read) == 0)
                        return ErrNo.Acces;
                    // Need PATH_CREATE_DIRECTORY + write in new
                    if (!newDirFileDescriptor.Rights.HasFlag(Rights.PATH_CREATE_DIRECTORY) ||
                        (newDirFileDescriptor.Access & FileAccess.Write) == 0)
                        return ErrNo.Acces;

                    Directory.Move(oldHostPath, newHostPath);
                }
                else
                {
                    // The source does not exist
                    return ErrNo.NoEnt;
                }

                // Update path mapper for the rename if needed
                _state.PathMapper.MoveHostPath(oldHostPath, newHostPath);

                // If you want to update FDs that currently reference the old path,
                // you'd do that logic here (optional or required by your design).
            }
            catch (FileNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (IOException)
            {
                // In POSIX rename can fail for many reasons: EACCES, EISDIR, etc.
                // Return a catchall for now
                return ErrNo.NoSys;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }

            return ErrNo.Success;
        }

        /// <summary>
        /// Creates a symbolic link.
        /// </summary>
        /// <param name="ctx">Execution context of the calling process.</param>
        /// <param name="oldPathPtr">Pointer to the contents of the symbolic link.</param>
        /// <param name="oldPathLen">Length of the symbolic link contents.</param>
        /// <param name="fd">File descriptor of the target directory where the symbolic link will be created.</param>
        /// <param name="newPathPtr">Pointer to the destination path for the new symbolic link.</param>
        /// <param name="newPathLen">Length of the destination path.</param>
        /// <returns>Returns an error code. Successful creation returns <c>ErrNo.Success</c>.</returns>
        public ErrNo PathSymlink(ExecContext ctx, ptr oldPathPtr, size oldPathLen, fd fd, ptr newPathPtr, size newPathLen)
        {
            if (!_config.AllowSymbolicLinks)
                return ErrNo.NotSup;

#if NET6_0_OR_GREATER
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)oldPathPtr, (int)oldPathLen) ||
                !mem.Contains((int)newPathPtr, (int)newPathLen))
                return ErrNo.Inval;

            if (!GetFd(fd, out var dirFileDescriptor))
                return ErrNo.NoEnt;
            if ((dirFileDescriptor.Access & FileAccess.Write) == 0)
                return ErrNo.Acces;
            if (dirFileDescriptor.Type != Filetype.Directory)
                return ErrNo.NotDir;

            try
            {
                // The symlink "target" (oldPath) is stored as-is — no
                // host-mapping. WASI symlinks contain a guest-relative
                // path string that the guest will later traverse via
                // path_open; the host doesn't need to resolve it now and
                // doing so would cause sandbox escape via dangling
                // symlinks.
                var oldPath = mem.ReadString(oldPathPtr, oldPathLen);
                var newPath = mem.ReadString(newPathPtr, newPathLen);

                var guestPath = Path.Combine(dirFileDescriptor.Path, newPath);
                var newHostPath = _state.PathMapper.MapToHostPath(guestPath);

                File.CreateSymbolicLink(newHostPath, oldPath);
                return ErrNo.Success;
            }
            catch (DirectoryNotFoundException) { return ErrNo.NoEnt; }
            catch (FileNotFoundException)      { return ErrNo.NoEnt; }
            catch (IOException ioe) when (ioe.HResult == unchecked((int)0x80070050)) { return ErrNo.Exist; }
            catch (IOException)                { return ErrNo.IO; }
            catch (UnauthorizedAccessException) { return ErrNo.Acces; }
#else
            return ErrNo.NotSup;
#endif
        }

        /// <summary>
        /// Unlinks (removes) a file.
        /// </summary>
        /// <param name="ctx">Execution context of the calling process.</param>
        /// <param name="fd">File descriptor of the directory from which to unlink the file.</param>
        /// <param name="pathPtr">Pointer to the path of the file to unlink.</param>
        /// <param name="pathLen">Length of the path to the file.</param>
        /// <returns>Returns an error code. Successful unlinking returns <c>ErrNo.Success</c>. If the path refers to a directory, returns <c>ErrNo.isdir</c>.</returns>
        public ErrNo PathUnlinkFile(ExecContext ctx, fd fd, ptr pathPtr, size pathLen)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)pathPtr, (int)pathLen))
                return ErrNo.Inval;

            if (!GetFd(fd, out var dirFileDescriptor))
                return ErrNo.NoEnt;

            // Typically need read + write to unlink
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0 ||
                (dirFileDescriptor.Access & FileAccess.Write) == 0)
            {
                return ErrNo.Acces;
            }

            try
            {
                var pathToUnlink = mem.ReadString(pathPtr, pathLen);
                var guestDirPath = dirFileDescriptor.Path;
                var hostDirPath = _state.PathMapper.MapToHostPath(guestDirPath);

                var guestPath = Path.Combine(guestDirPath, pathToUnlink);
                var hostPath = Path.Combine(hostDirPath, pathToUnlink);

                // If it's a directory, user should call PathRemoveDirectory
                if (Directory.Exists(hostPath))
                    return ErrNo.IsDir;

                File.Delete(hostPath);
                UnbindFile(guestPath);
            }
            catch (FileNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (UnauthorizedAccessException)
            {
                return ErrNo.Acces;
            }
            catch (IOException)
            {
                return ErrNo.NoSys;
            }

            return ErrNo.Success;
        }
    }
}