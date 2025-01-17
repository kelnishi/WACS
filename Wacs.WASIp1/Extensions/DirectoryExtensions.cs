using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;

namespace Wacs.WASIp1.Extensions
{
    internal static class DirectoryExtensions
    {
        /// <summary>
        /// Safely enumerates file system entries, skipping any that cause access or other errors.
        /// </summary>
        /// <param name="path">The directory path to enumerate</param>
        /// <param name="searchPattern">The search string (optional)</param>
        /// <param name="searchOption">Whether to search subdirectories (optional)</param>
        /// <param name="logger">Optional action to log errors</param>
        /// <returns>An enumerable of accessible file system entries</returns>
        public static IEnumerable<string> EnumerateFileSystemEntriesSafely(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly,
            Action<Exception, string>? logger = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));

            // First try to access the root directory
            if (!Directory.Exists(path))
            {
                if (logger != null)
                {
                    logger(new DirectoryNotFoundException($"Directory not found: {path}"), path);
                }
                yield break;
            }

            if (searchOption == SearchOption.TopDirectoryOnly)
            {
                foreach (var entry in EnumerateTopLevel(path, searchPattern, logger))
                {
                    yield return entry;
                }
            }
            else
            {
                var pendingDirectories = new Queue<string>();
                pendingDirectories.Enqueue(path);

                while (pendingDirectories.Count > 0)
                {
                    var currentDir = pendingDirectories.Dequeue();

                    // Enumerate files and directories in current directory
                    foreach (var entry in EnumerateTopLevel(currentDir, searchPattern, logger))
                    {
                        yield return entry;

                        // If entry is a directory, add it to queue for processing
                        try
                        {
                            if (Directory.Exists(entry))
                            {
                                pendingDirectories.Enqueue(entry);
                            }
                        }
                        catch (Exception ex) when (IsFileSystemException(ex))
                        {
                            if (logger != null)
                            {
                                logger(ex, entry);
                            }
                            // Skip this directory but continue with others
                            continue;
                        }
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateTopLevel(
            string path,
            string searchPattern,
            Action<Exception, string>? logger)
        {
            try
            {
                return Directory.EnumerateFileSystemEntries(path, searchPattern);
            }
            catch (Exception ex) when (IsFileSystemException(ex))
            {
                logger?.Invoke(ex, path);
                return Enumerable.Empty<string>();
            }
        }

        private static bool IsFileSystemException(Exception ex) =>
            ex is UnauthorizedAccessException ||
            ex is SecurityException ||
            ex is DirectoryNotFoundException ||
            ex is PathTooLongException ||
            ex is IOException;
    }
}