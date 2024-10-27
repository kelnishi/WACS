using System.IO;

namespace Wacs.WASIp1.Types
{
    public class FileDescriptor
    {
        public int Fd { get; set; }
        public Stream Stream { get; set; }
        public string Path { get; set; }
        public FileAccess Access { get; set; }
        public bool IsPreopened { get; set; }
    }
}