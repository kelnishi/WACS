using System;
using System.IO;
using System.Runtime.InteropServices;
using JetBrains.Profiler.Api;

namespace Wacs.Console
{
    public class NoOpProfilingSession : IDisposable
    {
        public void Dispose() { }
    }
    
    public class ProfilingSession : IDisposable
    {
        private readonly string _snapshotPath;
        private bool _disposed;

        public ProfilingSession(string? snapshotPath = null)
        {
            _snapshotPath = snapshotPath ?? GetDefaultSnapshotPath();
            
            if (!StartProfiling())
            {
                throw new InvalidOperationException("Failed to start profiling session");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                MeasureProfiler.StopCollectingData();
                SaveSnapshot();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error during cleanup: {ex.Message}");
            }

            _disposed = true;
        }

        private string GetDefaultSnapshotPath()
        {
            string basePath;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "JetBrains", "dotTrace"
                );
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // On macOS, dotTrace snapshots are typically stored in ~/Library/Application Support/JetBrains/dotTrace
                string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                basePath = Path.Combine(
                    homeDirectory, "Library", "Application Support", "JetBrains", "dotTrace"
                );
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // On Linux, use .local/share in the user's home directory
                string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                basePath = Path.Combine(
                    homeDirectory, ".local", "share", "JetBrains", "dotTrace"
                );
            }
            else
            {
                throw new PlatformNotSupportedException("Current operating system is not supported");
            }

            // Create the full path including Snapshots directory
            string snapshotsPath = Path.Combine(basePath, "Snapshots");
            
            // Generate unique filename
            string filename = $"Snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.dtp";
            string fullPath = Path.Combine(snapshotsPath, filename);

            // Ensure directory exists
            Directory.CreateDirectory(snapshotsPath);
            
            return fullPath;
        }

        private bool StartProfiling()
        {
            try
            {
                MeasureProfiler.StartCollectingData();
                return true;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error starting profiling: {ex.Message}");
                return false;
            }
        }

        public void SaveSnapshot()
        {
            try
            {
                MeasureProfiler.SaveData(_snapshotPath);
                System.Console.WriteLine($"Snapshot saved to: {_snapshotPath}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error saving snapshot: {ex.Message}");
            }
        }
    }
}