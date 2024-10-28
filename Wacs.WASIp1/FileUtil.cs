using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Wacs.WASIp1.Types;

namespace Wacs.WASIp1
{
    public static class FileUtil
    {
        public static ulong GenerateInode(FileInfo fileInfo)
        {
            using (var sha256 = SHA256.Create())
            {
                // Combine file attributes
                string data = $"{fileInfo.FullName}|{fileInfo.CreationTimeUtc.Ticks}|{fileInfo.Length}";
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                // Use the first 8 bytes of the hash as the inode
                ulong inode = BitConverter.ToUInt64(hashBytes, 0);
                return inode;
            }
        }

        public static Filetype FiletypeFromInfo(FileInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            // Check if the file exists
            if (!info.Exists)
            {
                return Filetype.Unknown;
            }
        
            // Determine the type of file and return the corresponding Filetype enum value
            if ((info.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                return Filetype.Directory;
            }
            else if ((info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
            {
                return Filetype.SymbolicLink;
            }
            else if ((info.Attributes & FileAttributes.Device) == FileAttributes.Device)
            {
                // Command to differentiate between BlockDevice and CharacterDevice would go here
                // For now, we'll assume it's a character device
                return Filetype.CharacterDevice;
            }
            else if (info.Extension.Equals(".sock", StringComparison.OrdinalIgnoreCase))
            {
                return Filetype.SocketStream; // Assuming socket files might have this extension (for example)
            }
            else if (info.Length == 0)
            {
                // No content to determine regular file vs. socket
                return Filetype.RegularFile;
            }
            else
            {
                return Filetype.RegularFile;
            }
        }
    }
}