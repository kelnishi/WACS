using System.Collections.Generic;
using System.IO;
using Wacs.WASIp1.Types;

namespace Wacs.WASIp1
{
    public class WasiConfiguration
    {
        public const long DefaultMaxFileSize = 10 * 1024 * 1024; //10MB
        public const int DefaultOperationSize = 1 * 1024 * 1024; //1MB
        public const int DefaultMaxOpenFiles = 128;

        public string HostRootDirectory { get; set; } = string.Empty;

        public List<PreopenedDirectory> PreopenedDirectories { get; set; } = new();

        public FileAccess DefaultPermissions { get; set; } = FileAccess.Read;

        public int MaxOpenFileDescriptors { get; set; } = DefaultMaxOpenFiles;
        public long MaxFileSize { get; set; } = DefaultMaxFileSize;
        public int MaxReadWriteOperationSize { get; set; } = DefaultOperationSize;

        public bool AllowSymbolicLinks { get; set; } = false;
        public bool AllowHardLinks { get; set; } = false;

        public bool AllowFileCreation { get; set; } = false;

        public bool AllowFileDeletion { get; set; } = false;

        public Stream StandardInput { get; set; } = Stream.Null;
        public Stream StandardOutput { get; set; } = Stream.Null;
        public Stream StandardError { get; set; } = Stream.Null;

        public List<string> Arguments { get; set; } = new();
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

        public bool AllowTimeAccess { get; set; } = true;
    }
}