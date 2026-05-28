// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Wacs.WASI.Preview3.Filesystem
{
    /// <summary>
    /// Native hard-link bridge — .NET (through .NET 9) has no
    /// managed `File.CreateHardLink` API, so we P/Invoke the OS
    /// primitives directly. P/Invoke is statically-bound and
    /// AOT-compatible.
    ///
    /// <para>Unix: <c>link(const char *src, const char *dst)</c>
    /// from libc. macOS and Linux share the same signature and
    /// errno semantics.</para>
    ///
    /// <para>Windows: <c>CreateHardLinkW</c> from kernel32 — the
    /// arg order is <c>(new, existing, attrs)</c>, opposite of
    /// the Unix convention; we normalize at the boundary.</para>
    /// </summary>
    internal static class NativeLink
    {
        /// <summary>Return the OS inode for <paramref name="path"/>
        /// when running under a Unix-like libc + the syscall
        /// succeeds. Two paths sharing the same inode under the
        /// same device are hard-links of each other. On Windows
        /// (or on Unix when the syscall fails) the call returns
        /// false and the caller falls back to path-equality.</summary>
        public static bool TryGetInode(string path, out ulong inode)
        {
            inode = 0;
            if (path == null) return false;
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var s = default(StatMacOs);
                    if (StatMacOs64(path, ref s) == 0)
                    { inode = s.st_ino; return true; }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var s = default(StatLinux);
                    if (StatLinuxDirect(path, ref s) == 0)
                    { inode = s.st_ino; return true; }
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StatMacOs
        {
            public uint st_dev;
            public ushort st_mode;
            public ushort st_nlink;
            public ulong st_ino;
            public uint st_uid;
            public uint st_gid;
            public uint st_rdev;
            public long st_atime, st_atime_nsec;
            public long st_mtime, st_mtime_nsec;
            public long st_ctime, st_ctime_nsec;
            public long st_btime, st_btime_nsec;
            public long st_size;
            public long st_blocks;
            public int st_blksize;
            public uint st_flags;
            public uint st_gen;
            public int st_lspare;
            public long st_qspare0, st_qspare1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StatLinux
        {
            public ulong st_dev;
            public ulong st_ino;
            public ulong st_nlink;
            public uint st_mode;
            public uint st_uid;
            public uint st_gid;
            public int __pad0;
            public ulong st_rdev;
            public long st_size;
            public long st_blksize;
            public long st_blocks;
            public long st_atime, st_atime_nsec;
            public long st_mtime, st_mtime_nsec;
            public long st_ctime, st_ctime_nsec;
            public long __unused0, __unused1, __unused2;
        }

        [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
        private static extern int StatMacOs64(
            string path, ref StatMacOs buf);

        [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
        private static extern int StatLinuxDirect(
            string path, ref StatLinux buf);

        public static void CreateHardLink(
            string existingFile, string newLink)
        {
            if (existingFile == null)
                throw new ArgumentNullException(nameof(existingFile));
            if (newLink == null)
                throw new ArgumentNullException(nameof(newLink));

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!Win32CreateHardLinkW(newLink, existingFile, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new IOException(
                        $"CreateHardLink failed (Win32 error {err}) " +
                        $"linking '{newLink}' → '{existingFile}'.");
                }
                return;
            }

            // Unix path: libc link(2).
            int rc = LibcLink(existingFile, newLink);
            if (rc != 0)
            {
                int err = Marshal.GetLastWin32Error();
                throw new IOException(
                    $"link(2) returned {rc} (errno {err}) " +
                    $"linking '{newLink}' → '{existingFile}'.");
            }
        }

        [DllImport("libc", EntryPoint = "link",
            SetLastError = true,
            CharSet = CharSet.Ansi,
            BestFitMapping = false, ThrowOnUnmappableChar = true)]
        private static extern int LibcLink(
            string existingFile, string newLink);

        [DllImport("kernel32.dll",
            EntryPoint = "CreateHardLinkW",
            SetLastError = true,
            CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Win32CreateHardLinkW(
            string newLink, string existingFile,
            IntPtr securityAttributes);
    }
}
