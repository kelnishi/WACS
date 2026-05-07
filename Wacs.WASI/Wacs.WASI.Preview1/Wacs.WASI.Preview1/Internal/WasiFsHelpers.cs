// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.

using System;
using System.IO;
using System.Linq;
using Wacs.WASI.Preview1.Types;

namespace Wacs.WASI.Preview1.Internal
{
    /// <summary>
    /// Static helpers extracted from <see cref="FileSystem"/>'s instance
    /// methods so the [WacsImport]-annotated AOT entry points can call
    /// them without owning a FileSystem instance. The interpreter path
    /// continues to use the instance-method wrappers, which delegate
    /// here too.
    /// </summary>
    internal static class WasiFsHelpers
    {
        public static bool TryGetFd(State state, uint fd, out FileDescriptor fileDescriptor)
        {
            if (state.FileDescriptors.TryGetValue(fd, out var file))
            {
                fileDescriptor = file;
                return true;
            }
            fileDescriptor = FileDescriptor.BadFd;
            return false;
        }

        public static bool TryRemoveFd(State state, uint fd)
        {
            if (state.FileDescriptors.TryRemove(fd, out var fileDescriptor))
            {
                if (fileDescriptor.Type == Filetype.RegularFile)
                    fileDescriptor.Dispose();
                return true;
            }
            return false;
        }

        public static void MoveFd(State state, uint from, uint to)
        {
            if (state.FileDescriptors.ContainsKey(to))
                throw new IOException($"Cannot overwrite existing file descriptor {to}.");

            if (state.FileDescriptors.TryRemove(from, out var fileDescriptor))
            {
                fileDescriptor.Fd = to;
                if (!state.FileDescriptors.TryAdd(to, fileDescriptor))
                    throw new IOException($"Failed to add file descriptor {to}.");
            }
        }

        public static FileStat BuildFileStatForFile(FileInfo fileInfo)
        {
            var inode = FileUtil.GenerateInode(fileInfo);
            return new FileStat
            {
                Device = 0,
                Ino = inode,
                Mode = Filetype.RegularFile,
                NLink = 1,
                Size = (ulong)fileInfo.Length,
                ATim = Clock.ToTimestamp(fileInfo.LastAccessTimeUtc),
                MTim = Clock.ToTimestamp(fileInfo.LastWriteTimeUtc),
                CTim = Clock.ToTimestamp(fileInfo.CreationTimeUtc),
            };
        }

        public static uint BindDir(State state, WasiConfiguration config,
            string hostDir, string guestDir, FileAccess perm, bool isPreopened,
            Rights restrictedRights = Rights.All, Rights inheritedRights = Rights.None)
        {
            if (state.FileDescriptors.Count >= config.MaxOpenFileDescriptors)
                throw new InvalidOperationException(
                    $"File descriptor count would exceed the maximum limit of {config.MaxOpenFileDescriptors}.");
            if (!Directory.Exists(hostDir))
                throw new DirectoryNotFoundException($"The host path '{hostDir}' does not exist.");
            if (string.IsNullOrWhiteSpace(guestDir))
                throw new DirectoryNotFoundException($"The guest path '{guestDir}' cannot be created.");

            if (!guestDir.StartsWith("/")) guestDir = "/" + guestDir;
            if (guestDir.StartsWith("/dev"))
                throw new UnauthorizedAccessException($"The guest path '{guestDir}' is not allowed.");

            state.PathMapper.AddDirectoryMapping(guestDir, hostDir);

            uint newFd = state.GetNextFd;
            var fileDescriptor = new FileDescriptor
            {
                Fd = newFd, Stream = Stream.Null, Path = guestDir,
                Access = perm, IsPreopened = isPreopened, Type = Filetype.Directory,
            };
            var dirInfo = new DirectoryInfo(hostDir);
            // What this directory could grant if uncapped — driven by
            // FileAccess + the AllowFileCreation/Deletion config flags.
            var maxRights = FileDescriptor.ComputeFileRights(
                dirInfo, Filetype.Directory, perm, Stream.Null,
                config.AllowFileCreation, config.AllowFileDeletion);
            // Per WASI spec, path_open's rules cap base/inheriting separately:
            //   base       = restrictedRights (= fsRightsBase ∩ parent.inheriting)
            //   inheriting = inheritedRights  (= fsRightsInheriting ∩ parent.inheriting)
            // Both still bounded by what this dir could possibly grant.
            // The dir's *own* base further drops file-only rights so
            // rust/directory_seek and rust/truncation_rights don't see
            // FD_SEEK/FD_FILESTAT_SET_SIZE on a directory FD.
            var baseRights = restrictedRights == Rights.All
                ? maxRights
                : (restrictedRights & maxRights);
            var inhRights  = inheritedRights == Rights.None
                ? maxRights
                : (inheritedRights & maxRights);
            fileDescriptor.Rights          = baseRights & ~FileDescriptor.DirInapplicableRights;
            fileDescriptor.InheritedRights = inhRights;
            state.FileDescriptors[newFd] = fileDescriptor;
            return newFd;
        }

        public static void UnbindDir(State state, string guestDir)
        {
            if (!state.PathMapper.TryRemoveMapping(guestDir))
                throw new DirectoryNotFoundException(
                    $"The guest path '{guestDir}' cannot be unbound because it does not exist.");
            var entry = state.FileDescriptors.Values
                .FirstOrDefault(descriptor => descriptor.Path == guestDir);
            if (entry == null) return;
            state.FileDescriptors.TryRemove(entry.Fd, out _);
        }

        public static uint BindFile(State state,
            string guestPath, Stream filestream, FileAccess perm,
            Rights rights, Rights inheritedRights,
            Rights restrictedRights = Rights.All)
        {
            if (guestPath == "/dev/null") return BindDevNull(state, perm);

            var hostpath = state.PathMapper.MapToHostPath(guestPath);
            if (!File.Exists(hostpath))
                throw new FileNotFoundException($"The host file '{hostpath}' does not exist.");

            rights &= restrictedRights;
            if (inheritedRights == Rights.None) inheritedRights = rights;
            else                                rights = inheritedRights & rights;

            uint newFd = state.GetNextFd;
            state.FileDescriptors[newFd] = new FileDescriptor
            {
                Fd = newFd, Stream = filestream, Path = guestPath,
                Access = perm, IsPreopened = false,
                Type = Filetype.RegularFile,
                Rights = rights, InheritedRights = inheritedRights,
            };
            return newFd;
        }

        public static uint BindDevNull(State state, FileAccess perm)
        {
            uint newFd = state.GetNextFd;
            state.FileDescriptors[newFd] = new FileDescriptor
            {
                Fd = newFd, Stream = new NullStream(), Path = "/dev/null",
                Access = perm, IsPreopened = true, Type = Filetype.CharacterDevice,
            };
            return newFd;
        }

        public static void UnbindFile(State state, string guestPath)
        {
            var entry = state.FileDescriptors.Values
                .FirstOrDefault(descriptor => descriptor.Path == guestPath);
            if (entry == null) return;
            state.FileDescriptors.TryRemove(entry.Fd, out _);
        }

        public static FileStat BuildFileStatForDirectory(DirectoryInfo dirInfo)
        {
            var inode = FileUtil.GenerateInode(dirInfo);
            return new FileStat
            {
                Device = 0,
                Ino = inode,
                Mode = Filetype.Directory,
                NLink = 1,
                Size = 0,
                ATim = Clock.ToTimestamp(dirInfo.LastAccessTimeUtc),
                MTim = Clock.ToTimestamp(dirInfo.LastWriteTimeUtc),
                CTim = Clock.ToTimestamp(dirInfo.CreationTimeUtc),
            };
        }
    }
}
