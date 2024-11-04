using System;
using System.IO;
using Wacs.Core.Runtime;
using Wacs.Core.WASIp1;
using Wacs.WASIp1.Types;
using ptr = System.UInt32;
using fd = System.UInt32;
using size = System.UInt32;

namespace Wacs.WASIp1
{
    public partial class Filesystem
    {
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
            
            if (!GetFd(fd, out var dirFd))
                return ErrNo.NoEnt;
            if (!dirFd.Rights.HasFlag(Rights.PATH_CREATE_DIRECTORY) || (dirFd.Access & FileAccess.Write) == 0)
                return ErrNo.Acces;
            
            try
            {
                var pathToCreate = mem.ReadString(pathPtr, pathLen);
                var guestDirPath = dirFd.Path;
                var hostDirPath = _state.PathMapper.MapToHostPath(guestDirPath);
                var newHostPath = Path.Combine(hostDirPath, pathToCreate);
                var newGuestPath = Path.Combine(guestDirPath, pathToCreate);
                Directory.CreateDirectory(pathToCreate);
                var rights = FileDescriptor.ComputeFileRights(
                    new FileInfo(newHostPath),
                    Filetype.Directory,
                    dirFd.Rights.ToFileAccess(),
                    Stream.Null,
                    dirFd.Rights.HasFlag(Rights.PATH_CREATE_FILE),
                    dirFd.Rights.HasFlag(Rights.PATH_UNLINK_FILE)
                ) & dirFd.Rights;
                
                fd newFd = BindDir(newHostPath, newGuestPath, dirFd.Access, true, rights, dirFd.Rights);
            }
            //TODO fix these exception results
            catch (IOException ex) when (ex is DirectoryNotFoundException)
            {
                return ErrNo.NoSys;
            }
            catch (IOException)
            {
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
            
            return ErrNo.NotSup;
            // var mem = ctx.DefaultMemory;
            // if (!mem.Contains((int)oldPathPtr, (int)oldPathLen) || !mem.Contains((int)newPathPtr, (int)newPathLen))
            //     return ErrNo.Inval;
            //
            // var oldFileDescriptor = GetFD(oldFd);
            // if ((oldFileDescriptor.Access & FileAccess.Read) == 0)
            //     return ErrNo.Acces;
            //
            // var newFileDescriptor = GetFD(newFd);
            // if (!newFileDescriptor.AllowFileCreation || (newFileDescriptor.Access & FileAccess.Write) == 0)
            //     return ErrNo.Acces;
            //
            // try
            // {
            //     var oldSourcePath = mem.ReadString(oldPathPtr, oldPathLen);
            //     var newDestinationPath = mem.ReadString(newPathPtr, newPathLen);
            //     var oldHostPath = _state.PathMapper.MapToHostPath(oldSourcePath);
            //     var newHostPath = _state.PathMapper.MapToHostPath(newDestinationPath);
            //
            //     // Creating hard link is system dependent
            //     System.IO.File.CreateHardLink(newHostPath, oldHostPath);
            // }
            // catch (IOException ex) when (ex is DirectoryNotFoundException)
            // {
            //     return ErrNo.NoSys;
            // }
            // catch (IOException ex)
            // {
            //     return ErrNo.Exist;
            // }
            //
            // return ErrNo.Success;
        }

        /// <summary>
        /// Open a file or directory.
        /// This method is analogous to the POSIX <c>openat</c> function.
        /// </summary>
        /// <param name="ctx">The execution context.</param>
        /// <param name="dirFd">File descriptor of the directory where the file is located.</param>
        /// <param name="dirFlags">Flags that determine how the path is resolved.</param>
        /// <param name="pathPtr">Pointer to the path of the file or directory to open.</param>
        /// <param name="pathLen">Length of the path.</param>
        /// <param name="oFlags">Flags determining how to open the file.</param>
        /// <param name="fsRightsBase">Initial rights for the newly created file descriptor.</param>
        /// <param name="fsRightsInheriting">Rights that apply to file descriptors derived from this one.</param>
        /// <param name="fsFlags">Desired values of the file descriptor flags.</param>
        /// <param name="fdPtr">Pointer to store the newly created file descriptor.</param>
        /// <returns>An error code indicating the result of the operation.</returns>
        public ErrNo PathOpen(ExecContext ctx, fd dirFd, LookupFlags dirFlags, ptr pathPtr, size pathLen, OFlags oFlags, Rights fsRightsBase,
            Rights fsRightsInheriting, FdFlags fsFlags, ptr fdPtr)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)pathPtr, (int)pathLen))
                return ErrNo.Inval;

            if (!GetFd(dirFd, out var dirFileDescriptor))
                return ErrNo.NoEnt;
            
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0)
                return ErrNo.Acces;

            try
            {
                var pathToOpen = mem.ReadString(pathPtr, pathLen);
                var guestPath = Path.Combine(dirFileDescriptor.Path, pathToOpen);
                var hostPath = _state.PathMapper.MapToHostPath(guestPath);
                
                if (File.Exists(hostPath))
                {
                    //File
                    if (GetFd(guestPath, out var existing))
                    {
                        mem.WriteInt32(fdPtr, existing.Fd);
                        return ErrNo.Success;
                    }
                    
                    var fileAccess = fsRightsBase.ToFileAccess();

                    var fileInfo = new FileInfo(hostPath);
                    var fileStream = new FileStream(hostPath, oFlags.ToFileMode(), fileAccess, FileShare.None);
                    var rights = FileDescriptor.ComputeFileRights(fileInfo,
                                     Filetype.RegularFile,
                                     fileAccess,
                                     fileStream,
                                     dirFileDescriptor.Rights.HasFlag(Rights.PATH_CREATE_FILE),
                                     dirFileDescriptor.Rights.HasFlag(Rights.PATH_UNLINK_FILE))
                                 & fsRightsBase & dirFileDescriptor.Rights;
                    var inheritedRights = fsRightsInheriting & dirFileDescriptor.Rights;
                    
                    if (!inheritedRights.HasFlag(Rights.PATH_OPEN))
                        return ErrNo.Acces;

                    fileAccess = rights.ToFileAccess();
                    if (fileAccess == 0)
                        return ErrNo.Acces;
                        
                    fd newFd = BindFile(guestPath, fileStream, dirFileDescriptor.Access, rights, inheritedRights);
                    
                    mem.WriteInt32(fdPtr, newFd);
                }
                else if (Directory.Exists(hostPath))
                {
                    //Directory
                    if (GetFd(guestPath, out var existing))
                    {
                        mem.WriteInt32(fdPtr, existing.Fd);
                        return ErrNo.Success;
                    }
                    
                    fd newFd = BindDir(guestPath, hostPath, dirFileDescriptor.Access, false, fsRightsBase, dirFileDescriptor.Rights & fsRightsInheriting);
                    
                    mem.WriteInt32(fdPtr, newFd);
                }
                else
                {
                    return ErrNo.NoEnt;
                }
            }
            catch (IOException ex) when (ex is FileNotFoundException)
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
        public ErrNo PathReadlink(ExecContext ctx, fd dirFd, ptr pathPtr, size pathLen, ptr bufPtr, size bufLen, ptr bufUsedPtr)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)pathPtr, (int)pathLen) || !mem.Contains((int)bufPtr, (int)bufLen))
                return ErrNo.Inval;

            if (!GetFd(dirFd, out var dirFileDescriptor))
                return ErrNo.NoEnt;
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0)
                return ErrNo.Acces;

            try
            {
                var pathToRead = mem.ReadString(pathPtr, pathLen);
                var guestPath = Path.Combine(dirFileDescriptor.Path, pathToRead);
                var hostPath = _state.PathMapper.MapToHostPath(guestPath);
                
                var linkTarget = File.ReadAllText(hostPath);
                if (linkTarget.Length > bufLen)
                    return ErrNo.MsgSize;

                int strLen = mem.WriteUtf8String(bufPtr, linkTarget, true);
                mem.WriteInt32(bufUsedPtr, strLen);
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
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0)
                return ErrNo.Acces;

            try
            {
                var pathToRemove = mem.ReadString(pathPtr, pathLen);
                var guestPath = Path.Combine(dirFileDescriptor.Path, pathToRemove);
                var hostPath = _state.PathMapper.MapToHostPath(guestPath);

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
        /// Renames a file or directory.
        /// </summary>
        /// <param name="ctx">Execution context of the calling process.</param>
        /// <param name="oldFd">File descriptor of the source directory.</param>
        /// <param name="oldPathPtr">Pointer to the source path of the file or directory to rename.</param>
        /// <param name="oldPathLen">Length of the source path.</param>
        /// <param name="newFd">File descriptor of the target directory.</param>
        /// <param name="newPathPtr">Pointer to the destination path for the renamed file or directory.</param>
        /// <param name="newPathLen">Length of the destination path.</param>
        /// <returns>Returns an error code. Successful rename returns <c>ErrNo.Success</c>.</returns>
        public ErrNo PathRename(ExecContext ctx, fd oldFd, ptr oldPathPtr, size oldPathLen, fd newFd, ptr newPathPtr, size newPathLen)
        {
            var mem = ctx.DefaultMemory;
            if (!mem.Contains((int)oldPathPtr, (int)oldPathLen) || !mem.Contains((int)newPathPtr, (int)newPathLen))
                return ErrNo.Inval;

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
                var oldHostPath = _state.PathMapper.MapToHostPath(oldGuestPath);
                var newHostPath = _state.PathMapper.MapToHostPath(newGuestPath);
                
                if (File.Exists(oldHostPath))
                {
                    // It is a file
                    if (!oldDirFileDescriptor.Rights.HasFlag(Rights.PATH_UNLINK_FILE) ||
                        (oldDirFileDescriptor.Access & FileAccess.Read) == 0)
                        return ErrNo.Acces;
                    if (!newDirFileDescriptor.Rights.HasFlag(Rights.PATH_CREATE_FILE) || 
                        (newDirFileDescriptor.Access & FileAccess.Write) == 0)
                        return ErrNo.Acces;
                }
                else if (Directory.Exists(oldHostPath))
                {
                    // It is a directory
                    if (!oldDirFileDescriptor.Rights.HasFlag(Rights.PATH_REMOVE_DIRECTORY) ||
                        (oldDirFileDescriptor.Access & FileAccess.Read) == 0)
                        return ErrNo.Acces;
                    if (!newDirFileDescriptor.Rights.HasFlag(Rights.PATH_CREATE_DIRECTORY) || 
                        (newDirFileDescriptor.Access & FileAccess.Write) == 0)
                        return ErrNo.Acces;
                }
                else
                {
                    return ErrNo.NoEnt; // The file or directory does not exist
                }

                // Rename the file or directory
                File.Move(oldHostPath, newHostPath);
                _state.PathMapper.MoveHostPath(oldHostPath, newHostPath);
            }
            catch (FileNotFoundException)
            {
                return ErrNo.NoEnt;
            }
            catch (IOException)
            {
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
            
            return ErrNo.NotSup;
            // var mem = ctx.DefaultMemory;
            // if (!mem.Contains((int)oldPathPtr, (int)oldPathLen) || !mem.Contains((int)newPathPtr, (int)newPathLen))
            //     return ErrNo.Inval;
            //
            // var dirFileDescriptor = GetFD(fd);
            // if ((dirFileDescriptor.Access & FileAccess.Write) == 0)
            //     return ErrNo.Acces;
            //
            // try
            // {
            //     var oldPath = mem.ReadString(oldPathPtr, oldPathLen);
            //     var oldHostPath = _state.PathMapper.MapToHostPath(oldPath);
            //     var newPath = mem.ReadString(newPathPtr, newPathLen);
            //     var guestPath = Path.Combine(dirFileDescriptor.Path, newPath);
            //     var newHostPath = _state.PathMapper.MapToHostPath(guestPath);
            //     
            //     var target = new FileInfo(oldHostPath);
            //     var symlink = new FileInfo(newHostPath);
            //     //Symlinks can be created as of .NET 5/.NET Core 3.0
            //     symlink.CreateSymbolicLink(target.FullName);
            // }
            // catch (IOException ex) when (ex is DirectoryNotFoundException)
            // {
            //     return ErrNo.NoSys;
            // }
            // catch (IOException ex)
            // {
            //     return ErrNo.Exist;
            // }
            // catch (UnauthorizedAccessException)
            // {
            //     return ErrNo.Acces;
            // }
            //
            // return ErrNo.Success;
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
            if ((dirFileDescriptor.Access & FileAccess.Read) == 0)
                return ErrNo.Acces;

            try
            {
                var pathToUnlink = mem.ReadString(pathPtr, pathLen);
                var guestPath = Path.Combine(dirFileDescriptor.Path, pathToUnlink);
                var hostPath = _state.PathMapper.MapToHostPath(guestPath);

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