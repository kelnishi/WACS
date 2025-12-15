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
using Wacs.WASIp1.Types;

using ptr = System.UInt32;
using fd = System.UInt32;
using filesize = System.UInt64;
using size = System.UInt32;
using timestamp = System.Int64;
using dircookie = System.Int64;
using filedelta = System.Int64;

namespace Wacs.WASIp1
{
    public partial class FileSystem : IBindable, IDisposable
    {
        private readonly WasiConfiguration _config;
        private readonly State _state;
        private bool _disposed;

        public FileSystem(WasiConfiguration config, State state)
        {
            _config = config;
            _state = state;
            InitializeFs();
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            ThrowIfDisposed();

            string module = "wasi_snapshot_preview1";
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, int>>(
                (module, "path_create_directory"), (ctx, a, b, c) => (int)PathCreateDirectory(ctx, a, b, c));
            
            runtime.BindHostFunction<Func<ExecContext, fd, int, ptr, size, ptr, int>>(
                (module, "path_filestat_get"), (ctx, a, b, c, d, e) => (int)PathFilestatGet(ctx, a, (LookupFlags)b, c, d, e));
            
            runtime.BindHostFunction<Func<ExecContext, fd, int, ptr, size, timestamp, timestamp, int, int>>(
                (module, "path_filestat_set_times"), (ctx, a, b, c, d, e, f, g) => (int)PathFilestatSetTimes(ctx, a, (LookupFlags)b, c, d, e, f, (FstFlags)g));
            
            runtime.BindHostFunction<Func<ExecContext, fd, int, ptr, size, fd, ptr, size, int>>(
                (module, "path_link"), (ctx, a, b, c, d, e, f, g) => (int)PathLink(ctx, a, (LookupFlags)b, c, d, e, f, g));

            runtime.BindHostFunction<Func<ExecContext, fd, int, ptr, size, int, long, long, int, ptr, int>>(
                (module, "path_open"), PathOpenAot);

            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, ptr, size, ptr, int>>(
                (module, "path_readlink"), (ctx, a, b, c, d, e, f) => (int)PathReadlink(ctx, a, b, c, d, e, f));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, int>>(
                (module, "path_remove_directory"), (ctx, a, b, c) => (int)PathRemoveDirectory(ctx, a, b, c));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, fd, ptr, size, int>>(
                (module, "path_rename"), (ctx, a, b, c, d, e, f) => (int)PathRename(ctx, a, b, c, d, e, f));
            
            runtime.BindHostFunction<Func<ExecContext, ptr, size, fd, ptr, size, int>>(
                (module, "path_symlink"), (ctx, a, b, c, d, e) => (int)PathSymlink(ctx, a, b, c, d, e));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, int>>(
                (module, "path_unlink_file"), (ctx, a, b, c) => (int)PathUnlinkFile(ctx, a, b, c));

            runtime.BindHostFunction<Func<ExecContext, fd, filesize, filesize, int, int>>(
                (module, "fd_advise"), (ctx, a, b, c, d) => (int)FdAdvise(ctx, a, b, c, (Advice)d));
            
            runtime.BindHostFunction<Func<ExecContext, fd, filesize, filesize, int>>(
                (module, "fd_allocate"), (ctx, a, b, c) => (int)FdAllocate(ctx, a, b, c));
            
            runtime.BindHostFunction<Func<ExecContext, fd, int>>(
                (module, "fd_close"), (ctx, a) => (int)FdClose(ctx, a));
            
            runtime.BindHostFunction<Func<ExecContext, fd, int>>(
                (module, "fd_datasync"), (ctx, a) => (int)FdDatasync(ctx, a));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, int>>(
                (module, "fd_fdstat_get"), (ctx, a, b) => (int)FdFdstatGet(ctx, a, b));
            
            runtime.BindHostFunction<Func<ExecContext, fd, int, int>>(
                (module, "fd_fdstat_set_flags"), (ctx, a, b) => (int)FdFdstatSetFlags(ctx, a, (FdFlags)b));
            
            runtime.BindHostFunction<Func<ExecContext, fd, long, long, int>>(
                (module, "fd_fdstat_set_rights"), (ctx, a, b, c) => (int)FdFdstatSetRights(ctx, a, (Rights)b, (Rights)c));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, int>>(
                (module, "fd_filestat_get"), (ctx, a, b) => (int)FdFilestatGet(ctx, a, b));
            
            runtime.BindHostFunction<Func<ExecContext, fd, filesize, int>>(
                (module, "fd_filestat_set_size"), (ctx, a, b) => (int)FdFilestatSetSize(ctx, a, b));
            
            runtime.BindHostFunction<Func<ExecContext, fd, timestamp, timestamp, int, int>>(
                (module, "fd_filestat_set_times"), (ctx, a, b, c, d) => (int)FdFilestatSetTimes(ctx, a, b, c, (FstFlags)d));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, filesize, ptr, int>>(
                (module, "fd_pread"), (ctx, a, b, c, d, e) => (int)FdPread(ctx, a, b, c, d, e));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, int>>(
                (module, "fd_prestat_get"), (ctx, a, b) => (int)FdPrestatGet(ctx, a, b));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, int>>(
                (module, "fd_prestat_dir_name"), (ctx, a, b, c) => (int)FdPrestatDirName(ctx, a, b, c));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, filesize, ptr, int>>(
                (module, "fd_pwrite"), (ctx, a, b, c, d, e) => (int)FdPwrite(ctx, a, b, c, d, e));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, ptr, int>>(
                (module, "fd_read"), (ctx, a, b, c, d) => (int)FdRead(ctx, a, b, c, d));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, dircookie, ptr, int>>(
                (module, "fd_readdir"), (ctx, a, b, c, d, e) => (int)FdReaddir(ctx, a, b, c, d, e));
            
            runtime.BindHostFunction<Func<ExecContext, fd, fd, int>>(
                (module, "fd_renumber"), (ctx, a, b) => (int)FdRenumber(ctx, a, b));
            
            runtime.BindHostFunction<Func<ExecContext, fd, filedelta, int, ptr, int>>(
                (module, "fd_seek"), (ctx, a, b, c, d) => (int)FdSeek(ctx, a, b, (Whence)c, d));
            
            runtime.BindHostFunction<Func<ExecContext, fd, int>>(
                (module, "fd_sync"), (ctx, a) => (int)FdSync(ctx, a));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, int>>(
                (module, "fd_tell"), (ctx, a, b) => (int)FdTell(ctx, a, b));
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, ptr, int>>(
                (module, "fd_write"), (ctx, a, b, c, d) => (int)FdWrite(ctx, a, b, c, d));
        }

        private int PathOpenAot(
            ExecContext ctx,
            fd dirfd,
            int dirflags,
            ptr pathPtr,
            size pathLen,
            int oflags,
            long fsRightsBase,
            long fsRightsInheriting,
            int fdflags,
            ptr fdOut)
        {
            PathOpen(
                ctx,
                dirfd,
                (LookupFlags)dirflags,
                pathPtr,
                pathLen,
                (OFlags)oflags,
                (Rights)fsRightsBase,
                (Rights)fsRightsInheriting,
                (FdFlags)fdflags,
                fdOut,
                out ErrNo result);

            return (int)result;
        }

        private void InitializeFs()
        {
            ThrowIfDisposed();
            BindStdio();
            BindPreopenedDirs();
        }

        private void BindPreopenedDirs()
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(_config.HostRootDirectory))
            {
                throw new ArgumentException(
                    "No root directory was specified in HostRootDirectory (null/whitespace).",
                    nameof(_config.HostRootDirectory));
            }

            if (!Directory.Exists(_config.HostRootDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"The host path '{_config.HostRootDirectory}' does not exist.");
            }

            _state.PathMapper.SetRootMapping(_config.HostRootDirectory);

            BindDir(_config.HostRootDirectory, "/", _config.DefaultPermissions, true);

            foreach (var pod in _config.PreopenedDirectories)
            {
                BindDir(pod.HostPath, pod.GuestPath, pod.Permissions, true);
            }
        }

        private fd BindDir(
            string hostDir,
            string guestDir,
            FileAccess perm,
            bool isPreopened,
            Rights restrictedRights = Rights.All,
            Rights inheritedRights = Rights.None)
        {
            ThrowIfDisposed();

            if (_state.FileDescriptors.Count >= _config.MaxOpenFileDescriptors)
            {
                throw new InvalidOperationException(
                    $"File descriptor count would exceed the maximum limit of {_config.MaxOpenFileDescriptors}.");
            }

            if (!Directory.Exists(hostDir))
            {
                throw new DirectoryNotFoundException(
                    $"The host path '{hostDir}' does not exist.");
            }

            if (string.IsNullOrWhiteSpace(guestDir))
            {
                throw new DirectoryNotFoundException(
                    $"The guest path '{guestDir}' cannot be created.");
            }

            if (!guestDir.StartsWith("/"))
            {
                guestDir = "/" + guestDir;
            }

            if (guestDir.StartsWith("/dev"))
            {
                throw new UnauthorizedAccessException(
                    $"The guest path '{guestDir}' is not allowed.");
            }

            _state.PathMapper.AddDirectoryMapping(guestDir, hostDir);

            fd newFd = _state.GetNextFd;
            var fileDescriptor = new FileDescriptor
            {
                Fd = newFd,
                Stream = Stream.Null,
                Path = guestDir,
                Access = perm,
                IsPreopened = isPreopened,
                Type = Filetype.Directory,
            };

            var dirInfo = new DirectoryInfo(hostDir);
            var rights = FileDescriptor.ComputeFileRights(
                dirInfo,
                Filetype.Directory,
                perm,
                Stream.Null,
                _config.AllowFileCreation,
                _config.AllowFileDeletion) & restrictedRights;

            if (inheritedRights == Rights.None)
            {
                fileDescriptor.Rights = rights;
                fileDescriptor.InheritedRights = rights;
            }
            else
            {
                fileDescriptor.Rights = inheritedRights & rights;
                fileDescriptor.InheritedRights = inheritedRights;
            }

            _state.FileDescriptors[newFd] = fileDescriptor;
            return newFd;
        }

        private void UnbindDir(string guestDir)
        {
            ThrowIfDisposed();

            if (!_state.PathMapper.TryRemoveMapping(guestDir))
            {
                throw new DirectoryNotFoundException(
                    $"The guest path '{guestDir}' cannot be unbound because it does not exist.");
            }

            var entry = _state.FileDescriptors.Values
                .FirstOrDefault(descriptor => descriptor.Path == guestDir);

            if (entry == null) return;

            _state.FileDescriptors.TryRemove(entry.Fd, out _);
        }

        private fd BindFile(
            string guestPath,
            Stream filestream,
            FileAccess perm,
            Rights rights,
            Rights inheritedRights,
            Rights restrictedRights = Rights.All)
        {
            ThrowIfDisposed();

            if (guestPath == "/dev/null")
            {
                return BindDevNull(perm);
            }

            var hostpath = _state.PathMapper.MapToHostPath(guestPath);
            if (!File.Exists(hostpath))
            {
                throw new FileNotFoundException($"The host file '{hostpath}' does not exist.");
            }

            rights &= restrictedRights;
            if (inheritedRights == Rights.None)
            {
                inheritedRights = rights;
            }
            else
            {
                rights = inheritedRights & rights;
            }

            fd newFd = _state.GetNextFd;
            var fileDescriptor = new FileDescriptor
            {
                Fd = newFd,
                Stream = filestream,
                Path = guestPath,
                Access = perm,
                IsPreopened = false,
                Type = Filetype.RegularFile,
                Rights = rights,
                InheritedRights = inheritedRights
            };

            _state.FileDescriptors[newFd] = fileDescriptor;
            return newFd;
        }

        private fd BindDevNull(FileAccess perm)
        {
            ThrowIfDisposed();

            var devNull = new NullStream();
            fd newFd = _state.GetNextFd;
            _state.FileDescriptors[newFd] = new FileDescriptor
            {
                Fd = newFd,
                Stream = devNull,
                Path = "/dev/null",
                Access = perm,
                IsPreopened = true,
                Type = Filetype.CharacterDevice
            };

            return newFd;
        }

        private void RemoveFd(fd fd)
        {
            ThrowIfDisposed();

            if (_state.FileDescriptors.TryRemove(fd, out var fileDescriptor))
            {
                // Only close the stream for real files (not directories or special devices).
                if (fileDescriptor.Type == Filetype.RegularFile)
                {
                    fileDescriptor.Dispose();
                }
            }
        }

        private void MoveFd(fd from, fd to)
        {
            ThrowIfDisposed();

            if (_state.FileDescriptors.ContainsKey(to))
            {
                throw new IOException($"Cannot overwrite existing file descriptor {to}.");
            }

            if (_state.FileDescriptors.TryRemove(from, out var fileDescriptor))
            {
                fileDescriptor.Fd = to;
                if (!_state.FileDescriptors.TryAdd(to, fileDescriptor))
                {
                    throw new IOException($"Failed to add file descriptor {to}: key already exists or concurrent modification detected.");
                }
            }
        }

        private void UnbindFile(string guestPath)
        {
            ThrowIfDisposed();

            var entry = _state.FileDescriptors.Values
                .FirstOrDefault(descriptor => descriptor.Path == guestPath);

            if (entry == null) return;

            _state.FileDescriptors.TryRemove(entry.Fd, out _);
        }

        private void BindStdio()
        {
            ThrowIfDisposed();

            fd newFd = _state.GetNextFd;
            if (newFd != 0)
            {
                // In a typical WASI environment, fd=0 is stdin, so we expect that to be the first.
                throw new InvalidDataException("Stdio should be bound first: fd=0 was not reserved.");
            }

            _state.FileDescriptors[newFd] = new FileDescriptor
            {
                Fd = newFd,
                Stream = _config.StandardInput,
                Path = "/dev/stdin",
                Access = FileAccess.Read,
                IsPreopened = IsStreamOpen(_config.StandardInput),
                Type = Filetype.CharacterDevice,
                Rights = Rights.FD_READ | Rights.PATH_OPEN
            };

            newFd = _state.GetNextFd; // Should become 1
            _state.FileDescriptors[newFd] = new FileDescriptor
            {
                Fd = newFd,
                Stream = _config.StandardOutput,
                Path = "/dev/stdout",
                Access = FileAccess.Write,
                IsPreopened = IsStreamOpen(_config.StandardOutput),
                Type = Filetype.CharacterDevice,
                Rights = Rights.FD_WRITE | Rights.PATH_OPEN
            };

            newFd = _state.GetNextFd; // Should become 2
            _state.FileDescriptors[newFd] = new FileDescriptor
            {
                Fd = newFd,
                Stream = _config.StandardError,
                Path = "/dev/stderr",
                Access = FileAccess.Write,
                IsPreopened = IsStreamOpen(_config.StandardError),
                Type = Filetype.CharacterDevice,
                Rights = Rights.FD_WRITE | Rights.PATH_OPEN
            };
        }

        public static bool IsStreamOpen(Stream? stream)
        {
            try
            {
                // If it can read, write, or seek, we treat it as open.
                return stream != null && (stream.CanRead || stream.CanWrite || stream.CanSeek);
            }
            catch (ObjectDisposedException)
            {
                // The stream has been disposed
                return false;
            }
            catch (NotSupportedException)
            {
                // The operation is not supported, but we cannot confirm it is closed.
                return false;
            }
            catch (Exception)
            {
                // Catch-all for other errors.
                return false;
            }
        }

        public bool GetFd(fd fd, out FileDescriptor fileDescriptor)
        {
            ThrowIfDisposed();

            if (_state.FileDescriptors.TryGetValue(fd, out var file))
            {
                fileDescriptor = file;
                return true;
            }

            fileDescriptor = FileDescriptor.BadFd;
            return false;
        }

        public bool GetFd(string path, out FileDescriptor fileDescriptor)
        {
            ThrowIfDisposed();

            var file = _state.FileDescriptors.Values
                .FirstOrDefault(descriptor => descriptor.Path == path);

            if (file != null)
            {
                fileDescriptor = file;
                return true;
            }
            else
            {
                fileDescriptor = FileDescriptor.BadFd;
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var fileDescriptor in _state.FileDescriptors.Values)
            {
                if (fileDescriptor.Type == Filetype.RegularFile)
                {
                    fileDescriptor.Dispose();
                }
            }

            _state.FileDescriptors.Clear();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new InvalidOperationException("This FileSystem instance has been disposed.");
            }
        }
    }
}
