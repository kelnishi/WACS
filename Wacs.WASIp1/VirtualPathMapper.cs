/*
 * Copyright 2024 Kelvin Nishikawa
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Wacs.Core.WASIp1;
using Wacs.WASIp1.Types;

namespace Wacs.WASIp1
{
    /// <summary>
    /// Manages mappings between guest paths and host file system paths.
    /// </summary>
    public class VirtualPathMapper
    {
        private readonly Dictionary<string, string> _directoryMappings;
        private string _rootMapping = "/";

        /// <summary>
        /// Initializes a new instance of the <see cref="VirtualPathMapper"/> class.
        /// </summary>
        public VirtualPathMapper()
        {
            _directoryMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets the root directory mapping on the host file system.
        /// </summary>
        /// <param name="hostPath">Absolute path on the host.</param>
        public void SetRootMapping(string hostPath)
        {
            if (string.IsNullOrWhiteSpace(hostPath))
                throw new ArgumentException("Root path cannot be empty", nameof(hostPath));

            _rootMapping = NormalizeHostRoot(hostPath);
        }

        /// <summary>
        /// Adds a custom directory mapping from guest to host.
        /// </summary>
        /// <param name="guestPath">Path in the guest file system.</param>
        /// <param name="hostPath">Absolute path on the host.</param>
        public void AddDirectoryMapping(string guestPath, string hostPath)
        {
            if (string.IsNullOrWhiteSpace(guestPath))
                throw new ArgumentException("Guest path cannot be empty", nameof(guestPath));
            if (string.IsNullOrWhiteSpace(hostPath))
                throw new ArgumentException("Host path cannot be empty", nameof(hostPath));

            var normalizedGuestPath = NormalizeAndValidateGuestPath(guestPath);
            var normalizedHostPath = NormalizeHostRoot(hostPath);

            _directoryMappings[normalizedGuestPath] = normalizedHostPath;
        }

        /// <summary>
        /// Removes an existing directory mapping, if present.
        /// </summary>
        /// <param name="guestPath">Path in the guest file system.</param>
        /// <returns>True if the mapping was removed; otherwise false.</returns>
        public bool TryRemoveMapping(string guestPath)
        {
            if (string.IsNullOrWhiteSpace(guestPath))
                throw new ArgumentException("Guest path cannot be empty", nameof(guestPath));

            var normalizedGuestPath = NormalizeAndValidateGuestPath(guestPath);
            return _directoryMappings.Remove(normalizedGuestPath);
        }

        /// <summary>
        /// Moves an existing mapped host path to a new location and updates all affected mappings.
        /// </summary>
        /// <param name="oldHostPath">Current host path to be moved.</param>
        /// <param name="newHostPath">New host path location.</param>
        public void MoveHostPath(string oldHostPath, string newHostPath)
        {
            if (string.IsNullOrWhiteSpace(oldHostPath))
                throw new ArgumentException("Old host path cannot be empty", nameof(oldHostPath));
            if (string.IsNullOrWhiteSpace(newHostPath))
                throw new ArgumentException("New host path cannot be empty", nameof(newHostPath));

            var normalizedOldPath = NormalizeHostRoot(oldHostPath);
            var normalizedNewPath = NormalizeHostRoot(newHostPath);

            if (_rootMapping?.Equals(normalizedOldPath, StringComparison.OrdinalIgnoreCase) == true)
            {
                _rootMapping = normalizedNewPath;
            }

            var affectedMappings = _directoryMappings
                .Where(kvp => kvp.Value.StartsWith(normalizedOldPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var mapping in affectedMappings)
            {
                var relativePath = mapping.Value
                    .Substring(normalizedOldPath.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                var newFullPath = Path.Combine(normalizedNewPath, relativePath);
                newFullPath = NormalizeHostRoot(newFullPath);
                _directoryMappings[mapping.Key] = newFullPath;
            }
        }

        /// <summary>
        /// Converts a guest path to the corresponding host path and performs symlink resolution if available.
        /// </summary>
        /// <param name="guestPath">The path in the guest file system.</param>
        /// <returns>The resolved absolute path on the host.</returns>
        public string MapToHostPath(string guestPath)
        {
            if (string.IsNullOrWhiteSpace(guestPath))
                throw new ArgumentException("Guest path cannot be empty", nameof(guestPath));

            var normalizedGuestPath = NormalizeAndValidateGuestPath(guestPath);
            var trimmed = normalizedGuestPath.TrimStart('/');
            var segments = trimmed.Split('/', 2, StringSplitOptions.None);

            if (segments.Length > 0 && string.Equals(segments[0], "dev", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedGuestPath;
            }

            var matchingMapping = _directoryMappings
                .OrderByDescending(m => m.Key.Length)
                .FirstOrDefault(m => normalizedGuestPath.StartsWith(m.Key, StringComparison.OrdinalIgnoreCase));

            string hostBase;
            string relativePath;

            if (!string.IsNullOrEmpty(matchingMapping.Key))
            {
                relativePath = normalizedGuestPath.Substring(matchingMapping.Key.Length).TrimStart('/');
                hostBase = matchingMapping.Value;
            }
            else if (!string.IsNullOrEmpty(_rootMapping))
            {
                relativePath = normalizedGuestPath.TrimStart('/');
                hostBase = _rootMapping;
            }
            else
            {
                throw new SandboxError(ErrNo.NotDir, $"No mapping found for guest path: {guestPath}");
            }

            var combined = Path.Combine(hostBase, relativePath);
            var fullPath = Path.GetFullPath(combined);

            if (!PathInsideBase(fullPath, hostBase))
            {
                throw new SandboxError(
                    ErrNo.Acces,
                    $"Path resolution escaped sandbox: '{fullPath}' is outside '{hostBase}'");
            }

            var resolved = ResolveSymbolicLinks(fullPath, hostBase);

            if (!PathInsideBase(resolved, hostBase))
            {
                throw new SandboxError(
                    ErrNo.Acces,
                    $"Symlink target '{resolved}' is outside sandbox base '{hostBase}'");
            }

            return resolved;
        }

        /// <summary>
        /// Converts a host path back to the corresponding guest path (if it exists in the mappings).
        /// </summary>
        /// <param name="hostPath">The absolute path on the host file system.</param>
        /// <returns>The mapped guest path.</returns>
        public string MapToGuestPath(string hostPath)
        {
            if (string.IsNullOrWhiteSpace(hostPath))
                throw new ArgumentException("Host path cannot be empty", nameof(hostPath));

            var normalizedHostPath = NormalizeHostRoot(hostPath);

            var matchingMapping = _directoryMappings
                .OrderByDescending(m => m.Value.Length)
                .FirstOrDefault(m => normalizedHostPath.StartsWith(m.Value, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(matchingMapping.Key))
            {
                var relativePath = normalizedHostPath
                    .Substring(matchingMapping.Value.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                var guest = Path.Combine(matchingMapping.Key, relativePath).Replace('\\', '/');
                return NormalizeAndValidateGuestPath(guest);
            }

            if (!string.IsNullOrEmpty(_rootMapping) &&
                normalizedHostPath.StartsWith(_rootMapping, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = normalizedHostPath
                    .Substring(_rootMapping.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                var guest = "/" + relativePath.Replace('\\', '/');
                return NormalizeAndValidateGuestPath(guest);
            }

            throw new SandboxError(ErrNo.NoEnt, $"No mapping found for host path: {hostPath}");
        }

        /// <summary>
        /// Iteratively resolves symlinks when running on .NET 6 or greater.
        /// On earlier versions, returns the path as-is since symlink resolution isn't available.
        /// </summary>
        /// <param name="path">Absolute path to start with.</param>
        /// <param name="sandboxBase">Sandbox base path for safety checks.</param>
        /// <param name="maxDepth">Maximum number of symlinks to resolve.</param>
        /// <returns>The resolved path, or the original path if not on .NET 8 or no symlinks found.</returns>
        public static string ResolveSymbolicLinks(string path, string sandboxBase, int maxDepth = 40)
        {
#if NET6_0_OR_GREATER
            string current = path;
            for (int i = 0; i < maxDepth; i++)
            {
                if (!FileOrDirectoryExists(current))
                {
                    return current;
                }

                var fsi = new FileInfo(current);
                if (fsi.LinkTarget is null)
                {
                    return current;
                }

                string target = fsi.LinkTarget;
                if (string.IsNullOrEmpty(target))
                {
                    return current;
                }

                if (Path.IsPathRooted(target))
                {
                    throw new SandboxError(
                        ErrNo.Acces,
                        $"Symlink '{current}' points to an absolute path '{target}' which is not permitted.");
                }

                var dirPart = Path.GetDirectoryName(current);
                var newPath = Path.Combine(dirPart ?? "", target);
                var resolvedFull = Path.GetFullPath(newPath);

                if (!PathInsideBase(resolvedFull, sandboxBase))
                {
                    throw new SandboxError(
                        ErrNo.Acces,
                        $"Symlink '{current}' points outside sandbox: '{resolvedFull}' not under '{sandboxBase}'");
                }

                current = resolvedFull;
            }

            throw new SandboxError(ErrNo.Loop, "Too many symlinks encountered.");
#else
            return path;
#endif
        }

        /// <summary>
        /// Checks if a file or directory exists at the specified path.
        /// </summary>
        /// <param name="path">The path to check.</param>
        /// <returns>True if the file or directory exists; otherwise false.</returns>
        private static bool FileOrDirectoryExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        /// <summary>
        /// Checks whether <paramref name="fullPath"/> is inside <paramref name="baseDir"/> (case-insensitive).
        /// </summary>
        /// <param name="fullPath">The path to check.</param>
        /// <param name="baseDir">The base directory.</param>
        /// <returns>True if inside; otherwise false.</returns>
        private static bool PathInsideBase(string fullPath, string baseDir)
        {
            var fullNorm = fullPath
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();

            var baseNorm = baseDir
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();

            return fullNorm.StartsWith(baseNorm);
        }

        /// <summary>
        /// Normalizes and validates a guest path by preventing directory traversals outside the sandbox.
        /// </summary>
        /// <param name="path">The guest path to normalize.</param>
        /// <returns>The normalized guest path.</returns>
        private static string NormalizeAndValidateGuestPath(string path)
        {
            if (path == null)
                throw new SandboxError(ErrNo.Inval, "Guest path cannot be null.");

            path = path.Replace('\\', '/');
            var trimmed = path.Trim('/');
            var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var stack = new List<string>();

            foreach (var segment in parts)
            {
                if (segment == ".")
                {
                    continue;
                }
                else if (segment == "..")
                {
                    if (stack.Count == 0)
                    {
                        throw new SandboxError(
                            ErrNo.Acces,
                            $"Path '{path}' attempts to traverse above the sandbox.");
                    }
                    stack.RemoveAt(stack.Count - 1);
                }
                else
                {
                    stack.Add(segment);
                }
            }

            var result = "/" + string.Join("/", stack);
            return result;
        }

        /// <summary>
        /// Normalizes a host path and handles drive root paths.
        /// </summary>
        /// <param name="hostPath">The host path to normalize.</param>
        /// <returns>The normalized path.</returns>
        private static string NormalizeHostRoot(string hostPath)
        {
            var full = Path.GetFullPath(hostPath);

            if (IsRootOrDrive(full))
            {
                return full;
            }

            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        /// <summary>
        /// Checks if the path represents a root ("/" or "C:\") or a drive.
        /// </summary>
        /// <param name="fullPath">Path to check.</param>
        /// <returns>True if the path is a root or drive; otherwise false.</returns>
        private static bool IsRootOrDrive(string fullPath)
        {
            if (fullPath.Length == 1 &&
                (fullPath[0] == Path.DirectorySeparatorChar || fullPath[0] == Path.AltDirectorySeparatorChar))
            {
                return true;
            }

            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var trimmedFull = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(trimmedRoot, trimmedFull, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
