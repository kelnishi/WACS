// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Wacs.ComponentModel.Async;
using Wacs.WASI.Preview3.CanonicalAbi;
using Wacs.WASI.Preview3.Cli;
using Wacs.WASI.Preview3.Clocks;

namespace Wacs.WASI.Preview3.Filesystem
{
    /// <summary>
    /// Default <see cref="IDescriptor"/> implementation backed
    /// by <see cref="System.IO"/>. Each instance wraps a single
    /// host-side filesystem object (file or directory) by
    /// absolute path. Open file streams are kept on-demand —
    /// the descriptor is the "name" of the object; per-call
    /// reads/writes open transient <see cref="FileStream"/>s.
    ///
    /// <para><b>Sandboxing.</b> A
    /// <see cref="Descriptor"/> rooted at a directory restricts
    /// path operations to children of its root path; attempts
    /// to escape via <c>..</c> or absolute paths throw
    /// <see cref="FilesystemException"/> with
    /// <see cref="ErrorCode.NotPermitted"/>. This mirrors the
    /// Preview 2 default's preopen-rooted sandboxing.</para>
    ///
    /// <para><b>Async surface.</b> Most methods are async in
    /// the WIT. The implementation runs the synchronous .NET
    /// I/O calls on the thread pool via
    /// <see cref="Task.Run(System.Action)"/> so the CLR thread
    /// the canon-async dispatcher invokes from is free to do
    /// other work — matches the spec's intent of
    /// non-blocking file I/O.</para>
    /// </summary>
    public sealed class Descriptor : IDescriptor
    {
        private static readonly DateTime UnixEpoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly string _absolutePath;
        private readonly string _rootPath;
        private readonly bool _isDirectory;
        private readonly DescriptorFlags _flags;

        /// <summary>Construct a descriptor rooted at
        /// <paramref name="absolutePath"/>. The
        /// <paramref name="rootPath"/> is the sandboxing
        /// boundary — path operations refuse to resolve outside
        /// it. When the descriptor IS the root, the two are
        /// equal.</summary>
        public Descriptor(
            string absolutePath, string rootPath, DescriptorFlags flags)
        {
            if (absolutePath == null)
                throw new ArgumentNullException(nameof(absolutePath));
            if (rootPath == null)
                throw new ArgumentNullException(nameof(rootPath));
            _absolutePath = Path.GetFullPath(absolutePath);
            _rootPath = Path.GetFullPath(rootPath);
            _isDirectory = Directory.Exists(_absolutePath);
            _flags = flags;
        }

        /// <summary>Absolute host path this descriptor refers
        /// to. Exposed for diagnostics + tests; embedders
        /// shouldn't depend on its format.</summary>
        public string AbsolutePath => _absolutePath;

        /// <summary>Sandboxing root path. Path-relative
        /// operations refuse to resolve outside this
        /// boundary.</summary>
        public string RootPath => _rootPath;

        // Resolve a guest-supplied path against this
        // descriptor's directory and confirm the result stays
        // within the sandbox root. Throws:
        //   - NoEntry        when the path is empty
        //   - NotPermitted   when the path is absolute, contains
        //                    `..` segments, crosses a symlink, or
        //                    resolves outside the sandbox root
        //   - Invalid        when the path is null
        //
        // Symlink-aware: any existing path component that's a
        // symbolic link causes a NotPermitted reject — the
        // wasip3 testsuite fixtures use `parent → ..` to probe
        // sandbox escape, and even when the symlink target
        // happens to land inside the sandbox literally, allowing
        // the traversal violates the spec's containment intent.
        // Stricter than necessary for non-escaping symlinks but
        // matches the testsuite's expectations.
        private string ResolveChild(string path)
        {
            if (path == null)
                throw new FilesystemException(
                    ErrorCode.Invalid, "path is null");
            if (path.Length == 0)
                throw new FilesystemException(
                    ErrorCode.NoEntry, "empty path");
            if (Path.IsPathRooted(path)
                || path.StartsWith("/", StringComparison.Ordinal)
                || path.StartsWith("\\", StringComparison.Ordinal))
                throw new FilesystemException(
                    ErrorCode.NotPermitted,
                    $"absolute path '{path}' not permitted.");
            foreach (var seg in path.Split('/', '\\'))
            {
                if (seg == "..")
                    throw new FilesystemException(
                        ErrorCode.NotPermitted,
                        $"path '{path}' contains '..' segment.");
            }

            var combined = Path.GetFullPath(Path.Combine(_absolutePath, path));
            var rootSep = _rootPath.EndsWith(
                Path.DirectorySeparatorChar.ToString())
                ? _rootPath
                : _rootPath + Path.DirectorySeparatorChar;
            // Allow combined == _rootPath (the root itself); else
            // it must start with rootSep so we don't accept
            // /foo/bar-malicious when root is /foo/bar.
            if (combined != _rootPath
                && !combined.StartsWith(rootSep, StringComparison.Ordinal))
                throw new FilesystemException(
                    ErrorCode.NotPermitted,
                    $"path '{path}' resolves outside sandbox root " +
                    $"'{_rootPath}'.");

            if (TraversesSymlinkComponent(path))
                throw new FilesystemException(
                    ErrorCode.NotPermitted,
                    $"path '{path}' crosses a symbolic link.");

            return combined;
        }

        // Walk each component of <paramref name="path"/> from
        // this descriptor's directory and return true iff any
        // existing intermediate component is a symbolic link.
        // Final-component symlinks are also detected. Missing
        // components (e.g., the leaf of a not-yet-created
        // directory in mkdir) just stop the walk — we can't
        // inspect what doesn't exist yet.
        private bool TraversesSymlinkComponent(string path)
        {
            var current = _absolutePath;
            foreach (var part in path.Split('/', '\\'))
            {
                if (string.IsNullOrEmpty(part) || part == ".") continue;
                current = Path.Combine(current, part);
                // Check the LITERAL path's link status, not what
                // it resolves to. DirectoryInfo / FileInfo for a
                // symlink to a directory still has its own
                // LinkTarget property — that's our signal.
#if NET6_0_OR_GREATER
                FileSystemInfo? info = null;
                if (Directory.Exists(current))
                    info = new DirectoryInfo(current);
                else if (File.Exists(current))
                    info = new FileInfo(current);
                if (info?.LinkTarget != null) return true;
                if (info == null) return false;
#else
                // netstandard2.1 has no LinkTarget API — best
                // we can do is detect missing components.
                if (!Directory.Exists(current)
                    && !File.Exists(current))
                    return false;
#endif
            }
            return false;
        }

        // Wrap a thrown CLR exception into a FilesystemException
        // with the closest matching error code. Exception →
        // error-code mapping is best-effort; uncategorized
        // exceptions become ErrorCode.Io with the original
        // message.
        private static FilesystemException ToFilesystem(Exception ex) =>
            ex switch
            {
                FilesystemException fe => fe,
                FileNotFoundException fnf =>
                    new FilesystemException(ErrorCode.NoEntry, fnf.Message),
                DirectoryNotFoundException dnf =>
                    new FilesystemException(ErrorCode.NoEntry, dnf.Message),
                UnauthorizedAccessException ua =>
                    new FilesystemException(ErrorCode.Access, ua.Message),
                PathTooLongException ptl =>
                    new FilesystemException(ErrorCode.NameTooLong, ptl.Message),
                IOException io when io.HResult ==
                    unchecked((int)0x80070050) /* ERROR_FILE_EXISTS */ =>
                    new FilesystemException(ErrorCode.Exist, io.Message),
                IOException io =>
                    new FilesystemException(ErrorCode.Io, io.Message),
                NotSupportedException nse =>
                    new FilesystemException(ErrorCode.Unsupported, nse.Message),
                ArgumentException ae =>
                    new FilesystemException(ErrorCode.Invalid, ae.Message),
                _ => new FilesystemException(
                    ErrorCode.Io, $"{ex.GetType().Name}: {ex.Message}"),
            };

        // ---- Stream-shape methods (sync from interface POV; the
        //      host-side read/write loop runs on Task.Run) -----

        public (int streamHandle, int futureHandle, Task ReadCompletion)
            ReadViaStream(AsyncDispatcher dispatcher, FileSize offset)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            var streamHandle = dispatcher.StreamNew(typeIdx: 0);
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);

            var path = _absolutePath;
            var completion = Task.Run(async () =>
            {
                try
                {
                    using var fs = new FileStream(
                        path, FileMode.Open, FileAccess.Read,
                        FileShare.Read);
                    if ((ulong)offset.Value > (ulong)fs.Length)
                        throw new FilesystemException(
                            ErrorCode.InvalidSeek,
                            $"offset {offset.Value} past EOF " +
                            $"(length {fs.Length}).");
                    fs.Position = (long)offset.Value;
                    var buffer = new byte[4096];
                    while (true)
                    {
                        var n = await fs.ReadAsync(
                            buffer, 0, buffer.Length).ConfigureAwait(false);
                        if (n == 0) break;
                        for (int i = 0; i < n; i++)
                            dispatcher.StreamTryWrite(streamHandle, buffer[i]);
                    }
                    dispatcher.StreamDropWritable(streamHandle);
                    dispatcher.FutureWrite(futureHandle, null);
                }
                catch (Exception ex)
                {
                    dispatcher.StreamDropWritable(streamHandle);
                    dispatcher.FutureWrite(futureHandle, ToFilesystem(ex));
                }
            });
            return (streamHandle, futureHandle, completion);
        }

        public (int futureHandle, Task WriteCompletion) WriteViaStream(
            AsyncDispatcher dispatcher, int streamHandle, FileSize offset)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);

            var buffer = dispatcher.GetByteStreamBuffer(streamHandle);
            if (buffer == null)
            {
                dispatcher.FutureWrite(futureHandle,
                    new FilesystemException(ErrorCode.BadDescriptor,
                        $"stream handle {streamHandle} not allocated"));
                return (futureHandle, Task.CompletedTask);
            }

            var path = _absolutePath;
            var ofs = offset;
            var completion = Task.Run(async () =>
            {
                FileStream? fs = null;
                Exception? err = null;
                try
                {
                    fs = new FileStream(
                        path, FileMode.OpenOrCreate, FileAccess.Write,
                        FileShare.None);
                    fs.Position = (long)ofs.Value;
                    var staging = new byte[4096];
                    while (await buffer.Reader
                        .WaitToReadAsync().ConfigureAwait(false))
                    {
                        int n = 0;
                        while (n < staging.Length
                            && buffer.Reader.TryRead(out var b))
                        {
                            staging[n++] = b;
                        }
                        if (n > 0)
                            await fs.WriteAsync(staging, 0, n)
                                .ConfigureAwait(false);
                    }
                    await fs.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception ex) { err = ex; }
                // Close the FileStream BEFORE writing to the
                // future — embedders that observe future
                // completion then immediately read the file
                // race with dispose otherwise.
                fs?.Dispose();
                if (err == null)
                    dispatcher.FutureWrite(futureHandle, null);
                else
                    dispatcher.FutureWrite(futureHandle, ToFilesystem(err));
            });
            return (futureHandle, completion);
        }

        public (int futureHandle, Task AppendCompletion) AppendViaStream(
            AsyncDispatcher dispatcher, int streamHandle)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);

            var buffer = dispatcher.GetByteStreamBuffer(streamHandle);
            if (buffer == null)
            {
                dispatcher.FutureWrite(futureHandle,
                    new FilesystemException(ErrorCode.BadDescriptor,
                        $"stream handle {streamHandle} not allocated"));
                return (futureHandle, Task.CompletedTask);
            }

            var path = _absolutePath;
            var completion = Task.Run(async () =>
            {
                FileStream? fs = null;
                Exception? err = null;
                try
                {
                    fs = new FileStream(
                        path, FileMode.Append, FileAccess.Write,
                        FileShare.None);
                    var staging = new byte[4096];
                    while (await buffer.Reader
                        .WaitToReadAsync().ConfigureAwait(false))
                    {
                        int n = 0;
                        while (n < staging.Length
                            && buffer.Reader.TryRead(out var b))
                        {
                            staging[n++] = b;
                        }
                        if (n > 0)
                            await fs.WriteAsync(staging, 0, n)
                                .ConfigureAwait(false);
                    }
                    await fs.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception ex) { err = ex; }
                fs?.Dispose();
                if (err == null)
                    dispatcher.FutureWrite(futureHandle, null);
                else
                    dispatcher.FutureWrite(futureHandle, ToFilesystem(err));
            });
            return (futureHandle, completion);
        }

        public Task AdviseAsync(
            FileSize offset, FileSize length, Advice advice,
            CancellationToken cancellationToken = default)
        {
            // .NET doesn't expose posix_fadvise. The advice is
            // a hint; ignoring it is spec-permitted ("not
            // implemented" treats it as a no-op).
            return Task.CompletedTask;
        }

        public Task SyncDataAsync(CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                try
                {
                    if (_isDirectory) return; // no-op for dirs
                    using var fs = new FileStream(
                        _absolutePath, FileMode.Open, FileAccess.Write,
                        FileShare.ReadWrite);
                    fs.Flush(flushToDisk: true);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

        public DescriptorFlags GetFlags() => _flags;

        public DescriptorType GetType_()
        {
            if (_isDirectory) return DescriptorType.Directory;
            if (File.Exists(_absolutePath))
            {
                var attr = File.GetAttributes(_absolutePath);
                if ((attr & FileAttributes.ReparsePoint) != 0)
                    return DescriptorType.SymbolicLink;
                return DescriptorType.RegularFile;
            }
            return DescriptorType.Other(null);
        }

        public Task SetSizeAsync(
            FileSize size, CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                try
                {
                    using var fs = new FileStream(
                        _absolutePath, FileMode.Open, FileAccess.Write,
                        FileShare.None);
                    fs.SetLength((long)size.Value);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

        public Task SetTimesAsync(
            NewTimestamp dataAccessTimestamp,
            NewTimestamp dataModificationTimestamp,
            CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                try
                {
                    ApplyTimes(_absolutePath,
                        dataAccessTimestamp, dataModificationTimestamp);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

        private static void ApplyTimes(
            string path, NewTimestamp access, NewTimestamp mod)
        {
            if (access.Kind == NewTimestamp.Tag.Now)
                File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            else if (access.Kind == NewTimestamp.Tag.Timestamp)
                File.SetLastAccessTimeUtc(
                    path, InstantToDateTime(access.TimestampValue));
            // NoChange = leave as-is.

            if (mod.Kind == NewTimestamp.Tag.Now)
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            else if (mod.Kind == NewTimestamp.Tag.Timestamp)
                File.SetLastWriteTimeUtc(
                    path, InstantToDateTime(mod.TimestampValue));
        }

        public (int streamHandle, int futureHandle, Task ReadCompletion)
            ReadDirectory(AsyncDispatcher dispatcher, ICabiRealloc realloc)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (realloc == null)
                throw new ArgumentNullException(nameof(realloc));

            // Enumerate UP-FRONT to size the byte stream
            // correctly. The default StreamNew capacity (64) is
            // too small for even a couple of 24-byte entries —
            // eager fill needs the channel sized for the full
            // payload to avoid TryWrite-on-full dropping the
            // tail. Embedders with very large directories should
            // override `ReadDirectory` for a true lazy-streaming
            // backing.
            //
            // Enumerate before allocating the stream + future so
            // a NotDirectory error doesn't leak handles.
            string[] entries;
            try
            {
                if (!_isDirectory)
                    throw new FilesystemException(
                        ErrorCode.NotDirectory,
                        $"'{_absolutePath}' is not a directory.");
                entries = Directory.GetFileSystemEntries(_absolutePath);
            }
            catch (FilesystemException) { throw; }
            catch (Exception ex) { throw ToFilesystem(ex); }

            // 24 bytes per entry; +24 headroom for any final
            // pad/flush. Minimum 64 to avoid pathological tiny
            // capacities.
            int capacity = System.Math.Max(64, entries.Length * 24 + 24);
            var streamHandle = dispatcher.StreamNew(
                typeIdx: 0, capacity: capacity);
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);

            try
            {
                foreach (var entry in entries)
                {
                    WriteDirectoryEntry(
                        dispatcher, streamHandle, realloc, entry);
                }
                dispatcher.StreamDropWritable(streamHandle);
                dispatcher.FutureWrite(futureHandle, /* ok */ null);
            }
            catch (Exception ex)
            {
                dispatcher.StreamDropWritable(streamHandle);
                dispatcher.FutureWrite(futureHandle, ToFilesystem(ex));
            }
            return (streamHandle, futureHandle, Task.CompletedTask);
        }

        // Serialize one directory-entry to the byte stream.
        // Layout: 24 bytes per entry:
        //   +0..16:  descriptor-type variant
        //     +0:    disc (u8) + 3-byte pad
        //     +4..16: Other-payload slot (12 bytes: option<string>);
        //            zero for non-Other variants
        //   +16..20: name-ptr (i32 — cabi_realloc-allocated)
        //   +20..24: name-len (i32)
        private static void WriteDirectoryEntry(
            AsyncDispatcher dispatcher, int streamHandle,
            ICabiRealloc realloc, string entryPath)
        {
            // Determine type.
            DescriptorType type;
            if (Directory.Exists(entryPath))
                type = DescriptorType.Directory;
            else if (File.Exists(entryPath))
            {
                var attr = File.GetAttributes(entryPath);
                type = (attr & FileAttributes.ReparsePoint) != 0
                    ? DescriptorType.SymbolicLink
                    : DescriptorType.RegularFile;
            }
            else
            {
                type = DescriptorType.Other(null);
            }

            // Allocate + write the name UTF-8 in guest memory.
            var name = Path.GetFileName(entryPath);
            var nameBytes = Encoding.UTF8.GetBytes(name);
            int namePtr = nameBytes.Length == 0
                ? 0
                : realloc.Allocate(align: 1, size: nameBytes.Length);
            if (nameBytes.Length > 0
                && dispatcher.Memory != null)
            {
                new ReadOnlySpan<byte>(nameBytes)
                    .CopyTo(dispatcher.Memory
                        .AsSpan(namePtr, nameBytes.Length));
            }

            // Serialize the 24-byte entry into the byte stream
            // one byte at a time (the byte-channel doesn't have
            // a bulk-write primitive). Performance isn't a
            // concern at the directory-entry granularity.
            var entryBytes = new byte[24];
            entryBytes[0] = (byte)type.Kind;
            // Bytes 1..16 stay zero (variant pad + Other payload
            // slot none-discriminant). type.OtherPayload is
            // ignored when emitting via the stream — the per-
            // entry retptr layout for the variant payload would
            // need a separate realloc allocation; not worth the
            // complexity for the rare Other case here.
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(
                    entryBytes.AsSpan(16, 4), namePtr);
            System.Buffers.Binary.BinaryPrimitives
                .WriteInt32LittleEndian(
                    entryBytes.AsSpan(20, 4), nameBytes.Length);

            for (int i = 0; i < entryBytes.Length; i++)
                dispatcher.StreamTryWrite(streamHandle, entryBytes[i]);
        }

        public Task SyncAsync(CancellationToken cancellationToken = default)
            => SyncDataAsync(cancellationToken);

        public Task CreateDirectoryAtAsync(
            string path, CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                try
                {
                    var resolved = ResolveChild(path);
                    // Spec: create-directory-at on a path that
                    // already exists (as file or directory)
                    // surfaces as Exist. .NET's
                    // Directory.CreateDirectory is a no-op on an
                    // existing dir (no error), which would
                    // wrongly succeed for cases the spec wants
                    // rejected.
                    if (Directory.Exists(resolved)
                        || File.Exists(resolved))
                        throw new FilesystemException(
                            ErrorCode.Exist,
                            $"'{path}' already exists.");
                    Directory.CreateDirectory(resolved);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

        public Task<DescriptorStat> StatAsync(
            CancellationToken cancellationToken = default)
            => Task.Run(() => StatPath(_absolutePath), cancellationToken);

        public Task<DescriptorStat> StatAtAsync(
            PathFlags pathFlags, string path,
            CancellationToken cancellationToken = default)
            => Task.Run(() => StatPath(ResolveChild(path)), cancellationToken);

        private DescriptorStat StatPath(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var di = new DirectoryInfo(path);
                    return new DescriptorStat
                    {
                        Type = DescriptorType.Directory,
                        LinkCount = new LinkCount(1),
                        Size = new FileSize(0),
                        DataAccessTimestamp =
                            DateTimeToInstant(di.LastAccessTimeUtc),
                        DataModificationTimestamp =
                            DateTimeToInstant(di.LastWriteTimeUtc),
                        StatusChangeTimestamp =
                            DateTimeToInstant(di.LastWriteTimeUtc),
                    };
                }
                if (!File.Exists(path))
                    throw new FilesystemException(
                        ErrorCode.NoEntry, $"no such path '{path}'.");
                var fi = new FileInfo(path);
                var dtype = (fi.Attributes & FileAttributes.ReparsePoint) != 0
                    ? DescriptorType.SymbolicLink
                    : DescriptorType.RegularFile;
                return new DescriptorStat
                {
                    Type = dtype,
                    LinkCount = new LinkCount(1),
                    Size = new FileSize((ulong)fi.Length),
                    DataAccessTimestamp =
                        DateTimeToInstant(fi.LastAccessTimeUtc),
                    DataModificationTimestamp =
                        DateTimeToInstant(fi.LastWriteTimeUtc),
                    StatusChangeTimestamp =
                        DateTimeToInstant(fi.LastWriteTimeUtc),
                };
            }
            catch (FilesystemException) { throw; }
            catch (Exception ex) { throw ToFilesystem(ex); }
        }

        public Task SetTimesAtAsync(
            PathFlags pathFlags, string path,
            NewTimestamp dataAccessTimestamp,
            NewTimestamp dataModificationTimestamp,
            CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                try
                {
                    ApplyTimes(ResolveChild(path),
                        dataAccessTimestamp, dataModificationTimestamp);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

        public Task LinkAtAsync(
            PathFlags oldPathFlags, string oldPath,
            IDescriptor newDescriptor, string newPath,
            CancellationToken cancellationToken = default)
        {
            // Hard links via System.IO are platform-limited;
            // surface as unsupported for now.
            throw new FilesystemException(
                ErrorCode.Unsupported,
                "link-at: hard-link creation not implemented " +
                "in the default backend.");
        }

        public Task<IDescriptor> OpenAtAsync(
            PathFlags pathFlags, string path,
            OpenFlags openFlags, DescriptorFlags flags,
            CancellationToken cancellationToken = default)
            => Task.Run<IDescriptor>(() =>
            {
                try
                {
                    var resolved = ResolveChild(path);

                    if ((openFlags & OpenFlags.Directory) != 0
                        && !Directory.Exists(resolved))
                        throw new FilesystemException(
                            ErrorCode.NotDirectory,
                            $"'{path}' is not a directory.");

                    if ((openFlags & OpenFlags.Exclusive) != 0
                        && File.Exists(resolved))
                        throw new FilesystemException(
                            ErrorCode.Exist,
                            $"'{path}' already exists.");

                    if ((openFlags & OpenFlags.Create) != 0
                        && !File.Exists(resolved)
                        && !Directory.Exists(resolved))
                    {
                        File.Create(resolved).Dispose();
                    }

                    if ((openFlags & OpenFlags.Truncate) != 0
                        && File.Exists(resolved))
                    {
                        File.Create(resolved).Dispose();
                    }

                    if (!File.Exists(resolved) && !Directory.Exists(resolved))
                        throw new FilesystemException(
                            ErrorCode.NoEntry,
                            $"'{path}' does not exist.");

                    return new Descriptor(resolved, _rootPath, flags);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

        public Task<string> ReadlinkAtAsync(
            string path, CancellationToken cancellationToken = default)
        {
            // .NET 6+ has FileSystemInfo.LinkTarget; netstandard2.1
            // doesn't. Surface unsupported on older targets; on
            // net8.0 the runtime path could implement it. Keeping
            // unsupported here for cross-target consistency.
            throw new FilesystemException(
                ErrorCode.Unsupported,
                "readlink-at: symbolic-link target read not " +
                "implemented in the default backend.");
        }

        public Task RemoveDirectoryAtAsync(
            string path, CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                try
                {
                    var resolved = ResolveChild(path);
                    // Removing the sandbox root itself (path == "."
                    // or a trailing-slash variant that normalizes
                    // back to root) is Invalid per the spec.
                    if (resolved == _rootPath
                        || resolved == _absolutePath)
                        throw new FilesystemException(
                            ErrorCode.Invalid,
                            $"cannot remove preopen root '{path}'.");
                    // Regular file at the path: not a directory.
                    if (File.Exists(resolved)
                        && !Directory.Exists(resolved))
                        throw new FilesystemException(
                            ErrorCode.NotDirectory,
                            $"'{path}' is a file, not a directory.");
                    if (!Directory.Exists(resolved))
                        throw new FilesystemException(
                            ErrorCode.NoEntry,
                            $"'{path}' does not exist.");
                    Directory.Delete(resolved, recursive: false);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

        public Task RenameAtAsync(
            string oldPath, IDescriptor newDescriptor, string newPath,
            CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                try
                {
                    var oldResolved = ResolveChild(oldPath);
                    if (newDescriptor is not Descriptor newDesc)
                        throw new FilesystemException(
                            ErrorCode.Invalid,
                            "rename-at: target descriptor not a " +
                            "System.IO-backed Descriptor.");
                    var newResolved = newDesc.ResolveChild(newPath);
                    if (File.Exists(oldResolved))
                        File.Move(oldResolved, newResolved);
                    else if (Directory.Exists(oldResolved))
                        Directory.Move(oldResolved, newResolved);
                    else
                        throw new FilesystemException(
                            ErrorCode.NoEntry,
                            $"'{oldPath}' does not exist.");
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

        public Task SymlinkAtAsync(
            string oldPath, string newPath,
            CancellationToken cancellationToken = default)
        {
            throw new FilesystemException(
                ErrorCode.Unsupported,
                "symlink-at: symbolic-link creation not implemented " +
                "in the default backend.");
        }

        public Task UnlinkFileAtAsync(
            string path, CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                try
                {
                    var resolved = ResolveChild(path);
                    if (Directory.Exists(resolved))
                        throw new FilesystemException(
                            ErrorCode.IsDirectory,
                            $"'{path}' is a directory.");
                    if (!File.Exists(resolved))
                        throw new FilesystemException(
                            ErrorCode.NoEntry,
                            $"'{path}' does not exist.");
                    File.Delete(resolved);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

        public Task<bool> IsSameObjectAsync(
            IDescriptor other,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                other is Descriptor d &&
                string.Equals(d._absolutePath, _absolutePath,
                    StringComparison.Ordinal));

        public Task<MetadataHashValue> MetadataHashAsync(
            CancellationToken cancellationToken = default)
            => Task.Run(() => HashMetadata(_absolutePath), cancellationToken);

        public Task<MetadataHashValue> MetadataHashAtAsync(
            PathFlags pathFlags, string path,
            CancellationToken cancellationToken = default)
            => Task.Run(
                () => HashMetadata(ResolveChild(path)), cancellationToken);

        // 128-bit hash of (path + last-write-time + size). Stable
        // for a given inode-equivalent across calls; SHA-256
        // truncated to 16 bytes is good enough for the spec's
        // intent of "different objects produce different hashes,
        // mostly".
        private static MetadataHashValue HashMetadata(string path)
        {
            try
            {
                long size = 0;
                DateTime lwt;
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    size = fi.Length;
                    lwt = fi.LastWriteTimeUtc;
                }
                else if (Directory.Exists(path))
                {
                    lwt = Directory.GetLastWriteTimeUtc(path);
                }
                else
                {
                    throw new FilesystemException(
                        ErrorCode.NoEntry,
                        $"metadata-hash: '{path}' does not exist.");
                }

                var bytes = new List<byte>();
                bytes.AddRange(Encoding.UTF8.GetBytes(path));
                bytes.AddRange(BitConverter.GetBytes(lwt.Ticks));
                bytes.AddRange(BitConverter.GetBytes(size));
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(bytes.ToArray());
                return new MetadataHashValue
                {
                    Lower = BitConverter.ToUInt64(hash, 0),
                    Upper = BitConverter.ToUInt64(hash, 8),
                };
            }
            catch (FilesystemException) { throw; }
            catch (Exception ex) { throw ToFilesystem(ex); }
        }

        // ---- Helpers ---------------------------------------------------

        private static Instant DateTimeToInstant(DateTime utc)
        {
            var ticks = (utc - UnixEpoch).Ticks;
            var totalNs = ticks * 100L;
            long seconds;
            uint nanoseconds;
            if (totalNs >= 0)
            {
                seconds = totalNs / 1_000_000_000L;
                nanoseconds = (uint)(totalNs % 1_000_000_000L);
            }
            else
            {
                long abs = -totalNs;
                long absSec = abs / 1_000_000_000L;
                long absNs = abs % 1_000_000_000L;
                if (absNs == 0)
                {
                    seconds = -absSec;
                    nanoseconds = 0;
                }
                else
                {
                    seconds = -absSec - 1;
                    nanoseconds = (uint)(1_000_000_000L - absNs);
                }
            }
            return new Instant(seconds, nanoseconds);
        }

        private static DateTime InstantToDateTime(Instant instant)
        {
            long totalNs = instant.Seconds * 1_000_000_000L
                + instant.Nanoseconds;
            return UnixEpoch.AddTicks(totalNs / 100L);
        }
    }

    /// <summary>
    /// Default <see cref="IPreopens"/> implementation. The
    /// embedder configures it with a list of
    /// (host-path, guest-path) pairs at construction time; the
    /// returned descriptors are
    /// <see cref="Descriptor"/>s rooted at the host paths with
    /// the host paths as their sandbox boundaries.
    /// </summary>
    public sealed class DirectoryPreopens : IPreopens
    {
        private readonly IReadOnlyList<DescriptorPreopen> _preopens;

        public DirectoryPreopens(IReadOnlyList<DescriptorPreopen> preopens)
        {
            _preopens = preopens
                ?? throw new ArgumentNullException(nameof(preopens));
        }

        /// <summary>Construct from raw (host-path, guest-path)
        /// pairs. Each becomes a <see cref="Descriptor"/> with
        /// read+write+mutate-directory flags rooted at the host
        /// path.</summary>
        public static DirectoryPreopens FromHostPaths(
            params (string hostPath, string guestPath)[] pairs)
        {
            var list = new List<DescriptorPreopen>(pairs.Length);
            foreach (var (host, guest) in pairs)
            {
                var desc = new Descriptor(
                    host, host,
                    DescriptorFlags.Read | DescriptorFlags.Write
                        | DescriptorFlags.MutateDirectory);
                list.Add(new DescriptorPreopen(desc, guest));
            }
            return new DirectoryPreopens(list);
        }

        public IReadOnlyList<DescriptorPreopen> GetDirectories() => _preopens;
    }
}
