using System;
using System.IO;

namespace Wacs.WASIp1.Types
{
    public class FileDescriptor
    {
        public uint Fd { get; set; }
        public Stream Stream { get; set; } = Stream.Null;

        //Guest Path
        public string Path { get; set; } = String.Empty;

        public Filetype Type { get; set; }

        public FileAccess Access { get; set; }
        public bool IsPreopened { get; set; }

        public bool AllowFileCreation { get; set; }
        public bool AllowFileDeletion { get; set; }

        public Rights Rights { get; set; }


        public Rights InheritedRights { get; set; }

        public void SetFileRights(FileInfo fileInfo)
        {
            // Determine the appropriate access rights based on FileInfo attributes
            if (fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly) && Access.HasFlag(FileAccess.Read))
            {
                // If the file is read-only, override Access to only allow read
                Access = FileAccess.Read; // This grants read access, but no write
            }

            // Set rights based on the modified Access value
            Rights rights = new Rights();

            // Set rights based on the modified Access enum
            if ((Access & FileAccess.Read) != 0)
            {
                rights.fd_read = true;
                rights.path_readlink = true; // Allow reading symlinks
                rights.fd_filestat_get = true; // Allow fetching the file status
                rights.fd_readdir = (Type == Filetype.Directory); // Grant readdir if it's a directory
            }

            if ((Access & FileAccess.Write) != 0)
            {
                rights.fd_write = true;
                rights.fd_filestat_set_size = true; // Allow changing file size if applicable
            }

            if ((Access & FileAccess.ReadWrite) != 0)
            {
                rights.fd_read = true;
                rights.fd_write = true;
                rights.fd_seek = true; // Allow seeking if read/write access is present
                rights.fd_tell = true; // Tell is also applicable in read/write mode
            }

            // Set additional rights based on the type of Stream
            if (Stream is FileStream fileStream)
            {
                if (fileStream.CanRead)
                {
                    rights.fd_read = true;
                }

                if (fileStream.CanWrite)
                {
                    rights.fd_write = true;
                }

                if (fileStream.CanSeek) // Check if the stream supports seeking
                {
                    rights.fd_seek = true;
                    rights.fd_tell = true; // Tell is implied if we can seek
                }
            }

            // Handle directory-specific rights
            if (Type == Filetype.Directory)
            {
                rights.path_create_directory = AllowFileCreation;
                rights.path_remove_directory = AllowFileDeletion;
                rights.path_open = true; // Allow opening directories
                rights.path_create_file = AllowFileCreation; // Allow creating files in directory
                rights.fd_readdir = true; // Grant read rights if it's a directory
            }

            // Optionally add any additional rights that might be universally applicable
            rights.fd_sync = true; // or make conditional based on context
            rights.fd_datasync = true; // or make conditional based on context

            Rights = rights;
        }
    }
}