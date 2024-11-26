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
                string data = $"{fileInfo.FullName}|{fileInfo.CreationTimeUtc.Ticks}|{(fileInfo.Exists ? fileInfo.Length : 0L)}";
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