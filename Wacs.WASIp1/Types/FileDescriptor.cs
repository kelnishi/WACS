using System;
using System.IO;

namespace Wacs.WASIp1.Types
{
    public class FileDescriptor
    {
        public static readonly FileDescriptor BadFd = new() { Fd = uint.MaxValue };

        public uint Fd { get; set; }
        public Stream Stream { get; set; } = Stream.Null;

        //Guest Path
        public string Path { get; set; } = String.Empty;

        public Filetype Type { get; set; }

        public FileAccess Access { get; set; }
        public bool IsPreopened { get; set; }

        public Rights Rights { get; set; }

        public Rights InheritedRights { get; set; }

        public static Rights ComputeFileRights(FileInfo fileInfo, Filetype type, FileAccess access, Stream stream, bool allowFileCreation, bool allowFileDeletion)
        {
            // Set rights based on the modified Access value
            Rights rights = new Rights();
            
            // Set rights based on the modified Access enum
            if ((access & FileAccess.Read) != 0)
            {
                rights |= Rights.FD_READ;
                rights |= Rights.PATH_READLINK; // Allow reading symlinks
                rights |= Rights.FD_FILESTAT_GET;
                if (type == Filetype.Directory)
                    rights |= Rights.FD_READDIR;
            }

            if ((access & FileAccess.Write) != 0)
            {
                rights |= Rights.FD_WRITE;
                rights |= Rights.FD_FILESTAT_SET_SIZE;
            }

            if ((access & FileAccess.ReadWrite) != 0)
            {
                rights |= Rights.FD_READ;
                rights |= Rights.FD_WRITE;
                rights |= Rights.FD_SEEK;
                rights |= Rights.FD_TELL;
            }

            // Set additional rights based on the type of Stream
            if (stream is FileStream fileStream)
            {
                if (fileStream.CanRead)
                {
                    rights |= Rights.FD_READ;
                }

                if (fileStream.CanWrite)
                {
                    rights |= Rights.FD_WRITE;
                }

                if (fileStream.CanSeek) // Check if the stream supports seeking
                {
                    rights |= Rights.FD_SEEK;
                    rights |= Rights.FD_TELL; // Tell is implied if we can seek
                }
            }

            // Handle directory-specific rights
            if (type == Filetype.Directory)
            {
                if (allowFileCreation)
                    rights |= Rights.PATH_CREATE_DIRECTORY | Rights.PATH_CREATE_FILE;
                if (allowFileDeletion)
                    rights |= Rights.PATH_REMOVE_DIRECTORY | Rights.PATH_UNLINK_FILE;

                rights |= Rights.PATH_OPEN;
                rights |= Rights.FD_READDIR;
            }

            // Optionally add any additional rights that might be universally applicable
            rights |= Rights.FD_SYNC; // or make conditional based on context
            rights |= Rights.FD_DATASYNC; // or make conditional based on context
            
            // Determine the appropriate access rights based on FileInfo attributes
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly) && access.HasFlag(FileAccess.Read))
            {
                // If the file is read-only, override Access to only allow read
                rights &= ~Rights.FD_WRITE;
            }
            
            return rights;
        }
    }
}