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
//Interop should use plain numeric types, functions are responsible for marshaling.
using ptr = System.UInt32;
using fd = System.UInt32;
using filesize = System.UInt64;
using size = System.UInt32;
using timestamp = System.Int64;
using dircookie = System.UInt64;
using filedelta = System.Int64;

namespace Wacs.WASIp1
{
    public partial class Filesystem : IBindable
    {
        private readonly WasiConfiguration _config;
        private readonly State _state;

        public Filesystem(WasiConfiguration config ,State state)
        {
            _config = config;
            _state = state;

            InitializeFs();
        }

        public void BindToRuntime(WasmRuntime runtime)
        {
            string module = "wasi_snapshot_preview1";
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, ErrNo>>(
                (module, "path_create_directory"), PathCreateDirectory);
            runtime.BindHostFunction<Func<ExecContext, fd, LookupFlags, ptr, size, ptr, ErrNo>>(
                (module, "path_filestat_get"), PathFilestatGet);
            runtime.BindHostFunction<Func<ExecContext, fd, LookupFlags, ptr, size, timestamp, timestamp, FstFlags, ErrNo>>(
                (module, "path_filestat_set_times"), PathFilestatSetTimes);
            runtime.BindHostFunction<Func<ExecContext, fd, LookupFlags, ptr, size, fd, ptr, size, ErrNo>>(
                (module, "path_link"), PathLink);
            //HACK: 10 parameters + return -> dotnet SEGFAULT!, so we're using outvars to return
            // *outvars require defining our own delegate (can't use Action<T>).
            runtime.BindHostFunction<PathOpenDelegate>(
                (module, "path_open"), PathOpen);
            
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, ptr, size, ptr, ErrNo>>(
                (module, "path_readlink"), PathReadlink);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, ErrNo>>(
                (module, "path_remove_directory"), PathRemoveDirectory);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, fd, ptr, size, ErrNo>>(
                (module, "path_rename"), PathRename);
            runtime.BindHostFunction<Func<ExecContext, ptr, size, fd, ptr, size, ErrNo>>(
                (module, "path_symlink"), PathSymlink);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, ErrNo>>(
                (module, "path_unlink_file"), PathUnlinkFile);
            runtime.BindHostFunction<Func<ExecContext, fd, filesize, filesize, Advice, ErrNo>>(
                (module, "fd_advise"), FdAdvise);
            runtime.BindHostFunction<Func<ExecContext, fd, filesize, filesize, ErrNo>>(
                (module, "fd_allocate"), FdAllocate);
            runtime.BindHostFunction<Func<ExecContext, fd, ErrNo>>(
                (module, "fd_close"), FdClose);
            runtime.BindHostFunction<Func<ExecContext, fd, ErrNo>>(
                (module, "fd_datasync"), FdDatasync);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, ErrNo>>(
                (module, "fd_fdstat_get"), FdFdstatGet);
            runtime.BindHostFunction<Func<ExecContext, fd, FdFlags, ErrNo>>(
                (module, "fd_fdstat_set_flags"), FdFdstatSetFlags);
            runtime.BindHostFunction<Func<ExecContext, fd, Rights, Rights, ErrNo>>(
                (module, "fd_fdstat_set_rights"), FdFdstatSetRights);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, ErrNo>>(
                (module, "fd_filestat_get"), FdFilestatGet);
            runtime.BindHostFunction<Func<ExecContext, fd, filesize, ErrNo>>(
                (module, "fd_filestat_set_size"), FdFilestatSetSize);
            runtime.BindHostFunction<Func<ExecContext, fd, timestamp, timestamp, FstFlags, ErrNo>>(
                (module, "fd_filestat_set_times"), FdFilestatSetTimes);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, filesize, ptr, ErrNo>>(
                (module, "fd_pread"), FdPread);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, ErrNo>>(
                (module, "fd_prestat_get"), FdPrestatGet);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, ErrNo>>(
                (module, "fd_prestat_dir_name"), FdPrestatDirName);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, filesize, ptr, ErrNo>>(
                (module, "fd_pwrite"), FdPwrite);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, ptr, ErrNo>>(
                (module, "fd_read"), FdRead);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, dircookie, ptr, ErrNo>>(
                (module, "fd_readdir"), FdReaddir);
            runtime.BindHostFunction<Func<ExecContext, fd, fd, ErrNo>>(
                (module, "fd_renumber"), FdRenumber);
            runtime.BindHostFunction<Func<ExecContext, fd, filedelta, Whence, ptr, ErrNo>>(
                (module, "fd_seek"), FdSeek);
            runtime.BindHostFunction<Func<ExecContext, fd, ErrNo>>(
                (module, "fd_sync"), FdSync);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, ErrNo>>(
                (module, "fd_tell"), FdTell);
            runtime.BindHostFunction<Func<ExecContext, fd, ptr, size, ptr, ErrNo>>(
                (module, "fd_write"), FdWrite);
        }

        private void InitializeFs()
        {
            BindStdio();
            BindPreopenedDirs();
        }

        private void BindPreopenedDirs()
        {
            if (string.IsNullOrWhiteSpace(_config.HostRootDirectory))
                throw new DirectoryNotFoundException($"The host path '{_config.HostRootDirectory}' does not exist.");
            
            _state.PathMapper.SetRootMapping(_config.HostRootDirectory);
            
            //Bind the HostRootDir
            BindDir(_config.HostRootDirectory, "/", _config.DefaultPermissions, true);
            
            foreach (var pod in _config.PreopenedDirectories)
            {
                BindDir(pod.HostPath,pod.GuestPath, pod.Permissions, true);
            }
        }

        private fd BindDir(string hostDir, string guestDir, FileAccess perm, bool isPreopened, Rights restrictedRights = Rights.All, Rights inheritedRights = Rights.None)
        {
            if (_state.FileDescriptors.Count >= _config.MaxOpenFileDescriptors)
            {
                throw new InvalidOperationException($"File descriptor count would exceed the maximum limit of {_config.MaxOpenFileDescriptors}.");
            }
            fd fd = _state.GetNextFd;
            
            if (!Directory.Exists(hostDir))
            {
                throw new DirectoryNotFoundException($"The host path '{hostDir}' does not exist.");
            }
            if (string.IsNullOrWhiteSpace(guestDir))
            {
                throw new DirectoryNotFoundException($"The guest path '{guestDir}' cannot be created.");
            }
            if (!guestDir.StartsWith("/"))
            {
                guestDir = "/" + guestDir;
            }
            if (guestDir.StartsWith("/dev"))
            {
                throw new UnauthorizedAccessException($"The guest path '{guestDir}' is not allowed.");
            }
            
            _state.PathMapper.AddDirectoryMapping(guestDir, hostDir);
            var fileDescriptor = new FileDescriptor
            {
                Fd = fd,
                Stream = Stream.Null,
                Path = guestDir,
                Access = perm,
                IsPreopened = isPreopened,
                Type = Filetype.Directory,
            };

            var fileInfo = new FileInfo(hostDir);
            var rights = FileDescriptor.ComputeFileRights(
                fileInfo, 
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
            
            _state.FileDescriptors[fd] = fileDescriptor;
            return fd;
        }

        private void UnbindDir(string guestDir)
        {
            if (!_state.PathMapper.TryRemoveMapping(guestDir))
            {
                throw new DirectoryNotFoundException($"The guest path '{guestDir}' cannot be unbound because it does not exist.");
            }

            var entry = _state.FileDescriptors.Values
                .FirstOrDefault(fd => fd.Path == guestDir);

            if (entry == null)
                return;

            if (!_state.FileDescriptors.TryRemove(entry.Fd, out var _))
            {
                //Complain?
            }
        }

        private fd BindFile(string guestPath, Stream filestream, FileAccess perm, Rights rights, Rights inheritedRights)
        {
            if (guestPath == "/dev/null")
            {
                return BindDevNull(perm);
            }
            
            var hostpath = _state.PathMapper.MapToHostPath(guestPath);
            if (!File.Exists(hostpath))
            {
                throw new FileNotFoundException($"The host file '{hostpath}' does not exist.");
            }

            fd fd = _state.GetNextFd; 
            _state.FileDescriptors[fd] = new FileDescriptor
            {
                Fd = fd,
                Stream = filestream,
                Path = guestPath,
                Access = perm,
                IsPreopened = false,
                Type = Filetype.RegularFile,
                Rights = rights,
                InheritedRights = inheritedRights,
            };
            return fd;
        }

        private fd BindDevNull(FileAccess perm)
        {
            var devNull = new NullStream();
            fd fd = _state.GetNextFd;
            _state.FileDescriptors[fd] = new FileDescriptor
            {
                Fd = fd,
                Stream = devNull,
                Path = "/dev/null",
                Access = perm,
                IsPreopened = true,
                Type = Filetype.CharacterDevice
            };
            return fd;
        }

        /// <summary>
        /// Closes the Stream and removes the FileDescriptor
        /// </summary>
        /// <param name="fd"></param>
        private void RemoveFd(fd fd)
        {
            if (_state.FileDescriptors.TryRemove(fd, out var fileDescriptor))
            {
                if (fileDescriptor.Type == Filetype.RegularFile)
                    fileDescriptor.Stream.Close();
            }
        }

        private void MoveFd(fd from, fd to)
        {
            if (_state.FileDescriptors.ContainsKey(to))
                throw new IOException($"Cannot overwrite existing file descriptor {to}");
            
            if (_state.FileDescriptors.TryRemove(from, out var fileDescriptor))
            {
                fileDescriptor.Fd = to;
                _state.FileDescriptors[to] = fileDescriptor;
            }
        }

        private void UnbindFile(string guestPath)
        {
            var entry = _state.FileDescriptors.Values
                .FirstOrDefault(fd => fd.Path == guestPath);
            
            if (entry == null)
                return;

            if (!_state.FileDescriptors.TryRemove(entry.Fd, out var _))
            {
                //Complain?
            }
        }

        private void BindStdio()
        {
            fd fd = _state.GetNextFd;
            if (fd != 0)
                throw new InvalidDataException($"Stdio should be bound first.");
            
            _state.FileDescriptors[fd] = new FileDescriptor
            {
                Fd = fd,
                Stream = _config.StandardInput,
                Path = "/dev/stdin",
                Access = FileAccess.Read,
                IsPreopened = IsStreamOpen(_config.StandardInput),
                Type = Filetype.CharacterDevice,
                Rights = Rights.FD_READ | Rights.PATH_OPEN,
            };
            fd = _state.GetNextFd;
            _state.FileDescriptors[fd] = new FileDescriptor
            {
                Fd = fd,
                Stream = _config.StandardOutput,
                Path = "/dev/stdout",
                Access = FileAccess.Write,
                IsPreopened = IsStreamOpen(_config.StandardOutput),
                Type = Filetype.CharacterDevice,
                Rights = Rights.FD_WRITE | Rights.PATH_OPEN,
            };
            fd = _state.GetNextFd;
            _state.FileDescriptors[fd] = new FileDescriptor
            {
                Fd = fd,
                Stream = _config.StandardError,
                Path = "/dev/stderr",
                Access = FileAccess.Write,
                IsPreopened = IsStreamOpen(_config.StandardError),
                Type = Filetype.CharacterDevice,
                Rights = Rights.FD_WRITE | Rights.PATH_OPEN,
            };
        }

        public static bool IsStreamOpen(Stream? stream)
        {
            try
            {
                // Check read capabilities, which usually imply the stream is open for reading.
                // Similarly, check for writing capabilities.
                return stream != null && (stream.CanRead || stream.CanWrite || stream.CanSeek);
            }
            catch (ObjectDisposedException)
            {
                // The stream has been disposed
                return false;
            }
            catch (NotSupportedException)
            {
                // The operation is not supported, but it doesn't indicate if the Stream is open/closed.
                return false;
            }
            catch (Exception)
            {
                // Handle any other exceptions that may arise.
                return false;
            }
        }

        public bool GetFd(fd fd, out FileDescriptor fileDescriptor)
        {
            
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
            var file = _state.FileDescriptors.Values.FirstOrDefault(fd => fd.Path == path);
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
    }
}