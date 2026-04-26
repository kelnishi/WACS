// Copyright 2026 Kelvin Nishikawa
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
//
//     http://www.apache.org/licenses/LICENSE-2.0

using System;
using System.IO;
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
        [WasiErrorResult]
        [WasiMethodName("get-type")]
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
        [WasiErrorResult]
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
        [WasiErrorResult]
        public virtual OutputStream WriteViaStream(ulong offset)
        {
            var stream = File.OpenWrite(Path);
            if (offset > 0)
                stream.Seek((long)offset, SeekOrigin.Begin);
            return new HostFileOutputStream(stream);
        }

        /// <summary>Open the file for appending. Returns an
        /// <see cref="OutputStream"/> positioned at EOF.</summary>
        [WasiErrorResult]
        public virtual OutputStream AppendViaStream()
        {
            var stream = new FileStream(Path, FileMode.Append,
                FileAccess.Write);
            return new HostFileOutputStream(stream);
        }

        /// <summary>Force any buffered modifications to be
        /// flushed to durable storage. Default: no-op (host
        /// streams flush on close anyway).</summary>
        [WasiErrorResult]
        public virtual void Sync() { }

        /// <summary>Like <see cref="Sync"/> but only flushes
        /// data, not metadata. Default: no-op.</summary>
        [WasiErrorResult]
        public virtual void SyncData() { }

        /// <summary>Truncate or extend the file to
        /// <paramref name="size"/> bytes.</summary>
        [WasiErrorResult]
        public virtual void SetSize(ulong size)
        {
            using var fs = new FileStream(Path, FileMode.Open,
                FileAccess.Write);
            fs.SetLength((long)size);
        }

        /// <summary>Write <paramref name="buffer"/> at
        /// <paramref name="offset"/>. Returns count actually
        /// written.</summary>
        [WasiErrorResult]
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
        [WasiErrorResult]
        [WasiMethodName("create-directory-at")]
        public virtual void CreateDirectoryAt(string path)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Path, path));
        }

        /// <summary>Remove the empty directory at
        /// <paramref name="path"/>.</summary>
        [WasiErrorResult]
        [WasiMethodName("remove-directory-at")]
        public virtual void RemoveDirectoryAt(string path)
        {
            Directory.Delete(System.IO.Path.Combine(Path, path));
        }

        /// <summary>Unlink (delete) the file at
        /// <paramref name="path"/>.</summary>
        [WasiErrorResult]
        [WasiMethodName("unlink-file-at")]
        public virtual void UnlinkFileAt(string path)
        {
            File.Delete(System.IO.Path.Combine(Path, path));
        }

        /// <summary>Hard-link <paramref name="oldPath"/> under
        /// <c>this</c> to <paramref name="newPath"/> under
        /// <paramref name="newDescriptor"/>. Default impl is a
        /// no-op since System.IO has no portable hard-link
        /// API; concrete subclasses override.</summary>
        [WasiErrorResult]
        [WasiMethodName("link-at")]
        public virtual void LinkAt(PathFlags oldPathFlags, string oldPath,
            Descriptor newDescriptor, string newPath) { }

        /// <summary>Rename <paramref name="oldPath"/> under
        /// <c>this</c> to <paramref name="newPath"/> under
        /// <paramref name="newDescriptor"/>.</summary>
        [WasiErrorResult]
        [WasiMethodName("rename-at")]
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
        [WasiErrorResult]
        [WasiMethodName("symlink-at")]
        public virtual void SymlinkAt(string oldPath, string newPath) { }

        /// <summary>Open or create a file/directory relative to
        /// this descriptor (treated as a directory) at
        /// <paramref name="path"/>. Returns a fresh
        /// <see cref="Descriptor"/> for the opened entry.
        /// Default impl honors <see cref="OpenFlags.Create"/> by
        /// touching the target file; the resulting descriptor's
        /// behavior comes from the base class.</summary>
        [WasiErrorResult]
        [WasiMethodName("open-at")]
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

        /// <summary>True iff <paramref name="other"/> refers to
        /// the same underlying filesystem object. Default
        /// compares the host-side <see cref="Path"/>; subclasses
        /// override for VFS shims that don't keep textual
        /// paths.</summary>
        [WasiMethodName("is-same-object")]
        public virtual bool IsSameObject(Descriptor other)
        {
            if (other == null) return false;
            return string.Equals(Path, other.Path, StringComparison.Ordinal);
        }

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
