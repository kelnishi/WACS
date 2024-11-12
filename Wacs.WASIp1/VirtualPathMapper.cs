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
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Wacs.WASIp1
{
    public class VirtualPathMapper
    {
        private readonly Dictionary<string, string> _directoryMappings;
        private string _rootMapping = "/";

        public VirtualPathMapper()
        {
            _directoryMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public void SetRootMapping(string hostPath)
        {
            if (string.IsNullOrWhiteSpace(hostPath))
                throw new ArgumentException("Root path cannot be empty", nameof(hostPath));

            _rootMapping = Path.GetFullPath(hostPath).TrimEnd(Path.DirectorySeparatorChar);
        }

        public void AddDirectoryMapping(string guestPath, string hostPath)
        {
            if (string.IsNullOrWhiteSpace(guestPath))
                throw new ArgumentException("Guest path cannot be empty", nameof(guestPath));
            if (string.IsNullOrWhiteSpace(hostPath))
                throw new ArgumentException("Host path cannot be empty", nameof(hostPath));

            var normalizedGuestPath = NormalizePath(guestPath);
            var normalizedHostPath = Path.GetFullPath(hostPath).TrimEnd(Path.DirectorySeparatorChar);

            _directoryMappings[normalizedGuestPath] = normalizedHostPath;
        }

        public bool TryRemoveMapping(string guestPath)
        {
            if (string.IsNullOrWhiteSpace(guestPath))
                throw new ArgumentException("Guest path cannot be empty", nameof(guestPath));

            var normalizedGuestPath = NormalizePath(guestPath);
            return _directoryMappings.Remove(normalizedGuestPath);
        }

        public void MoveHostPath(string oldHostPath, string newHostPath)
        {
            if (string.IsNullOrWhiteSpace(oldHostPath))
                throw new ArgumentException("Old host path cannot be empty", nameof(oldHostPath));
            if (string.IsNullOrWhiteSpace(newHostPath))
                throw new ArgumentException("New host path cannot be empty", nameof(newHostPath));

            var normalizedOldPath = Path.GetFullPath(oldHostPath).TrimEnd(Path.DirectorySeparatorChar);
            var normalizedNewPath = Path.GetFullPath(newHostPath).TrimEnd(Path.DirectorySeparatorChar);

            // Update root mapping if it matches
            if (_rootMapping?.Equals(normalizedOldPath, StringComparison.OrdinalIgnoreCase) == true)
            {
                _rootMapping = normalizedNewPath;
            }

            // Find all mappings that start with the old path
            var affectedMappings = _directoryMappings
                .Where(kvp => kvp.Value.StartsWith(normalizedOldPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Update each affected mapping
            foreach (var mapping in affectedMappings)
            {
                var relativePath = mapping.Value.Substring(normalizedOldPath.Length);
                var newFullPath = Path.Combine(normalizedNewPath, relativePath.TrimStart('\\'));
                _directoryMappings[mapping.Key] = newFullPath.TrimEnd(Path.DirectorySeparatorChar);
            }
        }

        public string MapToHostPath(string guestPath)
        {
            if (string.IsNullOrWhiteSpace(guestPath))
                throw new ArgumentException("Guest path cannot be empty", nameof(guestPath));

            var normalizedGuestPath = NormalizePath(guestPath);

            // Check specific mappings first
            var matchingMapping = _directoryMappings
                .OrderByDescending(m => m.Key.Length)
                .FirstOrDefault(m => normalizedGuestPath.StartsWith(m.Key, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(matchingMapping.Key))
            {
                var relativePath = normalizedGuestPath.Substring(matchingMapping.Key.Length).TrimStart('/');
                return Path.Combine(matchingMapping.Value, relativePath);
            }

            // Fall back to root mapping if available
            if (!string.IsNullOrEmpty(_rootMapping))
            {
                return Path.Combine(_rootMapping, normalizedGuestPath.TrimStart('/'));
            }

            throw new DirectoryNotFoundException($"No mapping found for guest path: {guestPath}");
        }

        public string MapToGuestPath(string hostPath)
        {
            if (string.IsNullOrWhiteSpace(hostPath))
                throw new ArgumentException("Host path cannot be empty", nameof(hostPath));

            var normalizedHostPath = Path.GetFullPath(hostPath);

            // Check specific mappings first
            var matchingMapping = _directoryMappings
                .OrderByDescending(m => m.Value.Length)
                .FirstOrDefault(m => normalizedHostPath.StartsWith(m.Value, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(matchingMapping.Key))
            {
                var relativePath = normalizedHostPath.Substring(matchingMapping.Value.Length);
                return Path.Combine(matchingMapping.Key, relativePath).Replace('\\', '/');
            }

            // Fall back to root mapping if available
            if (!string.IsNullOrEmpty(_rootMapping) && normalizedHostPath.StartsWith(_rootMapping, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = normalizedHostPath.Substring(_rootMapping.Length);
                return "/" + relativePath.TrimStart('\\').Replace('\\', '/');
            }

            throw new DirectoryNotFoundException($"No mapping found for host path: {hostPath}");
        }

        private string NormalizePath(string path)
        {
            return "/" + path.Trim('/').Replace('\\', '/');
        }
    }
}
