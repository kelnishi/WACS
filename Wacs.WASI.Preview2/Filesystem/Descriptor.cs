// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Filesystem
{
    /// <summary>WIT flags
    /// <c>wasi:filesystem/types.path-flags</c>. Wire form is
    /// i32 with one bit per flag.</summary>
    [System.Flags]
    public enum PathFlags : uint
    {
        None = 0,
        SymlinkFollow = 1 << 0,
    }

    /// <summary>WIT flags
    /// <c>wasi:filesystem/types.open-flags</c>.</summary>
    [System.Flags]
    public enum OpenFlags : uint
    {
        None = 0,
        Create = 1 << 0,
        Directory = 1 << 1,
        Exclusive = 1 << 2,
        Truncate = 1 << 3,
    }

    /// <summary>WIT flags
    /// <c>wasi:filesystem/types.descriptor-flags</c>.</summary>
    [System.Flags]
    public enum DescriptorFlags : uint
    {
        None = 0,
        Read = 1 << 0,
        Write = 1 << 1,
        FileIntegritySync = 1 << 2,
        DataIntegritySync = 1 << 3,
        RequestedWriteSync = 1 << 4,
        MutateDirectory = 1 << 5,
    }

    /// <summary>WIT enum
    /// <c>wasi:filesystem/types.error-code</c>. The full
    /// v0.2.3 spec has 37 cases, captured here as a byte
    /// enum with declaration-order values. Used as the
    /// Some payload of <c>filesystem-error-code()</c>.
    /// </summary>
    public enum FilesystemErrorCode : byte
    {
        Access = 0,
        WouldBlock = 1,
        Already = 2,
        BadDescriptor = 3,
        Busy = 4,
        Deadlock = 5,
        Quota = 6,
        Exist = 7,
        FileTooLarge = 8,
        IllegalByteSequence = 9,
        InProgress = 10,
        Interrupted = 11,
        Invalid = 12,
        Io = 13,
        IsDirectory = 14,
        Loop = 15,
        TooManyLinks = 16,
        MessageSize = 17,
        NameTooLong = 18,
        NoDevice = 19,
        NoEntry = 20,
        NoLock = 21,
        InsufficientMemory = 22,
        InsufficientSpace = 23,
        NotDirectory = 24,
        NotEmpty = 25,
        NotRecoverable = 26,
        Unsupported = 27,
        NoTty = 28,
        NoSuchDevice = 29,
        Overflow = 30,
        NotPermitted = 31,
        Pipe = 32,
        ReadOnly = 33,
        InvalidSeek = 34,
        TextFileBusy = 35,
        CrossDevice = 36,
    }

    /// <summary>Host-side surface for the top-level
    /// <c>wasi:filesystem/types.filesystem-error-code</c>:
    /// <code>filesystem-error-code: func(err: borrow&lt;error&gt;)
    ///     -&gt; option&lt;error-code&gt;;</code>
    /// Returns null when <paramref name="err"/> is not a
    /// filesystem error; matching <see cref="FilesystemErrorCode"/>
    /// otherwise.</summary>
    public interface IFilesystemErrorCode
    {
        FilesystemErrorCode? FilesystemErrorCode(
            Wacs.WASI.Preview2.Io.Error err);
    }

    /// <summary>Default <see cref="IFilesystemErrorCode"/>
    /// — returns null regardless of input. Concrete hosts
    /// override to classify <see cref="Wacs.WASI.Preview2.Io.Error"/>
    /// instances as filesystem errors.</summary>
    public sealed class FilesystemErrorCodeSource : IFilesystemErrorCode
    {
        public FilesystemErrorCode? FilesystemErrorCode(
            Wacs.WASI.Preview2.Io.Error err) => null;
    }

    /// <summary>WIT enum
    /// <c>wasi:filesystem/types.advice</c> — hint for
    /// descriptor.advise().</summary>
    public enum Advice : byte
    {
        Normal = 0,
        Sequential = 1,
        Random = 2,
        WillNeed = 3,
        DontNeed = 4,
        NoReuse = 5,
    }

    /// <summary>WIT flags
    /// <c>wasi:filesystem/types.modes</c> — accessibility
    /// modes used by access-type's access(modes) case.</summary>
    [System.Flags]
    public enum AccessModes : uint
    {
        None = 0,
        Readable = 1 << 0,
        Writable = 1 << 1,
        Executable = 1 << 2,
    }

    /// <summary>WIT variant
    /// <c>wasi:filesystem/types.access-type</c>:
    /// <code>variant access-type {
    ///   access(modes),       // bit-mask of which modes to test
    ///   exists,              // existence-only check
    /// }</code>
    /// Wire form: 2 flat slots (variant disc + modes-or-pad).
    /// </summary>
    public abstract class AccessType { }

    /// <summary>access-type case "access(modes)".</summary>
    public sealed class AccessTypeAccess : AccessType
    {
        public AccessModes Modes { get; }
        public AccessTypeAccess(AccessModes modes) { Modes = modes; }
    }

    /// <summary>access-type case "exists" (no payload).</summary>
    public sealed class AccessTypeExists : AccessType { }

    /// <summary>WIT variant
    /// <c>wasi:filesystem/types.new-timestamp</c>:
    /// <code>variant new-timestamp {
    ///   no-change, now, timestamp(datetime),
    /// }</code>
    /// Used as a param to <see cref="Descriptor.SetTimes"/>.
    /// Wire form: variant disc (u8) + datetime payload at
    /// align-8 offset; flat-lowered as 3 wire slots
    /// (disc + i64 seconds + i32 nanoseconds).</summary>
    public abstract class NewTimestamp { }

    /// <summary>"Don't change this timestamp."</summary>
    public sealed class NewTimestampNoChange : NewTimestamp { }

    /// <summary>"Set this timestamp to now."</summary>
    public sealed class NewTimestampNow : NewTimestamp { }

    /// <summary>"Set this timestamp to the given
    /// <see cref="Wacs.WASI.Preview2.Clocks.Datetime"/>."</summary>
    public sealed class NewTimestampTimestamp : NewTimestamp
    {
        public Datetime Value { get; }
        public NewTimestampTimestamp(Datetime value) { Value = value; }
    }

    /// <summary>WIT record
    /// <c>wasi:filesystem/types.metadata-hash-value</c>:
    /// <code>record metadata-hash-value { lower: u64, upper: u64 }</code>
    /// </summary>
    public struct MetadataHashValue
    {
        public ulong Lower;
        public ulong Upper;

        public MetadataHashValue(ulong lower, ulong upper)
        {
            Lower = lower;
            Upper = upper;
        }
    }

    /// <summary>WIT record
    /// <c>wasi:filesystem/types.directory-entry</c>:
    /// <code>record directory-entry {
    ///     type: descriptor-type,
    ///     name: string,
    /// }</code>
    /// </summary>
    public sealed class DirectoryEntry
    {
        public DescriptorType Type { get; }
        public string Name { get; }

        public DirectoryEntry(DescriptorType type, string name)
        {
            Type = type;
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }

    /// <summary>WIT record
    /// <c>wasi:filesystem/types.descriptor-stat</c> — the
    /// "stat" payload returned by descriptor.stat. Three
    /// option<datetime> fields cover access / modification /
    /// status-change timestamps; null on any of them means
    /// the host doesn't track that timestamp.
    /// <code>
    /// record descriptor-stat {
    ///   type: descriptor-type,
    ///   link-count: u64,
    ///   size: u64,
    ///   data-access-timestamp: option<datetime>,
    ///   data-modification-timestamp: option<datetime>,
    ///   status-change-timestamp: option<datetime>,
    /// }
    /// </code>
    /// Wire size 96, align 8. The result wrapper bumps total
    /// retArea to 104 bytes.</summary>
    public sealed class DescriptorStat
    {
        public DescriptorType Type { get; }
        public ulong LinkCount { get; }
        public ulong Size { get; }
        public Datetime? DataAccessTimestamp { get; }
        public Datetime? DataModificationTimestamp { get; }
        public Datetime? StatusChangeTimestamp { get; }

        public DescriptorStat(DescriptorType type, ulong linkCount,
            ulong size,
            Datetime? dataAccessTimestamp,
            Datetime? dataModificationTimestamp,
            Datetime? statusChangeTimestamp)
        {
            Type = type;
            LinkCount = linkCount;
            Size = size;
            DataAccessTimestamp = dataAccessTimestamp;
            DataModificationTimestamp = dataModificationTimestamp;
            StatusChangeTimestamp = statusChangeTimestamp;
        }
    }

    /// <summary>WIT enum
    /// <c>wasi:filesystem/types.descriptor-type</c>. The wire
    /// representation is a 1-byte discriminator; the host
    /// returns the C# enum and the binder reads its
    /// underlying byte.</summary>
    public enum DescriptorType : byte
    {
        Unknown = 0,
        BlockDevice = 1,
        CharacterDevice = 2,
        Directory = 3,
        Fifo = 4,
        SymbolicLink = 5,
        RegularFile = 6,
        Socket = 7,
    }

    /// <summary>
    /// Host representation of <c>wasi:filesystem/types@0.2.x</c>'s
    /// <c>descriptor</c> resource. Stands in for an opened file
    /// or directory.
    ///
    /// <para>v0 ships the bridge methods (read-via-stream,
    /// write-via-stream, append-via-stream) that turn a
    /// descriptor into an input/output-stream. The remaining
    /// ~25 descriptor methods (open-at, stat, get-type,
    /// readlink-at, etc.) ride incrementally as their wire
    /// shapes become tractable.</para>
    /// </summary>
    [WasiResource("descriptor")]
    public class Descriptor : IDisposable
    {
        /// <summary>Underlying filesystem path the descriptor
        /// represents — used by host implementations that
        /// proxy to <see cref="System.IO.File"/> /
        /// <see cref="System.IO.Directory"/>.</summary>
        public string Path { get; }

        public Descriptor(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        /// <summary>Report the descriptor's WIT type. Default
        /// inspects the host filesystem: directories yield
        /// <see cref="DescriptorType.Directory"/>, files
        /// <see cref="DescriptorType.RegularFile"/>, otherwise
        /// <see cref="DescriptorType.Unknown"/>. Subclasses
        /// override for virtual paths.
        ///
        /// <para>Method is named <c>GetDescriptorType</c> rather
        /// than <c>GetType</c> to avoid the
        /// <see cref="object.GetType"/> clash; the
        /// <see cref="WasiMethodNameAttribute"/> override
        /// restores the WIT name <c>get-type</c>.</para></summary>
        public virtual DescriptorType GetDescriptorType()
        {
            if (Directory.Exists(Path)) return DescriptorType.Directory;
            if (File.Exists(Path)) return DescriptorType.RegularFile;
            return DescriptorType.Unknown;
        }

        /// <summary>Open the file for reading from
        /// <paramref name="offset"/>. Returns an
        /// <see cref="InputStream"/> wrapping the underlying
        /// host file. Default impl uses
        /// <see cref="System.IO.File.OpenRead"/>; subclasses
        /// override for virtual file systems.</summary>
        public virtual InputStream ReadViaStream(ulong offset)
        {
            var stream = File.OpenRead(Path);
            if (offset > 0)
                stream.Seek((long)offset, SeekOrigin.Begin);
            return new HostFileInputStream(stream);
        }

        /// <summary>Open the file for writing at
        /// <paramref name="offset"/>. Returns an
        /// <see cref="OutputStream"/> wrapping the underlying
        /// host file.</summary>
        public virtual OutputStream WriteViaStream(ulong offset)
        {
            var stream = File.OpenWrite(Path);
            if (offset > 0)
                stream.Seek((long)offset, SeekOrigin.Begin);
            return new HostFileOutputStream(stream);
        }

        /// <summary>Open the file for appending. Returns an
        /// <see cref="OutputStream"/> positioned at EOF.</summary>
        public virtual OutputStream AppendViaStream()
        {
            var stream = new FileStream(Path, FileMode.Append,
                FileAccess.Write);
            return new HostFileOutputStream(stream);
        }

        /// <summary>Force any buffered modifications to be
        /// flushed to durable storage. Default: no-op (host
        /// streams flush on close anyway).</summary>
        public virtual void Sync() { }

        /// <summary>Like <see cref="Sync"/> but only flushes
        /// data, not metadata. Default: no-op.</summary>
        public virtual void SyncData() { }

        /// <summary>Truncate or extend the file to
        /// <paramref name="size"/> bytes.</summary>
        public virtual void SetSize(ulong size)
        {
            using var fs = new FileStream(Path, FileMode.Open,
                FileAccess.Write);
            fs.SetLength((long)size);
        }

        /// <summary>Direct read from the file at
        /// <paramref name="offset"/> for up to
        /// <paramref name="length"/> bytes. Returns the bytes
        /// actually read paired with an EOF flag (true iff
        /// the read hit end-of-file before consuming all
        /// requested bytes). The streams interface goes through
        /// <see cref="ReadViaStream"/>; this is the direct path
        /// for guests that don't want a stream's buffering.</summary>
        public virtual (byte[], bool) Read(ulong length, ulong offset)
        {
            using var fs = File.OpenRead(Path);
            if (offset > 0)
                fs.Seek((long)offset, SeekOrigin.Begin);
            int n = (int)System.Math.Min(length, (ulong)int.MaxValue);
            var buf = new byte[n];
            int read = fs.Read(buf, 0, n);
            bool atEof = fs.Position >= fs.Length;
            if (read == n) return (buf, atEof);
            var slice = new byte[read];
            System.Array.Copy(buf, 0, slice, 0, read);
            return (slice, atEof);
        }

        /// <summary>Write <paramref name="buffer"/> at
        /// <paramref name="offset"/>. Returns count actually
        /// written.</summary>
        public virtual ulong Write(byte[] buffer, ulong offset)
        {
            using var fs = new FileStream(Path, FileMode.OpenOrCreate,
                FileAccess.Write);
            if (offset > 0)
                fs.Seek((long)offset, SeekOrigin.Begin);
            fs.Write(buffer, 0, buffer.Length);
            return (ulong)buffer.Length;
        }

        /// <summary>Create a directory at <paramref name="path"/>
        /// relative to this descriptor (treated as a directory).
        /// </summary>
        public virtual void CreateDirectoryAt(string path)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, path));
        }

        /// <summary>Remove the empty directory at
        /// <paramref name="path"/>.</summary>
        public virtual void RemoveDirectoryAt(string path)
        {
            Directory.Delete(System.IO.Path.Combine(Path, path));
        }

        /// <summary>Unlink (delete) the file at
        /// <paramref name="path"/>.</summary>
        public virtual void UnlinkFileAt(string path)
        {
            File.Delete(System.IO.Path.Combine(Path, path));
        }

        /// <summary>Hard-link <paramref name="oldPath"/> under
        /// <c>this</c> to <paramref name="newPath"/> under
        /// <paramref name="newDescriptor"/>. Default impl is a
        /// no-op since System.IO has no portable hard-link
        /// API; concrete subclasses override.</summary>
        public virtual void LinkAt(PathFlags oldPathFlags, string oldPath,
            Descriptor newDescriptor, string newPath) { }

        /// <summary>Rename <paramref name="oldPath"/> under
        /// <c>this</c> to <paramref name="newPath"/> under
        /// <paramref name="newDescriptor"/>.</summary>
        public virtual void RenameAt(string oldPath,
            Descriptor newDescriptor, string newPath)
        {
            var oldFull = System.IO.Path.Combine(Path, oldPath);
            var newFull = System.IO.Path.Combine(newDescriptor.Path, newPath);
            if (File.Exists(oldFull))
                File.Move(oldFull, newFull);
            else if (Directory.Exists(oldFull))
                Directory.Move(oldFull, newFull);
        }

        /// <summary>Create a symbolic link at
        /// <paramref name="newPath"/> pointing at
        /// <paramref name="oldPath"/>. Default impl is a no-op
        /// since System.IO has no portable symlink API on
        /// netstandard2.1 — concrete subclasses override.
        /// </summary>
        public virtual void SymlinkAt(string oldPath, string newPath) { }

        /// <summary>Open or create a file/directory relative to
        /// this descriptor (treated as a directory) at
        /// <paramref name="path"/>. Returns a fresh
        /// <see cref="Descriptor"/> for the opened entry.
        /// Default impl honors <see cref="OpenFlags.Create"/> by
        /// touching the target file; the resulting descriptor's
        /// behavior comes from the base class.</summary>
        public virtual Descriptor OpenAt(PathFlags pathFlags, string path,
            OpenFlags openFlags, DescriptorFlags flags)
        {
            var fullPath = System.IO.Path.Combine(Path, path);
            if ((openFlags & OpenFlags.Create) != 0
                && !File.Exists(fullPath)
                && !Directory.Exists(fullPath))
            {
                File.WriteAllBytes(fullPath, System.Array.Empty<byte>());
            }
            return new Descriptor(fullPath);
        }

        /// <summary>Read the target of the symbolic link at
        /// <paramref name="path"/>. Default impl returns
        /// the empty string — concrete subclasses override
        /// (System.IO has no portable readlink in
        /// netstandard2.1).</summary>
        public virtual string ReadlinkAt(string path) => "";

        /// <summary>Inspect file metadata. Returns a
        /// <see cref="DescriptorStat"/> with type / link-count
        /// / size + the three timestamps. Default impl reads
        /// from System.IO.File / Directory; concrete VFS
        /// shims override.</summary>
        public virtual DescriptorStat Stat()
        {
            DescriptorType type = GetDescriptorType();
            ulong linkCount = 1;   // POSIX hardlink count; host-
                                   // only implementations rarely
                                   // know this exactly.
            ulong size = 0;
            Datetime? mtime = null;
            Datetime? atime = null;
            Datetime? ctime = null;
            if (type == DescriptorType.RegularFile)
            {
                var fi = new FileInfo(Path);
                size = (ulong)fi.Length;
                atime = ToDatetime(fi.LastAccessTimeUtc);
                mtime = ToDatetime(fi.LastWriteTimeUtc);
                ctime = ToDatetime(fi.CreationTimeUtc);
            }
            else if (type == DescriptorType.Directory)
            {
                var di = new DirectoryInfo(Path);
                atime = ToDatetime(di.LastAccessTimeUtc);
                mtime = ToDatetime(di.LastWriteTimeUtc);
                ctime = ToDatetime(di.CreationTimeUtc);
            }
            return new DescriptorStat(type, linkCount, size,
                atime, mtime, ctime);
        }

        /// <summary>Inspect file metadata at a path relative
        /// to this descriptor (treated as a directory). Same
        /// shape as <see cref="Stat"/> but takes a relative
        /// path; useful for guests walking trees without
        /// opening every entry.</summary>
        public virtual DescriptorStat StatAt(PathFlags pathFlags,
            string path)
        {
            var fullPath = System.IO.Path.Combine(Path, path);
            return new Descriptor(fullPath).Stat();
        }

        private static Datetime ToDatetime(DateTime dt)
        {
            // .NET DateTime → Unix epoch seconds + remainder
            // nanoseconds. WIT datetime expects "wall-clock
            // seconds since the Unix epoch".
            var diff = dt - new DateTime(1970, 1, 1,
                0, 0, 0, DateTimeKind.Utc);
            long ticks = diff.Ticks;
            ulong seconds = (ulong)(ticks / TimeSpan.TicksPerSecond);
            uint nanos = (uint)((ticks % TimeSpan.TicksPerSecond) * 100);
            return new Datetime { Seconds = seconds, Nanoseconds = nanos };
        }

        /// <summary>Open the directory for reading entries.
        /// Returns a <see cref="DirectoryEntryStream"/> the
        /// guest pulls entries from one at a time.</summary>
        public virtual DirectoryEntryStream ReadDirectory()
        {
            if (!Directory.Exists(Path))
                return new DirectoryEntryStream(
                    System.Array.Empty<DirectoryEntry>());
            var entries = new System.Collections.Generic.List<DirectoryEntry>();
            foreach (var d in Directory.EnumerateDirectories(Path))
                entries.Add(new DirectoryEntry(DescriptorType.Directory,
                    System.IO.Path.GetFileName(d) ?? ""));
            foreach (var f in Directory.EnumerateFiles(Path))
                entries.Add(new DirectoryEntry(DescriptorType.RegularFile,
                    System.IO.Path.GetFileName(f) ?? ""));
            return new DirectoryEntryStream(entries.ToArray());
        }

        /// <summary>Stable hash of the file's identity (inode +
        /// device on POSIX). Two descriptors referring to the
        /// same underlying file return equal values across
        /// <c>get-type</c> changes; default impl hashes
        /// <see cref="Path"/>.</summary>
        public virtual MetadataHashValue MetadataHash()
        {
            // Cheap deterministic hash from the path; concrete
            // hosts override with stat()-derived inode/device
            // pairs.
            ulong lower = (ulong)Path.GetHashCode();
            ulong upper = lower * 0x9E3779B97F4A7C15UL;
            return new MetadataHashValue(lower, upper);
        }

        /// <summary>Read the descriptor's open / mutability
        /// flag set. Default returns Read | Write. Concrete
        /// hosts override based on how the descriptor was
        /// obtained.</summary>
        public virtual DescriptorFlags GetFlags()
            => DescriptorFlags.Read | DescriptorFlags.Write;

        /// <summary>Hint at access pattern for the descriptor's
        /// underlying file. Default impl is a no-op — concrete
        /// hosts wire to posix_fadvise on POSIX.</summary>
        public virtual void Advise(ulong offset, ulong length,
            Advice advice) { }

        /// <summary>Test existence / accessibility at a path.
        /// Default impl is a no-op stub; concrete hosts
        /// override.</summary>
        public virtual void AccessAt(PathFlags pathFlags, string path,
            AccessType type) { }

        /// <summary>Update access + modification timestamps.
        /// Each arg picks one of three cases (no-change, now,
        /// or a specific datetime) — the host applies the
        /// corresponding utime semantics.</summary>
        public virtual void SetTimes(NewTimestamp dataAccessTimestamp,
            NewTimestamp dataModificationTimestamp) { }

        /// <summary>Update timestamps at a relative
        /// path.</summary>
        public virtual void SetTimesAt(PathFlags pathFlags, string path,
            NewTimestamp dataAccessTimestamp,
            NewTimestamp dataModificationTimestamp) { }

        /// <summary>Hash the file at a relative path. Pairs
        /// with <see cref="MetadataHash"/>; default impl
        /// hashes the full combined path string.</summary>
        public virtual MetadataHashValue MetadataHashAt(
            PathFlags pathFlags, string path)
        {
            var fullPath = System.IO.Path.Combine(Path, path);
            ulong lower = (ulong)fullPath.GetHashCode();
            ulong upper = lower * 0x9E3779B97F4A7C15UL;
            return new MetadataHashValue(lower, upper);
        }

        /// <summary>True iff <paramref name="other"/> refers to
        /// the same underlying filesystem object. Default
        /// compares the host-side <see cref="Path"/>; subclasses
        /// override for VFS shims that don't keep textual
        /// paths.</summary>
        public virtual bool IsSameObject(Descriptor other)
        {
            if (other == null) return false;
            return string.Equals(Path, other.Path, StringComparison.Ordinal);
        }

        public virtual void Dispose() { }
    }

    /// <summary>Host representation of
    /// <c>wasi:filesystem/types.directory-entry-stream</c>.
    /// Pull-stream of <see cref="DirectoryEntry"/> items. The
    /// base impl is array-backed — concrete hosts override
    /// <see cref="ReadDirectoryEntry"/> for stream sources
    /// that can't fully materialize ahead of time.</summary>
    [WasiResource("directory-entry-stream")]
    public class DirectoryEntryStream : IDisposable
    {
        private readonly DirectoryEntry[] _entries;
        private int _pos;

        public DirectoryEntryStream(DirectoryEntry[] entries)
        {
            _entries = entries
                ?? throw new ArgumentNullException(nameof(entries));
        }

        /// <summary>Pull the next directory entry, or null
        /// when exhausted.</summary>
        public virtual DirectoryEntry? ReadDirectoryEntry()
            => _pos < _entries.Length ? _entries[_pos++] : null;

        public virtual void Dispose() { }
    }

    /// <summary>InputStream that wraps a host
    /// <see cref="Stream"/> and disposes it on
    /// <see cref="Dispose"/>. Used by Descriptor's
    /// read-via-stream so closing the guest stream actually
    /// releases the file.</summary>
    public sealed class HostFileInputStream : InputStream
    {
        private readonly Stream _stream;
        public HostFileInputStream(Stream stream) { _stream = stream; }

        public override byte[] Read(ulong len)
        {
            int n = (int)System.Math.Min(len, (ulong)int.MaxValue);
            var buf = new byte[n];
            int read = _stream.Read(buf, 0, n);
            if (read == n) return buf;
            var slice = new byte[read];
            System.Array.Copy(buf, 0, slice, 0, read);
            return slice;
        }

        public override byte[] BlockingRead(ulong len) => Read(len);
        public override void Dispose() { _stream.Dispose(); }
    }

    /// <summary>OutputStream that wraps a host
    /// <see cref="Stream"/> and disposes it on
    /// <see cref="Dispose"/>.</summary>
    public sealed class HostFileOutputStream : OutputStream
    {
        private readonly Stream _stream;
        public HostFileOutputStream(Stream stream) { _stream = stream; }

        public override void Write(byte[] contents)
        {
            _stream.Write(contents, 0, contents.Length);
        }

        public override void BlockingWriteAndFlush(byte[] contents)
        {
            _stream.Write(contents, 0, contents.Length);
            _stream.Flush();
        }

        public override void Flush() { _stream.Flush(); }
        public override void BlockingFlush() { _stream.Flush(); }
        public override void Dispose() { _stream.Dispose(); }
    }
}
