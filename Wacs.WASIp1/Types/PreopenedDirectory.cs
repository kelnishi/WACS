using System.IO;

namespace Wacs.WASIp1.Types
{
    public class PreopenedDirectory
    {
        public string HostPath { get; set; }    // The path on the host filesystem
        public string GuestPath { get; set; }   // The path as seen by the WASM module
        public FileAccess Permissions { get; set; } // Read/Write permissions

        public bool AllowFileCreation { get; set; } = true;
        public bool AllowFileDeletion { get; set; } = true;
    }
}