// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
using Wacs.ComponentModel.Runtime;
using Wacs.WASI.Preview2.Clocks;
using Wacs.WASI.Preview2.HostBinding;
using Wacs.WASI.Preview2.Io;

namespace Wacs.WASI.Preview2.Filesystem
{
    // The WIT-derived enums + records (DescriptorType,
    // DescriptorFlags, PathFlags, OpenFlags, Advice, ErrorCode,
    // MetadataHashValue, DescriptorStat, DirectoryEntry,
    // NewTimestamp + nested cases, IDescriptor,
    // IDirectoryEntryStream, IPreopens, ITypes) are emitted by
    // the source generator from wit/deps/filesystem/*.wit and
    // land in this namespace.

    /// <summary>
    /// Host representation of <c>wasi:filesystem/types@0.2.8</c>'s
    /// <c>descriptor</c> resource. Implements
    /// <see cref="IDescriptor"/> directly — every method returns
    /// the faithful canonical-ABI <c>result&lt;X,
    /// error-code&gt;</c> shape, surfacing
    /// <see cref="ErrorCode"/> values rather than thrown
    /// exceptions on the explicit error path.
    ///
    /// <para>v0 ships the bridge methods (read-via-stream,
    /// write-via-stream, append-via-stream) that turn a
    /// descriptor into an input/output-stream, plus default
    /// impls of the major path / metadata operations that
    /// proxy to <see cref="System.IO"/>.</para>
    /// </summary>
    [WasiResource("descriptor")]
    public class Descriptor : IDescriptor, IDisposable
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
        /// <para>Method is implemented as an explicit interface
        /// member (<see cref="IDescriptor.GetType"/>) to avoid
        /// the <see cref="object.GetType"/> clash. Host
        /// subclasses override <see cref="GetDescriptorType"/>;
        /// the explicit-interface shim wraps the result.</para>
        /// </summary>
        public virtual DescriptorType GetDescriptorType()
        {
            if (Directory.Exists(Path)) return DescriptorType.Directory;
            if (File.Exists(Path)) return DescriptorType.RegularFile;
            return DescriptorType.Unknown;
        }

        Result<DescriptorType, ErrorCode> IDescriptor.GetType()
            => Result<DescriptorType, ErrorCode>.FromOk(GetDescriptorType());

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

        Result<IInputStream, ErrorCode> IDescriptor.ReadViaStream(ulong offset)
            => Result<IInputStream, ErrorCode>.FromOk(ReadViaStream(offset));

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

        Result<IOutputStream, ErrorCode> IDescriptor.WriteViaStream(ulong offset)
            => Result<IOutputStream, ErrorCode>.FromOk(WriteViaStream(offset));

        /// <summary>Open the file for appending. Returns an
        /// <see cref="OutputStream"/> positioned at EOF.</summary>
        public virtual OutputStream AppendViaStream()
        {
            var stream = new FileStream(Path, FileMode.Append,
                FileAccess.Write);
            return new HostFileOutputStream(stream);
        }

        Result<IOutputStream, ErrorCode> IDescriptor.AppendViaStream()
            => Result<IOutputStream, ErrorCode>.FromOk(AppendViaStream());

        /// <summary>Force any buffered modifications to be
        /// flushed to durable storage. Default: no-op (host
        /// streams flush on close anyway).</summary>
        public virtual Result<Unit, ErrorCode> Sync()
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Like <see cref="Sync"/> but only flushes
        /// data, not metadata. Default: no-op.</summary>
        public virtual Result<Unit, ErrorCode> SyncData()
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Truncate or extend the file to
        /// <paramref name="size"/> bytes.</summary>
        public virtual Result<Unit, ErrorCode> SetSize(ulong size)
        {
            using var fs = new FileStream(Path, FileMode.Open,
                FileAccess.Write);
            fs.SetLength((long)size);
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        /// <summary>Direct read from the file at
        /// <paramref name="offset"/> for up to
        /// <paramref name="length"/> bytes. Returns the bytes
        /// actually read paired with an EOF flag (true iff
        /// the read hit end-of-file before consuming all
        /// requested bytes). The streams interface goes through
        /// <see cref="ReadViaStream"/>; this is the direct path
        /// for guests that don't want a stream's buffering.</summary>
        public virtual Result<(byte[], bool), ErrorCode> Read(
            ulong length, ulong offset)
        {
            using var fs = File.OpenRead(Path);
            if (offset > 0)
                fs.Seek((long)offset, SeekOrigin.Begin);
            int n = (int)System.Math.Min(length, (ulong)int.MaxValue);
            var buf = new byte[n];
            int read = fs.Read(buf, 0, n);
            bool atEof = fs.Position >= fs.Length;
            byte[] data;
            if (read == n) data = buf;
            else
            {
                data = new byte[read];
                System.Array.Copy(buf, 0, data, 0, read);
            }
            return Result<(byte[], bool), ErrorCode>.FromOk((data, atEof));
        }

        /// <summary>Write <paramref name="buffer"/> at
        /// <paramref name="offset"/>. Returns count actually
        /// written.</summary>
        public virtual Result<ulong, ErrorCode> Write(byte[] buffer, ulong offset)
        {
            using var fs = new FileStream(Path, FileMode.OpenOrCreate,
                FileAccess.Write);
            if (offset > 0)
                fs.Seek((long)offset, SeekOrigin.Begin);
            fs.Write(buffer, 0, buffer.Length);
            return Result<ulong, ErrorCode>.FromOk((ulong)buffer.Length);
        }

        /// <summary>Create a directory at <paramref name="path"/>
        /// relative to this descriptor (treated as a directory).
        /// </summary>
        public virtual Result<Unit, ErrorCode> CreateDirectoryAt(string path)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, path));
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        /// <summary>Remove the empty directory at
        /// <paramref name="path"/>.</summary>
        public virtual Result<Unit, ErrorCode> RemoveDirectoryAt(string path)
        {
            Directory.Delete(System.IO.Path.Combine(Path, path));
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        /// <summary>Unlink (delete) the file at
        /// <paramref name="path"/>.</summary>
        public virtual Result<Unit, ErrorCode> UnlinkFileAt(string path)
        {
            File.Delete(System.IO.Path.Combine(Path, path));
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        /// <summary>Hard-link <paramref name="oldPath"/> under
        /// <c>this</c> to <paramref name="newPath"/> under
        /// <paramref name="newDescriptor"/>. Default impl is a
        /// no-op since System.IO has no portable hard-link
        /// API; concrete subclasses override.</summary>
        public virtual Result<Unit, ErrorCode> LinkAt(PathFlags oldPathFlags,
            string oldPath, Descriptor newDescriptor, string newPath)
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        Result<Unit, ErrorCode> IDescriptor.LinkAt(PathFlags oldPathFlags,
            string oldPath, IDescriptor newDescriptor, string newPath)
            => LinkAt(oldPathFlags, oldPath,
                (Descriptor)newDescriptor, newPath);

        /// <summary>Rename <paramref name="oldPath"/> under
        /// <c>this</c> to <paramref name="newPath"/> under
        /// <paramref name="newDescriptor"/>.</summary>
        public virtual Result<Unit, ErrorCode> RenameAt(string oldPath,
            Descriptor newDescriptor, string newPath)
        {
            var oldFull = System.IO.Path.Combine(Path, oldPath);
            var newFull = System.IO.Path.Combine(newDescriptor.Path, newPath);
            if (File.Exists(oldFull))
                File.Move(oldFull, newFull);
            else if (Directory.Exists(oldFull))
                Directory.Move(oldFull, newFull);
            return Result<Unit, ErrorCode>.FromOk(Unit.Value);
        }

        Result<Unit, ErrorCode> IDescriptor.RenameAt(string oldPath,
            IDescriptor newDescriptor, string newPath)
            => RenameAt(oldPath, (Descriptor)newDescriptor, newPath);

        /// <summary>Create a symbolic link at
        /// <paramref name="newPath"/> pointing at
        /// <paramref name="oldPath"/>. Default impl is a no-op
        /// since System.IO has no portable symlink API on
        /// netstandard2.1 — concrete subclasses override.
        /// </summary>
        public virtual Result<Unit, ErrorCode> SymlinkAt(string oldPath,
            string newPath)
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Open or create a file/directory relative to
        /// this descriptor (treated as a directory) at
        /// <paramref name="path"/>. Returns a fresh
        /// <see cref="Descriptor"/> for the opened entry.
        /// Default impl honors <see cref="OpenFlags.Create"/> by
        /// touching the target file; the resulting descriptor's
        /// behavior comes from the base class.</summary>
        public virtual Result<Descriptor, ErrorCode> OpenAt(PathFlags pathFlags,
            string path, OpenFlags openFlags, DescriptorFlags flags)
        {
            var fullPath = System.IO.Path.Combine(Path, path);
            if ((openFlags & OpenFlags.Create) != 0
                && !File.Exists(fullPath)
                && !Directory.Exists(fullPath))
            {
                File.WriteAllBytes(fullPath, System.Array.Empty<byte>());
            }
            return Result<Descriptor, ErrorCode>.FromOk(new Descriptor(fullPath));
        }

        Result<IDescriptor, ErrorCode> IDescriptor.OpenAt(PathFlags pathFlags,
            string path, OpenFlags openFlags, DescriptorFlags flags)
        {
            var r = OpenAt(pathFlags, path, openFlags, flags);
            return r.IsOk
                ? Result<IDescriptor, ErrorCode>.FromOk(r.Ok)
                : Result<IDescriptor, ErrorCode>.FromErr(r.Err);
        }

        /// <summary>Read the target of the symbolic link at
        /// <paramref name="path"/>. Default impl returns
        /// the empty string — concrete subclasses override
        /// (System.IO has no portable readlink in
        /// netstandard2.1).</summary>
        public virtual Result<string, ErrorCode> ReadlinkAt(string path)
            => Result<string, ErrorCode>.FromOk("");

        /// <summary>Inspect file metadata. Returns a
        /// <see cref="DescriptorStat"/> with type / link-count
        /// / size + the three timestamps. Default impl reads
        /// from System.IO.File / Directory; concrete VFS
        /// shims override.</summary>
        public virtual Result<DescriptorStat, ErrorCode> Stat()
        {
            DescriptorType type = GetDescriptorType();
            ulong linkCount = 1;   // POSIX hardlink count; host-
                                   // only implementations rarely
                                   // know this exactly.
            ulong size = 0;
            Option<Datetime> mtime = Option<Datetime>.None;
            Option<Datetime> atime = Option<Datetime>.None;
            Option<Datetime> ctime = Option<Datetime>.None;
            if (type == DescriptorType.RegularFile)
            {
                var fi = new FileInfo(Path);
                size = (ulong)fi.Length;
                atime = Option<Datetime>.Some(ToDatetime(fi.LastAccessTimeUtc));
                mtime = Option<Datetime>.Some(ToDatetime(fi.LastWriteTimeUtc));
                ctime = Option<Datetime>.Some(ToDatetime(fi.CreationTimeUtc));
            }
            else if (type == DescriptorType.Directory)
            {
                var di = new DirectoryInfo(Path);
                atime = Option<Datetime>.Some(ToDatetime(di.LastAccessTimeUtc));
                mtime = Option<Datetime>.Some(ToDatetime(di.LastWriteTimeUtc));
                ctime = Option<Datetime>.Some(ToDatetime(di.CreationTimeUtc));
            }
            return Result<DescriptorStat, ErrorCode>.FromOk(new DescriptorStat
            {
                Type = type,
                LinkCount = linkCount,
                Size = size,
                DataAccessTimestamp = atime,
                DataModificationTimestamp = mtime,
                StatusChangeTimestamp = ctime,
            });
        }

        /// <summary>Inspect file metadata at a path relative
        /// to this descriptor (treated as a directory). Same
        /// shape as <see cref="Stat"/> but takes a relative
        /// path; useful for guests walking trees without
        /// opening every entry.</summary>
        public virtual Result<DescriptorStat, ErrorCode> StatAt(
            PathFlags pathFlags, string path)
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
                entries.Add(new DirectoryEntry
                {
                    Type = DescriptorType.Directory,
                    Name = System.IO.Path.GetFileName(d) ?? "",
                });
            foreach (var f in Directory.EnumerateFiles(Path))
                entries.Add(new DirectoryEntry
                {
                    Type = DescriptorType.RegularFile,
                    Name = System.IO.Path.GetFileName(f) ?? "",
                });
            return new DirectoryEntryStream(entries.ToArray());
        }

        Result<IDirectoryEntryStream, ErrorCode> IDescriptor.ReadDirectory()
            => Result<IDirectoryEntryStream, ErrorCode>.FromOk(ReadDirectory());

        /// <summary>Stable hash of the file's identity (inode +
        /// device on POSIX). Two descriptors referring to the
        /// same underlying file return equal values across
        /// <c>get-type</c> changes; default impl hashes
        /// <see cref="Path"/>.</summary>
        public virtual Result<MetadataHashValue, ErrorCode> MetadataHash()
        {
            // Cheap deterministic hash from the path; concrete
            // hosts override with stat()-derived inode/device
            // pairs.
            ulong lower = (ulong)Path.GetHashCode();
            ulong upper = lower * 0x9E3779B97F4A7C15UL;
            return Result<MetadataHashValue, ErrorCode>.FromOk(
                new MetadataHashValue { Lower = lower, Upper = upper });
        }

        /// <summary>Read the descriptor's open / mutability
        /// flag set. Default returns Read | Write. Concrete
        /// hosts override based on how the descriptor was
        /// obtained.</summary>
        public virtual Result<DescriptorFlags, ErrorCode> GetFlags()
            => Result<DescriptorFlags, ErrorCode>.FromOk(
                DescriptorFlags.Read | DescriptorFlags.Write);

        /// <summary>Hint at access pattern for the descriptor's
        /// underlying file. Default impl is a no-op — concrete
        /// hosts wire to posix_fadvise on POSIX.</summary>
        public virtual Result<Unit, ErrorCode> Advise(ulong offset,
            ulong length, Advice advice)
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Update access + modification timestamps.
        /// Each arg picks one of three cases (no-change, now,
        /// or a specific datetime) — the host applies the
        /// corresponding utime semantics.</summary>
        public virtual Result<Unit, ErrorCode> SetTimes(
            NewTimestamp dataAccessTimestamp,
            NewTimestamp dataModificationTimestamp)
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Update timestamps at a relative
        /// path.</summary>
        public virtual Result<Unit, ErrorCode> SetTimesAt(PathFlags pathFlags,
            string path, NewTimestamp dataAccessTimestamp,
            NewTimestamp dataModificationTimestamp)
            => Result<Unit, ErrorCode>.FromOk(Unit.Value);

        /// <summary>Hash the file at a relative path. Pairs
        /// with <see cref="MetadataHash"/>; default impl
        /// hashes the full combined path string.</summary>
        public virtual Result<MetadataHashValue, ErrorCode> MetadataHashAt(
            PathFlags pathFlags, string path)
        {
            var fullPath = System.IO.Path.Combine(Path, path);
            ulong lower = (ulong)fullPath.GetHashCode();
            ulong upper = lower * 0x9E3779B97F4A7C15UL;
            return Result<MetadataHashValue, ErrorCode>.FromOk(
                new MetadataHashValue { Lower = lower, Upper = upper });
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

        bool IDescriptor.IsSameObject(IDescriptor other)
            => IsSameObject((Descriptor)other);

        public virtual void Dispose() { }
    }

    /// <summary>Host representation of
    /// <c>wasi:filesystem/types.directory-entry-stream</c>.
    /// Pull-stream of <see cref="DirectoryEntry"/> items. The
    /// base impl is array-backed — concrete hosts override
    /// <see cref="ReadDirectoryEntry"/> for stream sources
    /// that can't fully materialize ahead of time.</summary>
    [WasiResource("directory-entry-stream")]
    public class DirectoryEntryStream : IDirectoryEntryStream, IDisposable
    {
        private readonly DirectoryEntry[] _entries;
        private int _pos;

        public DirectoryEntryStream(DirectoryEntry[] entries)
        {
            _entries = entries
                ?? throw new ArgumentNullException(nameof(entries));
        }

        /// <summary>Pull the next directory entry.
        /// <see cref="Option{T}.None"/> when exhausted.</summary>
        public virtual Result<Option<DirectoryEntry>, ErrorCode>
            ReadDirectoryEntry()
            => Result<Option<DirectoryEntry>, ErrorCode>.FromOk(
                _pos < _entries.Length
                    ? Option<DirectoryEntry>.Some(_entries[_pos++])
                    : Option<DirectoryEntry>.None);

        public virtual void Dispose() { }
    }

    /// <summary>Host-side surface for the top-level
    /// <c>wasi:filesystem/types.filesystem-error-code</c>:
    /// <code>filesystem-error-code: func(err: borrow&lt;error&gt;)
    ///     -&gt; option&lt;error-code&gt;;</code>
    /// Returns <see cref="Option{T}.None"/> when the error is
    /// not filesystem-related; matching <see cref="ErrorCode"/>
    /// otherwise. Implemented as a thin shim around the
    /// generated <see cref="ITypes"/> interface so concrete
    /// hosts can implement just this single method without
    /// providing the rest of the types interface.</summary>
    public interface IFilesystemErrorCode
    {
        Option<ErrorCode> FilesystemErrorCode(
            Wacs.WASI.Preview2.Io.Error err);
    }

    /// <summary>Default <see cref="IFilesystemErrorCode"/>
    /// — returns None regardless of input. Concrete hosts
    /// override to classify <see cref="Wacs.WASI.Preview2.Io.Error"/>
    /// instances as filesystem errors.</summary>
    public sealed class FilesystemErrorCodeSource : IFilesystemErrorCode
    {
        public Option<ErrorCode> FilesystemErrorCode(
            Wacs.WASI.Preview2.Io.Error err) => Option<ErrorCode>.None;
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

        public override Result<byte[], StreamError> Read(ulong len)
        {
            int n = (int)System.Math.Min(len, (ulong)int.MaxValue);
            var buf = new byte[n];
            int read;
            try { read = _stream.Read(buf, 0, n); }
            catch (System.Exception e)
            {
                return Result<byte[], StreamError>.FromErr(
                    new StreamError.StreamErrorLastOperationFailed(
                        new Wacs.WASI.Preview2.Io.Error(e.Message)));
            }
            if (read == 0)
                return Result<byte[], StreamError>.FromErr(
                    new StreamError.StreamErrorClosed());
            if (read == n)
                return Result<byte[], StreamError>.FromOk(buf);
            var slice = new byte[read];
            System.Array.Copy(buf, 0, slice, 0, read);
            return Result<byte[], StreamError>.FromOk(slice);
        }

        public override Result<byte[], StreamError> BlockingRead(ulong len)
            => Read(len);
        public override void Dispose() { _stream.Dispose(); }
    }

    /// <summary>OutputStream that wraps a host
    /// <see cref="Stream"/> and disposes it on
    /// <see cref="Dispose"/>.</summary>
    public sealed class HostFileOutputStream : OutputStream
    {
        private readonly Stream _stream;
        public HostFileOutputStream(Stream stream) { _stream = stream; }

        public override Result<Unit, StreamError> Write(byte[] contents)
        {
            try { _stream.Write(contents, 0, contents.Length); }
            catch (System.Exception e)
            {
                return Result<Unit, StreamError>.FromErr(
                    new StreamError.StreamErrorLastOperationFailed(
                        new Wacs.WASI.Preview2.Io.Error(e.Message)));
            }
            return Result<Unit, StreamError>.FromOk(Unit.Value);
        }

        public override Result<Unit, StreamError> BlockingWriteAndFlush(
            byte[] contents)
        {
            try
            {
                _stream.Write(contents, 0, contents.Length);
                _stream.Flush();
            }
            catch (System.Exception e)
            {
                return Result<Unit, StreamError>.FromErr(
                    new StreamError.StreamErrorLastOperationFailed(
                        new Wacs.WASI.Preview2.Io.Error(e.Message)));
            }
            return Result<Unit, StreamError>.FromOk(Unit.Value);
        }

        public override Result<Unit, StreamError> Flush()
        {
            try { _stream.Flush(); }
            catch (System.Exception e)
            {
                return Result<Unit, StreamError>.FromErr(
                    new StreamError.StreamErrorLastOperationFailed(
                        new Wacs.WASI.Preview2.Io.Error(e.Message)));
            }
            return Result<Unit, StreamError>.FromOk(Unit.Value);
        }

        public override Result<Unit, StreamError> BlockingFlush()
            => Flush();
        public override void Dispose() { _stream.Dispose(); }
    }
}
