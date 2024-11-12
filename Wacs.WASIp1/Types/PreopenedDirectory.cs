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

using System.IO;

namespace Wacs.WASIp1.Types
{
    public class PreopenedDirectory
    {
        public PreopenedDirectory(WasiConfiguration config, string path)
        {
            HostPath = Path.GetFullPath(Path.Combine(config.HostRootDirectory, path));
            
            if (!Directory.Exists(HostPath))
            {
                throw new DirectoryNotFoundException($"The directory '{Path.Combine(config.HostRootDirectory, path)}' does not exist.");
            }

            GuestPath = Path.GetFullPath(Path.Combine("/", path));

            Permissions = config.DefaultPermissions;
        }

        public string HostPath { get; set; } = "";    // The path on the host filesystem
        public string GuestPath { get; set; } = "";   // The path as seen by the WASM module
        public FileAccess Permissions { get; set; } // Read/Write permissions

        public bool AllowFileCreation { get; set; } = true;
        public bool AllowFileDeletion { get; set; } = true;
    }
}