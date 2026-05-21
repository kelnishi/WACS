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
    public sealed class Descriptor : IDescriptor, IDisposable
    {
        private static readonly DateTime UnixEpoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly string _absolutePath;
        private readonly string _rootPath;
        private readonly bool _isDirectory;
        private readonly DescriptorFlags _flags;
        // For file descriptors we hold an OS file handle open
        // for the descriptor's lifetime. Under Unix this gives
        // us inode-survives-unlink semantics for free (stat /
        // set-size etc. continue to work via the fd after the
        // path is unlinked). FileShare.ReadWrite|Delete lets
        // multiple sibling descriptors coexist and lets unlink
        // proceed. null for directory descriptors.
        private readonly FileStream? _fileStream;
        private bool _disposed;

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
            if (!_isDirectory && File.Exists(_absolutePath))
            {
                FileAccess access =
                    ((flags & DescriptorFlags.Read) != 0,
                     (flags & DescriptorFlags.Write) != 0) switch
                    {
                        (true, true) => FileAccess.ReadWrite,
                        (false, true) => FileAccess.Write,
                        _ => FileAccess.Read,
                    };
                _fileStream = new FileStream(
                    _absolutePath, FileMode.Open, access,
                    FileShare.ReadWrite | FileShare.Delete);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _fileStream?.Dispose();
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
            // All *-at methods are scoped to a directory descriptor.
            // Resolving against a file descriptor surfaces as
            // NotDirectory per the spec (filesystem-stat fixture
            // probes this with `afd.stat_at(empty, "z.txt")`).
            if (!_isDirectory)
                throw new FilesystemException(
                    ErrorCode.NotDirectory,
                    "descriptor is not a directory.");
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
        // this descriptor's directory and decide whether the
        // walk crosses a sandbox-escaping symlink:
        //  - Intermediate symlinks: always reject (we can't
        //    cheaply prove the followed path stays in the
        //    sandbox, and forcing a follow violates the spec
        //    when path-flags doesn't request it).
        //  - Final-component symlink: resolve its literal
        //    target relative to the parent directory and
        //    reject only if the resolved location escapes the
        //    sandbox. Operations like rename / unlink / etc.
        //    legitimately need to overwrite or remove a
        //    symlink whose target stays within the sandbox.
        //  - Missing components: just stop the walk — we
        //    can't inspect what doesn't exist yet.
        private bool TraversesSymlinkComponent(string path)
        {
            var parts = path.Split('/', '\\');
            var current = _absolutePath;
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.IsNullOrEmpty(part) || part == ".") continue;
                current = Path.Combine(current, part);
                bool isFinalComponent = i == parts.Length - 1;
#if NET6_0_OR_GREATER
                FileSystemInfo? info = null;
                if (Directory.Exists(current))
                    info = new DirectoryInfo(current);
                else if (File.Exists(current))
                    info = new FileInfo(current);
                if (info?.LinkTarget != null)
                {
                    if (!isFinalComponent) return true;
                    if (FinalSymlinkEscapesSandbox(current, info.LinkTarget))
                        return true;
                }
                if (info == null) return false;
#else
                if (!Directory.Exists(current)
                    && !File.Exists(current))
                    return false;
#endif
            }
            return false;
        }

        // Resolve a final-component symlink's literal target
        // (which may be relative or absolute) against the
        // parent directory of the symlink, then test whether
        // the resolved location is still inside the sandbox
        // root. Returns true ONLY when the symlink would
        // escape; that's the case where we surface
        // NotPermitted.
        private bool FinalSymlinkEscapesSandbox(
            string linkPath, string linkTarget)
        {
            var parent = Path.GetDirectoryName(linkPath) ?? _rootPath;
            string resolved = Path.IsPathRooted(linkTarget)
                ? Path.GetFullPath(linkTarget)
                : Path.GetFullPath(Path.Combine(parent, linkTarget));
            var rootSep = _rootPath.EndsWith(
                Path.DirectorySeparatorChar.ToString())
                ? _rootPath
                : _rootPath + Path.DirectorySeparatorChar;
            return resolved != _rootPath
                && !resolved.StartsWith(rootSep, StringComparison.Ordinal);
        }

        // Encode a `result<_, error-code>` value as the 20-byte
        // canonical-ABI payload for the canon-async future-read
        // scaffolding to memcpy into guest memory. Layout:
        //   +0:    result-disc:u8 (0=Ok, 1=Err) + 3-byte pad
        //   +4..20: error-code variant (16 bytes — disc:u8 +
        //           option<string> at +4..12; the option-string
        //           "other" payload would need a realloc, which
        //           we skip; the spec accepts the Other case
        //           with no detail string).
        // Ok: returns 20 bytes of zeros.
        private static readonly byte[] _resultErrCodeOkPayload =
            new byte[20];
        private static byte[] EncodeResultErrCodeErr(FilesystemException ex)
        {
            var bytes = new byte[20];
            bytes[0] = 1;
            bytes[4] = (byte)ex.Code;
            return bytes;
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
                        FileShare.ReadWrite | FileShare.Delete);
                    // u64::MAX → -1 in long is the spec's
                    // "Invalid" sentinel; the in-range case
                    // where offset > length is allowed (POSIX
                    // pread semantics) and naturally returns 0
                    // bytes at the first ReadAsync.
                    if (offset.Value > long.MaxValue)
                        throw new FilesystemException(
                            ErrorCode.Invalid,
                            $"offset {offset.Value} exceeds host " +
                            "long range.");
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
                    dispatcher.FutureWrite(futureHandle, _resultErrCodeOkPayload);
                }
                catch (Exception ex)
                {
                    dispatcher.StreamDropWritable(streamHandle);
                    dispatcher.FutureWrite(futureHandle,
                        EncodeResultErrCodeErr(ToFilesystem(ex)));
                }
            });
            return (streamHandle, futureHandle, completion);
        }

        public (int futureHandle, Task WriteCompletion) WriteViaStream(
            AsyncDispatcher dispatcher, int streamHandle, FileSize offset)
            => SetupSyncWriteSink(dispatcher, streamHandle,
                openFs: () =>
                {
                    var fs = new FileStream(
                        _absolutePath, FileMode.OpenOrCreate,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete);
                    fs.Position = (long)offset.Value;
                    return fs;
                });

        public (int futureHandle, Task AppendCompletion) AppendViaStream(
            AsyncDispatcher dispatcher, int streamHandle)
            => SetupSyncWriteSink(dispatcher, streamHandle,
                openFs: () => new FileStream(
                    _absolutePath, FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete));

        // Common write-via-stream / append-via-stream plumbing:
        // bind a synchronous file-write sink to the stream
        // handle so that each guest `tx.write(chunk).await`
        // immediately writes to the file and flushes, then
        // stat() reflects the new size before the next guest
        // continuation step. The completion future resolves
        // when the writer-side drops.
        private (int futureHandle, Task Completion) SetupSyncWriteSink(
            AsyncDispatcher dispatcher, int streamHandle,
            Func<FileStream> openFs)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);
            FileStream? fs;
            try { fs = openFs(); }
            catch (Exception ex)
            {
                dispatcher.FutureWrite(futureHandle,
                    EncodeResultErrCodeErr(ToFilesystem(ex)));
                return (futureHandle, Task.CompletedTask);
            }
            FilesystemException? err = null;
            dispatcher.BindStreamSyncWriteSink(streamHandle,
                onWrite: bytes =>
                {
                    if (err != null) return;
                    try
                    {
                        var arr = bytes.ToArray();
                        fs.Write(arr, 0, arr.Length);
                        fs.Flush();
                    }
                    catch (Exception ex) { err = ToFilesystem(ex); }
                },
                onClose: () =>
                {
                    try { fs.Dispose(); }
                    catch (Exception ex)
                    { err ??= ToFilesystem(ex); }
                    if (err == null)
                        dispatcher.FutureWrite(
                            futureHandle, _resultErrCodeOkPayload);
                    else
                        dispatcher.FutureWrite(
                            futureHandle,
                            EncodeResultErrCodeErr(err));
                });
            return (futureHandle, Task.CompletedTask);
        }

        public Task AdviseAsync(
            FileSize offset, FileSize length, Advice advice,
            CancellationToken cancellationToken = default)
        {
            // Spec: advise on a non-regular-file (directory,
            // symlink, etc.) returns BadDescriptor.
            if (_isDirectory)
                throw new FilesystemException(
                    ErrorCode.BadDescriptor,
                    "advise: not valid on a directory.");
            // .NET doesn't expose posix_fadvise; the spec
            // permits implementations to treat advice as a
            // hint and ignore it.
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
                    if ((_flags & DescriptorFlags.Write) == 0)
                        throw new FilesystemException(
                            ErrorCode.Invalid,
                            "set-size: descriptor opened without " +
                            "the WRITE flag.");
                    if (size.Value > long.MaxValue)
                        throw new FilesystemException(
                            ErrorCode.Invalid,
                            $"set-size: size {size.Value} exceeds " +
                            "host long range.");
                    // Prefer the open fd so set-size survives
                    // sibling unlinks; fall back to opening by
                    // path when no fd is held (e.g. directory).
                    if (_fileStream != null)
                    {
                        _fileStream.SetLength((long)size.Value);
                    }
                    else
                    {
                        using var fs = new FileStream(
                            _absolutePath, FileMode.Open,
                            FileAccess.Write, FileShare.None);
                        fs.SetLength((long)size.Value);
                    }
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

            // 24 bytes per directory-entry record (canonical-ABI
            // size). The stream is typed — the canon-async
            // stream-read scaffolding negotiates in item counts
            // not byte counts, so flag the slot's item size up
            // front. +24 headroom for any final pad/flush;
            // minimum 64 to avoid pathological tiny capacities.
            const int DirectoryEntryItemSize = 24;
            int capacity = System.Math.Max(
                64, entries.Length * DirectoryEntryItemSize + 24);
            var streamHandle = dispatcher.StreamNew(
                typeIdx: 0, capacity: capacity);
            dispatcher.SetStreamItemSize(
                streamHandle, DirectoryEntryItemSize);
            var futureHandle = dispatcher.FutureNew(typeIdx: 0);

            try
            {
                foreach (var entry in entries)
                {
                    WriteDirectoryEntry(
                        dispatcher, streamHandle, realloc, entry);
                }
                dispatcher.StreamDropWritable(streamHandle);
                dispatcher.FutureWrite(futureHandle, _resultErrCodeOkPayload);
            }
            catch (Exception ex)
            {
                dispatcher.StreamDropWritable(streamHandle);
                dispatcher.FutureWrite(futureHandle,
                    EncodeResultErrCodeErr(ToFilesystem(ex)));
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
            // Determine type. Check LinkTarget FIRST so a
            // symlink that points to a directory doesn't get
            // mistakenly labeled Directory by the Directory.Exists
            // fast path. FileSystemInfo.LinkTarget is the
            // cross-platform "is this entry a symlink?" probe
            // on .NET 6+ — works without inspecting
            // platform-specific attribute bits.
            DescriptorType type;
#if NET6_0_OR_GREATER
            FileSystemInfo? probe = null;
            try
            {
                if (Directory.Exists(entryPath))
                    probe = new DirectoryInfo(entryPath);
                else if (File.Exists(entryPath))
                    probe = new FileInfo(entryPath);
            }
            catch { /* fall through */ }
            if (probe != null && probe.LinkTarget != null)
                type = DescriptorType.SymbolicLink;
            else if (Directory.Exists(entryPath))
                type = DescriptorType.Directory;
            else if (File.Exists(entryPath))
                type = DescriptorType.RegularFile;
            else
                type = DescriptorType.Other(null);
#else
            if (Directory.Exists(entryPath))
                type = DescriptorType.Directory;
            else if (File.Exists(entryPath))
                type = DescriptorType.RegularFile;
            else
                type = DescriptorType.Other(null);
#endif

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

#if NET6_0_OR_GREATER
            if (Environment.GetEnvironmentVariable("WACS_TRACE_FS") == "1")
                Console.Error.WriteLine(
                    $"[fs] dir-entry path=\"{Path.GetFileName(entryPath)}\" " +
                    $"type={type.Kind} name=\"{name}\" namePtr=0x{namePtr:X} " +
                    $"nameLen={nameBytes.Length}");
#endif
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
            => Task.Run(() =>
            {
                // For file descriptors with an open FileStream
                // we stat via the open fd so the descriptor
                // stays valid across a sibling unlink-file-at
                // (Unix fd-survives-unlink semantics).
                if (_fileStream != null)
                {
                    return new DescriptorStat
                    {
                        Type = DescriptorType.RegularFile,
                        LinkCount = new LinkCount(1),
                        Size = new FileSize((ulong)_fileStream.Length),
                        DataAccessTimestamp = DateTimeToInstant(
                            File.Exists(_absolutePath)
                                ? File.GetLastAccessTimeUtc(_absolutePath)
                                : UnixEpoch),
                        DataModificationTimestamp = DateTimeToInstant(
                            File.Exists(_absolutePath)
                                ? File.GetLastWriteTimeUtc(_absolutePath)
                                : UnixEpoch),
                        StatusChangeTimestamp = DateTimeToInstant(
                            File.Exists(_absolutePath)
                                ? File.GetLastWriteTimeUtc(_absolutePath)
                                : UnixEpoch),
                    };
                }
                return StatPath(_absolutePath);
            }, cancellationToken);

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
            => Task.Run(() =>
            {
                try
                {
                    var oldResolved = ResolveChild(oldPath);
                    if (newDescriptor is not Descriptor newDesc)
                        throw new FilesystemException(
                            ErrorCode.Invalid,
                            "link-at: target descriptor not a " +
                            "System.IO-backed Descriptor.");
                    var newResolved = newDesc.ResolveChild(newPath);
                    if (Directory.Exists(oldResolved))
                        throw new FilesystemException(
                            ErrorCode.NotPermitted,
                            "link-at: cannot hard-link a directory.");
                    if (!File.Exists(oldResolved))
                        throw new FilesystemException(
                            ErrorCode.NoEntry,
                            $"'{oldPath}' does not exist.");
                    if (File.Exists(newResolved)
                        || Directory.Exists(newResolved))
                        throw new FilesystemException(
                            ErrorCode.Exist,
                            $"'{newPath}' already exists.");
                    NativeLink.CreateHardLink(oldResolved, newResolved);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

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

                    // EXCLUSIVE is only meaningful when CREATE is
                    // also set — the wasip3 flags-and-type fixture
                    // explicitly opens an existing file with
                    // EXCLUSIVE-but-no-CREATE and expects success.
                    if ((openFlags & OpenFlags.Exclusive) != 0
                        && (openFlags & OpenFlags.Create) != 0
                        && (File.Exists(resolved)
                            || Directory.Exists(resolved)))
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
                        // Use FileShare.ReadWrite|Delete so we
                        // don't trip over a sibling Descriptor
                        // that's already holding the file open.
                        using var trunc = new FileStream(
                            resolved, FileMode.Open,
                            FileAccess.Write,
                            FileShare.ReadWrite | FileShare.Delete);
                        trunc.SetLength(0);
                    }

                    if (!File.Exists(resolved) && !Directory.Exists(resolved))
                        throw new FilesystemException(
                            ErrorCode.NoEntry,
                            $"'{path}' does not exist.");

                    bool isDirectory = Directory.Exists(resolved);

                    // Spec normalization (per the wasip3 flags-
                    // and-type fixture):
                    //  - Opening a directory with the WRITE flag
                    //    is invalid; directories use
                    //    MUTATE_DIRECTORY instead.
                    //  - Empty descriptor flags default to READ.
                    //  - CREATE implies WRITE on the resulting
                    //    descriptor even when not requested.
                    if (isDirectory
                        && (flags & DescriptorFlags.Write) != 0)
                        throw new FilesystemException(
                            ErrorCode.IsDirectory,
                            $"'{path}' is a directory; cannot open " +
                            "with WRITE flag.");
                    if (flags == 0)
                        flags = DescriptorFlags.Read;
                    if ((openFlags & OpenFlags.Create) != 0)
                        flags |= DescriptorFlags.Write;

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
                    // Renaming the root (".") is invalid per spec
                    // — the fixture accepts Busy / Invalid /
                    // Access. We surface Invalid since neither
                    // the source nor destination is misused on a
                    // resource-busy basis.
                    if (oldResolved == _rootPath
                        || oldResolved == _absolutePath)
                        throw new FilesystemException(
                            ErrorCode.Invalid,
                            $"cannot rename preopen root '{oldPath}'.");
                    if (!File.Exists(oldResolved)
                        && !Directory.Exists(oldResolved))
                        throw new FilesystemException(
                            ErrorCode.NoEntry,
                            $"'{oldPath}' does not exist.");
                    // mv(self, self) on an existing path is a
                    // no-op per spec (rename to current name).
                    if (oldResolved == newResolved) return;
                    // POSIX rename overwrites; .NET File.Move
                    // throws when destination exists. Delete
                    // first to match POSIX semantics. (Symlinks
                    // are deleted by File.Delete — even though
                    // File.Exists may report false for a
                    // broken symlink, the delete still removes
                    // the link entry.)
                    if (File.Exists(newResolved))
                        File.Delete(newResolved);
                    else if (Directory.Exists(newResolved))
                        Directory.Delete(newResolved);
                    if (File.Exists(oldResolved)
                        || IsSymbolicLink(oldResolved))
                        File.Move(oldResolved, newResolved);
                    else
                        Directory.Move(oldResolved, newResolved);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

        private static bool IsSymbolicLink(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                return fi.Exists
                    && (fi.Attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch { return false; }
        }

        public Task SymlinkAtAsync(
            string oldPath, string newPath,
            CancellationToken cancellationToken = default)
            => Task.Run(() =>
            {
                try
                {
                    // newPath is sandbox-scoped; oldPath is the
                    // symlink target as stored in the link (NOT
                    // resolved — it can be any string).
                    var newResolved = ResolveChild(newPath);
                    if (File.Exists(newResolved)
                        || Directory.Exists(newResolved))
                        throw new FilesystemException(
                            ErrorCode.Exist,
                            $"'{newPath}' already exists.");
                    File.CreateSymbolicLink(newResolved, oldPath);
                }
                catch (Exception ex) { throw ToFilesystem(ex); }
            }, cancellationToken);

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
            => Task.FromResult(IsSameObject(other));

        private bool IsSameObject(IDescriptor other)
        {
            if (other is not Descriptor d) return false;
            // Fast path: same wrapper instance / same path.
            if (string.Equals(d._absolutePath, _absolutePath,
                    StringComparison.Ordinal))
                return true;
            // Hard-link case: two distinct paths backed by the
            // same inode (filesystem-is-same-object exercises
            // this). NativeLink.TryGetInode falls back to false
            // on Windows / when stat() fails; in that case we
            // already returned the path-equality answer above.
            if (NativeLink.TryGetInode(_absolutePath, out var selfIno)
                && NativeLink.TryGetInode(d._absolutePath, out var otherIno))
                return selfIno == otherIno;
            return false;
        }

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
                // Directories carry MUTATE_DIRECTORY rather
                // than WRITE — WRITE is a file-only stream flag
                // in the spec, MUTATE_DIRECTORY is the verb-
                // permitter for mkdir/rename/unlink/etc.
                var desc = new Descriptor(
                    host, host,
                    DescriptorFlags.Read
                        | DescriptorFlags.MutateDirectory);
                list.Add(new DescriptorPreopen(desc, guest));
            }
            return new DirectoryPreopens(list);
        }

        public IReadOnlyList<DescriptorPreopen> GetDirectories() => _preopens;
    }
}
