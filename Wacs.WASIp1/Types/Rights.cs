using System;
using System.IO;
using System.Runtime.InteropServices;
using Wacs.Core.Attributes;
using Wacs.Core.Runtime;
using Wacs.Core.Types;

namespace Wacs.WASIp1.Types
{
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    [WasmType(nameof(ValType.I64))]
    public struct Rights : ITypeConvertable
    {
        [FieldOffset(0)] private ulong _flags;

        public bool this[int bit]
        {
            get => (_flags & (1uL << bit)) != 0;
            set => _flags = value ? (_flags | (1uL << bit)) : (_flags & ~(1uL << bit));
        }

        /// <summary>
        /// The right to invoke fd_datasync. If path_open is set, includes the right to invoke path_open with fdflags::dsync.
        /// </summary>
        public bool fd_datasync
        {
            get => this[0];
            set => this[0] = value;
        }

        /// <summary>
        /// The right to invoke fd_read and sock_recv. If rights::fd_seek is set, includes the right to invoke fd_pread.
        /// </summary>
        public bool fd_read
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_seek. This flag implies rights::fd_tell.
        /// </summary>
        public bool fd_seek
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_fdstat_set_flags.
        /// </summary>
        public bool fd_fdstat_set_flags
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_sync. If path_open is set, includes the right to invoke path_open with fdflags::rsync and fdflags::dsync.
        /// </summary>
        public bool fd_sync
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_seek in such a way that the file offset remains unaltered (i.e., whence::cur with offset zero), or to invoke fd_tell.
        /// </summary>
        public bool fd_tell
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_write and sock_send. If rights::fd_seek is set, includes the right to invoke fd_pwrite.
        /// </summary>
        public bool fd_write
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_advise.
        /// </summary>
        public bool fd_advise
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_allocate.
        /// </summary>
        public bool fd_allocate
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_create_directory.
        /// </summary>
        public bool path_create_directory
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// If path_open is set, the right to invoke path_open with oflags::creat.
        /// </summary>
        public bool path_create_file
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_link with the file descriptor as the source directory.
        /// </summary>
        public bool path_link_source
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_link with the file descriptor as the target directory.
        /// </summary>
        public bool path_link_target
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_open.
        /// </summary>
        public bool path_open
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_readdir.
        /// </summary>
        public bool fd_readdir
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_readlink.
        /// </summary>
        public bool path_readlink
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_rename with the file descriptor as the source directory.
        /// </summary>
        public bool path_rename_source
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_rename with the file descriptor as the target directory.
        /// </summary>
        public bool path_rename_target
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_filestat_get.
        /// </summary>
        public bool path_filestat_get
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to change a file's size. If path_open is set, includes the right to invoke path_open with oflags::trunc.
        /// Note: there is no function named path_filestat_set_size. This follows POSIX design.
        /// </summary>
        public bool path_filestat_set_size
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_filestat_set_times.
        /// </summary>
        public bool path_filestat_set_times
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_filestat_get.
        /// </summary>
        public bool fd_filestat_get
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_filestat_set_size.
        /// </summary>
        public bool fd_filestat_set_size
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke fd_filestat_set_times.
        /// </summary>
        public bool fd_filestat_set_times
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_symlink.
        /// </summary>
        public bool path_symlink
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_remove_directory.
        /// </summary>
        public bool path_remove_directory
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke path_unlink_file.
        /// </summary>
        public bool path_unlink_file
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// If rights::fd_read is set, includes the right to invoke poll_oneoff to subscribe to eventtype::fd_read.
        /// If rights::fd_write is set, includes the right to invoke poll_oneoff to subscribe to eventtype::fd_write.
        /// </summary>
        public bool poll_fd_readwrite
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke sock_shutdown.
        /// </summary>
        public bool sock_shutdown
        {
            get => this[1];
            set => this[1] = value;
        }
        /// <summary>
        /// The right to invoke sock_accept.
        /// </summary>
        public bool sock_accept
        {
            get => this[1];
            set => this[1] = value;
        }

        public FileAccess FileAccess =>
            (fd_read ? FileAccess.Read : 0) | (fd_write ? FileAccess.Write : 0);
        

        public void FromWasmValue(object value)
        {
            ulong bits = (ulong)value;
            byte[] bytes = BitConverter.GetBytes(bits);
            this = MemoryMarshal.Cast<byte, Rights>(bytes.AsSpan())[0];
        }

        public Value ToWasmType()
        {
            byte[] bytes = new byte[8];
            MemoryMarshal.Write(bytes, ref this);
            return MemoryMarshal.Read<ulong>(bytes);
        }
    }

}