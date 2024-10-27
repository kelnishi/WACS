namespace Wacs.WASIp1.Types
{
    public enum Filetype : byte
    {
        /// <summary>
        /// The type of the file descriptor or file is unknown or is different from any of the other types specified.
        /// </summary>
        Unknown = 0,   
        
        /// <summary>
        /// The file descriptor or file refers to a block device inode.
        /// </summary>
        BlockDevice = 1,   
        
        /// <summary>
        /// The file descriptor or file refers to a character device inode.
        /// </summary>
        CharacterDevice = 2,   
        
        /// <summary>
        /// The file descriptor or file refers to a directory inode.
        /// </summary>
        Directory = 3,   
        
        /// <summary>
        /// The file descriptor or file refers to a regular file inode.
        /// </summary>
        RegularFile = 4,   
        
        /// <summary>
        /// The file descriptor or file refers to a datagram socket.
        /// </summary>
        SocketDgram = 5,   
        
        /// <summary>
        /// The file descriptor or file refers to a byte-stream socket.
        /// </summary>
        SocketStream = 6,   
        
        /// <summary>
        /// The file refers to a symbolic link inode.
        /// </summary>
        SymbolicLink = 7   
    }

}