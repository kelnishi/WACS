using System.Runtime.InteropServices;

namespace Wacs.WASIp1.Types
{
    [StructLayout(LayoutKind.Explicit)]
    public struct Rights
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
        public bool fd_datasync => this[0];

        /// <summary>
        /// The right to invoke fd_read and sock_recv. If rights::fd_seek is set, includes the right to invoke fd_pread.
        /// </summary>
        public bool fd_read => this[1];

        /// <summary>
        /// The right to invoke fd_seek. This flag implies rights::fd_tell.
        /// </summary>
        public bool fd_seek => this[2];

        /// <summary>
        /// The right to invoke fd_fdstat_set_flags.
        /// </summary>
        public bool fd_fdstat_set_flags => this[3];

        /// <summary>
        /// The right to invoke fd_sync. If path_open is set, includes the right to invoke path_open with fdflags::rsync and fdflags::dsync.
        /// </summary>
        public bool fd_sync => this[4];

        /// <summary>
        /// The right to invoke fd_seek in such a way that the file offset remains unaltered (i.e., whence::cur with offset zero), or to invoke fd_tell.
        /// </summary>
        public bool fd_tell => this[5];

        /// <summary>
        /// The right to invoke fd_write and sock_send. If rights::fd_seek is set, includes the right to invoke fd_pwrite.
        /// </summary>
        public bool fd_write => this[6];

        /// <summary>
        /// The right to invoke fd_advise.
        /// </summary>
        public bool fd_advise => this[7];

        /// <summary>
        /// The right to invoke fd_allocate.
        /// </summary>
        public bool fd_allocate => this[8];

        /// <summary>
        /// The right to invoke path_create_directory.
        /// </summary>
        public bool path_create_directory => this[9];

        /// <summary>
        /// If path_open is set, the right to invoke path_open with oflags::creat.
        /// </summary>
        public bool path_create_file => this[10];

        /// <summary>
        /// The right to invoke path_link with the file descriptor as the source directory.
        /// </summary>
        public bool path_link_source => this[11];

        /// <summary>
        /// The right to invoke path_link with the file descriptor as the target directory.
        /// </summary>
        public bool path_link_target => this[12];

        /// <summary>
        /// The right to invoke path_open.
        /// </summary>
        public bool path_open => this[13];

        /// <summary>
        /// The right to invoke fd_readdir.
        /// </summary>
        public bool fd_readdir => this[14];

        /// <summary>
        /// The right to invoke path_readlink.
        /// </summary>
        public bool path_readlink => this[15];

        /// <summary>
        /// The right to invoke path_rename with the file descriptor as the source directory.
        /// </summary>
        public bool path_rename_source => this[16];

        /// <summary>
        /// The right to invoke path_rename with the file descriptor as the target directory.
        /// </summary>
        public bool path_rename_target => this[17];

        /// <summary>
        /// The right to invoke path_filestat_get.
        /// </summary>
        public bool path_filestat_get => this[18];

        /// <summary>
        /// The right to change a file's size. If path_open is set, includes the right to invoke path_open with oflags::trunc.
        /// Note: there is no function named path_filestat_set_size. This follows POSIX design.
        /// </summary>
        public bool path_filestat_set_size => this[19];

        /// <summary>
        /// The right to invoke path_filestat_set_times.
        /// </summary>
        public bool path_filestat_set_times => this[20];

        /// <summary>
        /// The right to invoke fd_filestat_get.
        /// </summary>
        public bool fd_filestat_get => this[21];

        /// <summary>
        /// The right to invoke fd_filestat_set_size.
        /// </summary>
        public bool fd_filestat_set_size => this[22];

        /// <summary>
        /// The right to invoke fd_filestat_set_times.
        /// </summary>
        public bool fd_filestat_set_times => this[23];

        /// <summary>
        /// The right to invoke path_symlink.
        /// </summary>
        public bool path_symlink => this[24];

        /// <summary>
        /// The right to invoke path_remove_directory.
        /// </summary>
        public bool path_remove_directory => this[25];

        /// <summary>
        /// The right to invoke path_unlink_file.
        /// </summary>
        public bool path_unlink_file => this[26];

        /// <summary>
        /// If rights::fd_read is set, includes the right to invoke poll_oneoff to subscribe to eventtype::fd_read.
        /// If rights::fd_write is set, includes the right to invoke poll_oneoff to subscribe to eventtype::fd_write.
        /// </summary>
        public bool poll_fd_readwrite => this[27];

        /// <summary>
        /// The right to invoke sock_shutdown.
        /// </summary>
        public bool sock_shutdown => this[28];

        /// <summary>
        /// The right to invoke sock_accept.
        /// </summary>
        public bool sock_accept => this[29];
    }

    
}